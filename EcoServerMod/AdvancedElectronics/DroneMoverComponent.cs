using System;
using System.Collections.Generic;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;
using Eco.Shared.Serialization;
using Quaternion = Eco.Shared.Math.Quaternion;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Drives a WorldObject's movement along a self-computed path, one step per
    /// server component tick (U2, R1).
    ///
    /// Per KTD3 / docs/solutions/best-practices/eco-013-server-driven-movement.md
    /// (proven live on a 0.13.0.4 dedicated server), this component's own
    /// <see cref="Tick"/> override is the ONLY working movement mechanism in
    /// this codebase -- the mod-facing IWorldObjectManager.AddToTick /
    /// ITickOnDemand surface fires exactly once and never re-queues for mod
    /// callbacks. Movement must never be driven from that surface; this class
    /// deliberately does not use it. Position/rotation are written directly and
    /// pushed to clients via WorldObject.SyncPositionAndRotation() every tick,
    /// mirroring the spike's proven SpikeMoveCommand.cs (SpikeMover.TickOnDemand)
    /// shape.
    ///
    /// Pathfinding itself is fully delegated to
    /// AdvancedElectronics.Navigation.GridPathfinder (U3, covered by
    /// GridPathfinderTests.cs) -- this component only consumes
    /// PathResult/ArrivalDetector to decide where to step next each tick; it
    /// does not reimplement any pathing logic, and that logic is not
    /// re-tested here (see this unit's "Test scenarios" note).
    ///
    /// Not yet wired into DroneDockObject's dispatch logic -- that is U8 (drone
    /// lifecycle) per DroneDockObject.cs's own scope note. SetDestination() is this
    /// unit's entry point for exercising the mover independently (e.g. from a
    /// future debug command or U8's dispatch code); no chat command surface is
    /// added here.
    /// </summary>
    [Serialized]
    [NoIcon]
    public class DroneMoverComponent : WorldObjectComponent
    {
        // Design constants (not unverified live APIs -- these are tunable
        // choices, not ASSUMPTIONs about Eco's behavior).
        private const float MoveSpeedMetersPerSecond = 3f;

        /// <summary>
        /// How fast the drone climbs and descends when a leg is essentially straight up or down.
        ///
        /// Cruise speed applied to a vertical leg is what made leaving and reaching the dock read
        /// as a teleport: the climb off the pad and the drop onto it are only a metre or two, so
        /// at 3 m/s they resolve inside a fraction of a second while a four-second animation is
        /// still playing around them. Nothing was snapping -- the motion was simply too fast to
        /// see. A rotorcraft changes altitude far more slowly than it crosses ground, so a
        /// separate vertical rate is also the honest model, not just the prettier one.
        /// </summary>
        private const float VerticalSpeedMetersPerSecond = 0.75f;

        /// <summary>
        /// Cruise speed for an ordinary leg, the slower vertical rate for one that is more up or
        /// down than along. Compares squared magnitudes so no square root is needed; a perfectly
        /// diagonal leg counts as horizontal, which is the conservative choice -- it keeps
        /// ordinary travel at full speed and slows only what actually reads as a climb or a drop.
        /// </summary>
        private static float SpeedFor(Vector3 leg)
        {
            var horizontalSquared = (leg.X * leg.X) + (leg.Z * leg.Z);
            var verticalSquared   = leg.Y * leg.Y;
            return verticalSquared > horizontalSquared
                ? VerticalSpeedMetersPerSecond
                : MoveSpeedMetersPerSecond;
        }

        // Fallback delta-time if IWorldObjectManager.TickDeltaTime ever reads
        // as 0 (e.g. an early tick before the manager has measured a real
        // interval) -- keeps the drone advancing instead of stalling forever.
        // 50ms mirrors the spike's timer-strategy interval (SpikeMoveCommand.cs).
        private const float FallbackTickDeltaSeconds = 0.05f;

        /// <summary>
        /// Animation-state contract name (v1 closure plan KTD1): the drone's current
        /// movement speed in world units/second — the mover's constant while moving,
        /// 0 when stationary. The client prefab declares this name in its FloatStates
        /// array; future art binds an animator Speed parameter to it. Frozen — renaming
        /// touches server, prefab, and bundle at once.
        /// </summary>
        internal const string MoveSpeedStateName = "MoveSpeed";

        private GridPathfinder pathfinder;
        private PathResult currentPath = PathResult.NotFound;
        private int waypointIndex;
        private bool lastPushedMoving;
        private bool hasPushedMoveSpeed;

        /// <summary>True while a path is active and the drone has not yet reached its final waypoint.</summary>
        public bool IsMoving => this.currentPath.Found && this.waypointIndex < this.currentPath.Waypoints.Count;

        // Climb height is per-attempt rather than a constant (R11): the return-leg escalation
        // rebuilds the pathfinder at a looser height when a dock-bound path cannot be found.
        // Outbound legs never change it, so they behave exactly as before.
        private float maxStepHeight = ReturnEscalation.OrdinaryMaxStepHeight;

        /// <summary>
        /// How far above the ground the drone flies, in blocks.
        ///
        /// The pathfinder's default is one block -- the cell a walking entity stands in. A drone
        /// routed at that height skims the terrain and reads as something dragging along the
        /// floor rather than flying. Routing still solves on the ground grid, so this only lifts
        /// where the drone is drawn along the route it already chose; obstacles and step heights
        /// are judged exactly as before, and the drone still descends to the pad on arrival
        /// because docking uses the dock's park point rather than a routed waypoint.
        /// </summary>
        private const float CruiseHeightAboveGround = 2.5f;

        public override void Initialize()
        {
            base.Initialize();
            this.pathfinder = new GridPathfinder(new EcoWorldSampler(), this.maxStepHeight,
                                                 standingHeightOffset: CruiseHeightAboveGround);
        }

        /// <summary>
        /// Rebuilds the pathfinder at <paramref name="height"/> if it differs from the current
        /// one. Cheap to call every attempt: an unchanged height is a no-op, so the ordinary
        /// case never rebuilds.
        /// </summary>
        public void SetClimbHeight(float height)
        {
            if (Math.Abs(height - this.maxStepHeight) < 0.0001f) return;

            this.maxStepHeight = height;
            this.pathfinder    = new GridPathfinder(new EcoWorldSampler(), height,
                                                    standingHeightOffset: CruiseHeightAboveGround);
        }

        /// <summary>
        /// Heads straight for <paramref name="destination"/> with no pathfinding at all -- the
        /// hover and clip rungs of the return ladder (R11). The drone crosses whatever is in the
        /// way rather than routing around it, which is the point: these rungs only run when
        /// routing has already failed.
        /// </summary>
        public void SetDirectDestination(Vector3 destination)
        {
            this.currentPath   = PathResult.Success(new[] { destination });
            this.waypointIndex = 0;
        }

        /// <summary>
        /// Places the drone at <paramref name="destination"/> immediately, stopping any movement.
        /// The last rung of the return ladder, and the reason a return leg never fails.
        /// </summary>
        public void TeleportTo(Vector3 destination)
        {
            this.Stop();
            this.Parent.Position = destination;
            this.Parent.SyncPositionAndRotation();
        }

        /// <summary>
        /// Computes a path from the drone's current position to
        /// <paramref name="destination"/> and, if one is found, starts
        /// stepping toward it on subsequent ticks. Returns false (leaving the
        /// drone idle/unchanged) when no path exists -- callers must check
        /// this rather than assume dispatch always succeeds (mirrors
        /// GridPathfinder.FindPath's own Found-checking contract).
        /// </summary>
        /// <param name="exemptOccupiedColumns">
        /// The world columns of an object the caller knows the drone may legitimately
        /// stand on and cross — in practice its own dock's footprint. Passed on every
        /// leg, not only the homebound one: a drone parked on a multi-cell pad has to
        /// cross the rest of the pad to leave it, and every pad cell reports occupied.
        /// Null for a destination that belongs to nobody, so the drone never parks inside
        /// another player's object (it could not roam back out).
        /// </param>
        /// <param name="landAtDestination">
        /// True to finish exactly at <paramref name="destination"/> rather than at cruise
        /// height above it. Every routed waypoint sits at the flying altitude, so a landing
        /// target -- the dock's park point -- would otherwise be approached and then reached
        /// by some other means, which is what made docking snap downward. Appending the real
        /// destination turns that drop into an ordinary final leg, flown at the same speed as
        /// the rest.
        /// </param>
        /// <param name="climbOnDeparture">
        /// True to rise straight up to travelling altitude before setting off, rather than
        /// leaving on the diagonal of the first routed leg. The mirror of
        /// <paramref name="landAtDestination"/>, and wanted for the same reason: every routed
        /// waypoint sits at cruise height, so a drone parked lower gains all of that altitude as
        /// a side effect of its first horizontal move. Splitting the climb out makes it a leg of
        /// its own, which <see cref="SpeedFor"/> then flies slowly.
        /// </param>
        public bool SetDestination(Vector3 destination, IReadOnlyCollection<Vector3> exemptOccupiedColumns = null,
                                   bool landAtDestination = false, bool climbOnDeparture = false)
        {
            var result = this.pathfinder.FindPath(this.Parent.Position, destination, exemptOccupiedColumns);

            if (result.Found && climbOnDeparture && result.Waypoints.Count > 0)
            {
                // Straight up from where it stands to the altitude the route begins at. Skipped
                // when the route already starts at this height, so a drone that is level with
                // its path does not gain a zero-length waypoint it would tick through anyway.
                var start = this.Parent.Position;
                var routeHeight = result.Waypoints[0].Y;

                if (Math.Abs(routeHeight - start.Y) > 0.0001f)
                {
                    var withClimb = new List<Vector3> { new Vector3(start.X, routeHeight, start.Z) };
                    withClimb.AddRange(result.Waypoints);
                    result = PathResult.Success(withClimb);
                }
            }

            if (result.Found && landAtDestination)
            {
                var withLanding = new List<Vector3>(result.Waypoints) { destination };
                result = PathResult.Success(withLanding);
            }

            this.currentPath = result;
            this.waypointIndex = 0;
            return result.Found;
        }

        /// <summary>Clears the current path; the drone stops advancing on the next tick.</summary>
        public void Stop()
        {
            this.currentPath = PathResult.NotFound;
            this.waypointIndex = 0;
        }

        /// <summary>
        /// Keeps the drone still for <paramref name="seconds"/> without discarding its path.
        ///
        /// The animator's transition clips are one-shots with real length -- take-off runs
        /// through mode-select, arm-select and lift-off before the flying loop begins -- and
        /// the drone was sliding away while all of that was still playing. Pausing here rather
        /// than delaying the path keeps a failed route reported at the moment it is asked for,
        /// instead of surfacing seconds later from somewhere the caller has already left.
        /// </summary>
        public void HoldFor(float seconds)
        {
            if (seconds > this.holdRemainingSeconds) this.holdRemainingSeconds = seconds;
        }

        /// <summary>True while a lead-in animation is playing and the drone must not advance.</summary>
        public bool IsHolding => this.holdRemainingSeconds > 0f;

        private float holdRemainingSeconds;

        public override void Tick()
        {
            base.Tick();

            if (this.holdRemainingSeconds > 0f)
            {
                var holdManager = ServiceHolder<IWorldObjectManager>.Obj;
                this.holdRemainingSeconds -= holdManager != null && holdManager.TickDeltaTime > 0f
                    ? holdManager.TickDeltaTime
                    : FallbackTickDeltaSeconds;
            }

            // Push MoveSpeed on movement transitions (and once initially so the key
            // always exists after the first tick) — before the early return below, or
            // the moving->stationary transition would never be pushed.
            //
            // A held drone reports stationary: it has a path but is not travelling along it,
            // and any speed-driven art should show it standing still while the lead-in plays.
            var moving = this.IsMoving && !this.IsHolding;
            if (!this.hasPushedMoveSpeed || moving != this.lastPushedMoving)
            {
                this.hasPushedMoveSpeed = true;
                this.lastPushedMoving = moving;
                this.Parent.SetAnimatedState(MoveSpeedStateName, moving ? MoveSpeedMetersPerSecond : 0f);
            }

            if (!this.IsMoving || this.IsHolding)
                return;

            var current = this.Parent.Position;
            var target = this.currentPath.Waypoints[this.waypointIndex];

            // ArrivalDetector.HasArrived (U3) compares the current position
            // against the LAST element of whatever waypoint list it is given.
            // Wrapping just the upcoming waypoint in a one-element list lets
            // this component reuse that same check per-waypoint, not only for
            // the whole path's final stop.
            if (ArrivalDetector.HasArrived(current, new[] { target }))
            {
                this.waypointIndex++;
                if (this.waypointIndex >= this.currentPath.Waypoints.Count)
                {
                    this.Stop();
                    return;
                }
                target = this.currentPath.Waypoints[this.waypointIndex];
            }

            var toTarget = target - current;
            var distance = toTarget.Length();

            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            var deltaTime = manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;
            var step = SpeedFor(toTarget) * deltaTime;

            var nextPos = distance <= step || distance < 0.0001f
                ? target
                : current + Vector3.Normalize(toTarget) * step;

            this.Parent.Position = nextPos;

            // Yaw only: face where it is going horizontally, and stay level.
            //
            // Rotating toward the full 3D heading pitched the whole drone nose-up on every
            // climb, which suited an entity walking up a slope and does not suit a rotorcraft
            // -- a helicopter holds its attitude and changes altitude underneath it. A purely
            // vertical move leaves the heading untouched rather than snapping to some
            // arbitrary facing.
            var heading = new Vector3(toTarget.X, 0f, toTarget.Z);
            if (heading.LengthSquared() >= 0.0001f)
                this.Parent.Rotation = Quaternion.LookRotation(Vector3.Normalize(heading));

            this.Parent.SyncPositionAndRotation();
        }
    }
}
