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
        private const float MaxStepHeight = 1f;

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

        public override void Initialize()
        {
            base.Initialize();
            this.pathfinder = new GridPathfinder(new EcoWorldSampler(), MaxStepHeight);
        }

        /// <summary>
        /// Computes a path from the drone's current position to
        /// <paramref name="destination"/> and, if one is found, starts
        /// stepping toward it on subsequent ticks. Returns false (leaving the
        /// drone idle/unchanged) when no path exists -- callers must check
        /// this rather than assume dispatch always succeeds (mirrors
        /// GridPathfinder.FindPath's own Found-checking contract).
        /// </summary>
        /// <param name="destinationIsOccupiedObject">
        /// True when the caller knows the destination column is legitimately occupied by
        /// a WorldObject it intends to path into — i.e. the return-to-dock leg targeting
        /// the dock itself. Roam and district-dispatch destinations pass false so the
        /// drone never parks inside another player's object (it could not roam back out).
        /// </param>
        public bool SetDestination(Vector3 destination, bool destinationIsOccupiedObject = false)
        {
            var result = this.pathfinder.FindPath(this.Parent.Position, destination, destinationIsOccupiedObject);
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

        public override void Tick()
        {
            base.Tick();

            // Push MoveSpeed on movement transitions (and once initially so the key
            // always exists after the first tick) — before the early return below, or
            // the moving->stationary transition would never be pushed.
            var moving = this.IsMoving;
            if (!this.hasPushedMoveSpeed || moving != this.lastPushedMoving)
            {
                this.hasPushedMoveSpeed = true;
                this.lastPushedMoving = moving;
                this.Parent.SetAnimatedState(MoveSpeedStateName, moving ? MoveSpeedMetersPerSecond : 0f);
            }

            if (!this.IsMoving)
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
            var step = MoveSpeedMetersPerSecond * deltaTime;

            var nextPos = distance <= step || distance < 0.0001f
                ? target
                : current + Vector3.Normalize(toTarget) * step;

            this.Parent.Position = nextPos;
            if (distance >= 0.0001f)
                this.Parent.Rotation = Quaternion.LookRotation(Vector3.Normalize(toTarget));
            this.Parent.SyncPositionAndRotation();
        }
    }
}
