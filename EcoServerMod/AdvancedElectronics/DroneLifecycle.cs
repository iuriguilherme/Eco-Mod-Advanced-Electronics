using System;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.LegislationSystem;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Eco-side glue (U8) driving the pure <see cref="AdvancedElectronics.Navigation.DroneStateMachine"/>
    /// from real tick events, <see cref="DroneDockObject"/>'s district assignment (U4), and
    /// <see cref="DroneMoverComponent"/>'s pathing results (U2/U3). Implements the
    /// dispatch / return / re-path / unreachable-status behavior for R6, R13, and R15.
    ///
    /// Per KTD3 (docs/solutions/best-practices/eco-013-server-driven-movement.md), the
    /// only tick surface proven to re-fire for mod callbacks is a
    /// <see cref="WorldObjectComponent"/>'s own <see cref="Tick"/> override -- so, like
    /// <see cref="DroneMoverComponent"/>, this class IS one, rather than trying to hook
    /// the mod-facing IWorldObjectManager.AddToTick surface.
    ///
    /// ASSUMPTION -- attachment/wiring, needs a future unit and/or live-server check:
    /// this component is designed to live on the same physical drone WorldObject as
    /// <see cref="DroneMoverComponent"/> (it reaches the mover via
    /// <c>this.Parent.TryGetComponent&lt;DroneMoverComponent&gt;()</c>), with
    /// <see cref="HomeDock"/> set to the pairing <see cref="DroneDockObject"/> by whichever
    /// future unit actually spawns that drone WorldObject and pairs it to its dock.
    /// No such WorldObject class exists yet in this codebase -- SurveyDroneItem (U1) is
    /// deliberately just an inventory Item today (see its own doc comment), and
    /// DroneMoverComponent.cs's doc comment already flags itself as "not yet wired into
    /// DroneDockObject's dispatch logic" pending this unit. U8's Files list covers the
    /// lifecycle/state-machine logic only, not that spawn/pairing wiring or a new
    /// WorldObject prefab class, so this component is written and left equally
    /// unattached (same pattern U2 already established), ready to be required/attached
    /// once that physical WorldObject exists. Until then, <see cref="Tick"/> safely
    /// no-ops whenever <see cref="HomeDock"/> is unset or no <see cref="DroneMoverComponent"/>
    /// is found on the same parent -- it never assumes either exists.
    /// </summary>
    [Serialized]
    [NoIcon]
    public class DroneLifecycle : WorldObjectComponent
    {
        // How long to wait between automatic return-to-dock retries while Unreachable,
        // to avoid re-running GridPathfinder.FindPath (an O(search area) operation)
        // every single tick while genuinely stuck. Not a correctness knob for the
        // state machine itself -- purely a pacing choice.
        private const float ReturnRetryIntervalSeconds = 5f;

        // ASSUMPTION -- verify against a live server: mirrors DroneMoverComponent's own
        // FallbackTickDeltaSeconds constant/reasoning (same file, same justification):
        // if IWorldObjectManager.TickDeltaTime ever reads as 0 (e.g. a very early tick
        // before the manager has measured a real interval), fall back to a plausible
        // interval instead of freezing the retry pacing forever.
        private const float FallbackTickDeltaSeconds = 0.05f;

        // Bounds for the in-district destination search (see
        // ResolveDestinationInDistrict) -- see that method's doc for why this search
        // exists at all.
        private const int DestinationSearchMaxRadius = 96;
        private const int DestinationSearchRingStep = 4;

        // Roam hop sizing (see TickSurveyRoam). Min exceeds OreSensorComponent's
        // SurveyCellSize (8) so consecutive roam points tend to land in different
        // survey cells and coverage actually spreads; max keeps each hop's A* cheap.
        private const float RoamStepMin = 8f;
        private const float RoamStepMax = 24f;
        private const int RoamPickAttempts = 8;
        // Pacing for retry after a failed pick round only -- a successful hop picks
        // its next destination immediately on arrival, no idle gap.
        private const float RoamRetryIntervalSeconds = 2f;

        // Backoff ceiling for consecutive failed roam rounds. A drone that cannot reach
        // any in-district hop (walled in, district shrank away from it) would otherwise
        // re-run RoamPickAttempts full A* searches every RoamRetryIntervalSeconds
        // forever; each search sweeps world-object queries per visited column, so a few
        // stuck drones become a standing server tax. The interval doubles per failed
        // round up to this cap and resets on any success.
        private const float RoamRetryMaxIntervalSeconds = 60f;

        private readonly DroneStateMachine stateMachine = new DroneStateMachine();
        private readonly Random roamRandom = new Random();
        private readonly EcoWorldSampler worldSampler = new EcoWorldSampler();

        private string lastKnownAssignedDistrictName;
        private float secondsSinceLastReturnRetry;
        private float secondsSinceLastRoamAttempt;
        private float roamRetryIntervalSeconds = RoamRetryIntervalSeconds;

        /// <summary>Current lifecycle status (R15) -- Idle/EnRoute/Surveying/Unreachable.</summary>
        public DroneStatus Status => this.stateMachine.Status;

        /// <summary>
        /// True only while Surveying -- U5's per-tick ore-sampling work (not built by
        /// this unit) should gate on this rather than duplicating the Idle/EnRoute/
        /// Unreachable exclusion itself (R6).
        /// </summary>
        public bool ShouldSample => this.stateMachine.ShouldSample;

        /// <summary>
        /// The dock this drone is paired to and dispatched from/returns to. Null until
        /// externally set at spawn/pairing time (see class doc's ASSUMPTION) -- every
        /// <see cref="Tick"/> checks for null first and no-ops rather than throwing, so
        /// an unpaired/unwired drone WorldObject is inert instead of crashing the
        /// server tick loop.
        /// </summary>
        public DroneDockObject HomeDock { get; set; }

        public override void Tick()
        {
            base.Tick();

            // IsDestroyed as well as null: a dock demolished while its drone is out
            // leaves the reference dangling, and every leg below paths to a dead dock's
            // coordinates. An orphaned drone goes inert instead of pathing forever.
            if (this.HomeDock == null || this.HomeDock.IsDestroyed)
                return;
            if (!this.Parent.TryGetComponent<DroneMoverComponent>(out var mover))
                return;

            var assignedName = this.HomeDock.AssignedDistrictName;

            if (!string.Equals(assignedName, this.lastKnownAssignedDistrictName, StringComparison.Ordinal))
            {
                this.lastKnownAssignedDistrictName = assignedName;

                if (!string.IsNullOrWhiteSpace(assignedName))
                {
                    // Covers Idle->EnRoute (fresh dispatch), Surveying->EnRoute and
                    // EnRoute(district)->EnRoute(new district) (R13 reassignment), and
                    // Unreachable->EnRoute (new reachable district assigned).
                    this.DispatchToDistrict(mover, assignedName);
                }
                else if (this.stateMachine.Status == DroneStatus.Surveying ||
                         (this.stateMachine.Status == DroneStatus.EnRoute && this.stateMachine.TravelTarget == DroneTravelTarget.District))
                {
                    // District cleared while actively pursuing one -- start the
                    // return-to-dock leg (R6: Surveying -> EnRoute(dock) -> Idle).
                    this.BeginReturnToDock(mover, viaDistrictCleared: true);
                }
                // else: cleared while already Idle/EnRoute(dock)/Unreachable -- nothing
                // was in progress to interrupt.

                return;
            }

            switch (this.stateMachine.Status)
            {
                case DroneStatus.EnRoute when this.stateMachine.TravelTarget == DroneTravelTarget.District:
                    if (!mover.IsMoving)
                    {
                        if (this.HomeDock.IsPositionInAssignedDistrict(this.Parent.Position))
                            this.stateMachine.OnArrived();
                        else
                            // Defensive: the path completed but membership test failed
                            // (e.g. the resolved destination point turned out to be
                            // just outside the district boundary). Treat like a failed
                            // dispatch rather than leaving the drone idle-in-place.
                            this.HandleNoPath(mover);
                    }
                    break;

                case DroneStatus.EnRoute when this.stateMachine.TravelTarget == DroneTravelTarget.Dock:
                    if (!mover.IsMoving)
                    {
                        if (ArrivalDetector.HasArrived(this.Parent.Position, new[] { this.HomeDock.Position }))
                            this.stateMachine.OnReturnedToDock();
                        else
                            this.HandleNoPath(mover);
                    }
                    break;

                case DroneStatus.Unreachable:
                    this.TickUnreachableRetry(mover);
                    break;

                case DroneStatus.Surveying:
                    // F2/R6: the drone ROAMS its district -- without this the drone
                    // arrives at one point and samples the same few columns forever,
                    // so coverage never grows and the survey never localizes anything.
                    // Per-tick sampling itself stays U5's concern (gated on
                    // ShouldSample); this only keeps the drone moving between
                    // in-district waypoints.
                    this.TickSurveyRoam(mover);
                    break;

                default:
                    // Idle: nothing to drive (R6 -- no district, no work).
                    break;
            }
        }

        /// <summary>
        /// Dispatches toward <paramref name="districtName"/> from the drone's CURRENT
        /// position (R13) -- never the dock. The state machine transitions immediately
        /// and optimistically (see <see cref="DroneStateMachine.OnDistrictAssigned"/>);
        /// path success/failure is reported back via a separate follow-up call, exactly
        /// mirroring the pure state machine's two-call contract.
        /// </summary>
        private void DispatchToDistrict(DroneMoverComponent mover, string districtName)
        {
            this.stateMachine.OnDistrictAssigned(districtName);

            var district = DistrictAssignment.FindDistrictByName(districtName);
            if (district == null)
            {
                // Name no longer resolves (renamed/deleted between assignment and this
                // tick) -- nothing to path to.
                this.HandleNoPath(mover);
                return;
            }

            // R13: the search below is anchored at this.Parent.Position -- the drone's
            // CURRENT position -- not HomeDock.Position. This is what makes a
            // reassignment re-path immediately from wherever the drone already is
            // instead of first returning home and re-dispatching from the dock.
            // (DroneMoverComponent.SetDestination independently always paths FROM the
            // current position too, so an anchoring mistake here couldn't route the
            // drone through the dock regardless -- but anchoring the destination
            // SEARCH here as well keeps the chosen in-district point close to the
            // drone rather than close to the dock.)
            var destination = ResolveDestinationInDistrict(district, this.Parent.Position);
            if (destination == null || !mover.SetDestination(destination.Value))
            {
                this.HandleNoPath(mover);
            }
        }

        /// <summary>
        /// Attempts to path back to the dock. On success, either fires
        /// <see cref="DroneStateMachine.OnDistrictCleared"/> (the normal
        /// Surveying/EnRoute(district) -> EnRoute(dock) transition) or -- when this is
        /// an Unreachable retry succeeding -- simply lets the drone start moving; the
        /// state machine has no dedicated "retry found a path" event (see
        /// <see cref="DroneStateMachine.OnNoPathFound"/>'s doc) and stays
        /// Unreachable-labeled until <see cref="TickUnreachableRetry"/> detects actual
        /// arrival. On failure, routes through <see cref="HandleNoPath"/> like every
        /// other no-path outcome.
        /// </summary>
        private void BeginReturnToDock(DroneMoverComponent mover, bool viaDistrictCleared)
        {
            // destinationIsOccupiedObject: the dock occupies its own column by design.
            var found = mover.SetDestination(this.HomeDock.Position, destinationIsOccupiedObject: true);
            if (found)
            {
                if (viaDistrictCleared)
                    this.stateMachine.OnDistrictCleared();
            }
            else
            {
                this.HandleNoPath(mover);
            }
        }

        /// <summary>
        /// A path attempt (to the district, or -- when already Unreachable/Surveying's
        /// immediate return leg -- to the dock) came back with no route. Drives the
        /// pure state machine's Unreachable transition and, per this unit's spec, also
        /// immediately attempts a return-to-dock leg in the SAME pass rather than
        /// waiting for the next periodic <see cref="TickUnreachableRetry"/> (AE9/R15).
        /// </summary>
        private void HandleNoPath(DroneMoverComponent mover)
        {
            mover.Stop();
            var shouldAttemptReturn = this.stateMachine.OnNoPathFound();
            if (shouldAttemptReturn)
            {
                this.AttemptReturnLegOnly(mover);
            }
        }

        /// <summary>
        /// Tries the return-to-dock path only -- no district lookup, no
        /// OnDistrictCleared. Used both by <see cref="HandleNoPath"/>'s immediate
        /// retry and by <see cref="TickUnreachableRetry"/>'s periodic retry.
        /// </summary>
        private void AttemptReturnLegOnly(DroneMoverComponent mover)
        {
            if (!mover.SetDestination(this.HomeDock.Position, destinationIsOccupiedObject: true))
            {
                // The return leg itself has no path either. OnNoPathFound() is
                // idempotent while already Unreachable (see its doc) -- safe to call
                // again; it does not re-signal a fresh return attempt.
                this.stateMachine.OnNoPathFound();
            }
        }

        /// <summary>
        /// While Unreachable: if a return path is currently in flight (a previous
        /// attempt succeeded and the mover is actively moving), watch for arrival.
        /// Otherwise, retry the return leg on a fixed interval rather than every tick.
        /// </summary>
        private void TickUnreachableRetry(DroneMoverComponent mover)
        {
            if (mover.IsMoving)
            {
                if (ArrivalDetector.HasArrived(this.Parent.Position, new[] { this.HomeDock.Position }))
                    this.stateMachine.OnReturnedToDock();
                return;
            }

            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            var deltaTime = manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;

            this.secondsSinceLastReturnRetry += deltaTime;
            if (this.secondsSinceLastReturnRetry < ReturnRetryIntervalSeconds)
                return;

            this.secondsSinceLastReturnRetry = 0f;
            this.AttemptReturnLegOnly(mover);
        }

        /// <summary>
        /// Keeps the drone roaming while Surveying (F2/R6). When the current hop has
        /// finished (<see cref="DroneMoverComponent.IsMoving"/> false), picks the next
        /// in-district waypoint: up to <see cref="RoamPickAttempts"/> random hops of
        /// <see cref="RoamStepMin"/>-<see cref="RoamStepMax"/> world units from the
        /// current position, each membership-tested via
        /// <see cref="DroneDockObject.IsPositionInAssignedDistrict"/> (the only proven
        /// district-geometry query -- see ResolveDestinationInDistrict's doc) and then
        /// path-tested via <see cref="DroneMoverComponent.SetDestination"/>. A fully
        /// failed round (e.g. the drone sits in a pocket, or the district shrank) falls
        /// back to ResolveDestinationInDistrict to pull the drone back inside, and
        /// retries are paced by <see cref="RoamRetryIntervalSeconds"/> so a stuck drone
        /// does not run pathfinding every tick. A drone that cannot roam merely stays
        /// put and keeps sampling its current columns -- never a state-machine error.
        /// </summary>
        private void TickSurveyRoam(DroneMoverComponent mover)
        {
            if (mover.IsMoving)
                return;

            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            var deltaTime = manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;
            this.secondsSinceLastRoamAttempt += deltaTime;
            if (this.secondsSinceLastRoamAttempt < this.roamRetryIntervalSeconds)
                return;
            this.secondsSinceLastRoamAttempt = 0f;

            var current = this.Parent.Position;
            for (var attempt = 0; attempt < RoamPickAttempts; attempt++)
            {
                var angle = this.roamRandom.NextDouble() * Math.PI * 2.0;
                var distance = RoamStepMin + (float)this.roamRandom.NextDouble() * (RoamStepMax - RoamStepMin);
                var candidate = new Vector3(
                    current.X + (float)Math.Cos(angle) * distance,
                    current.Y,
                    current.Z + (float)Math.Sin(angle) * distance);

                // Cheap rejections BEFORE the expensive pathfind: district membership is
                // a point test, and a candidate whose own column is solid/obstructed can
                // never be a valid destination now that the goal column is no longer
                // obstacle-exempt for roam hops. Both spare a full A* search per reject.
                if (!this.HomeDock.IsPositionInAssignedDistrict(candidate))
                    continue;
                var candidateX = (int)MathF.Round(candidate.X);
                var candidateZ = (int)MathF.Round(candidate.Z);
                if (this.worldSampler.IsSolidAt(candidateX, candidateZ) || this.worldSampler.IsObstacleAt(candidateX, candidateZ))
                    continue;

                if (mover.SetDestination(candidate))
                {
                    // Success: drop back to the fast cadence so the next arrival re-picks
                    // promptly, and let that arrival re-pick without waiting out an
                    // interval it did not need.
                    this.roamRetryIntervalSeconds = RoamRetryIntervalSeconds;
                    this.secondsSinceLastRoamAttempt = this.roamRetryIntervalSeconds;
                    return;
                }
            }

            // No random hop landed in-district with a path -- pull back toward the
            // district body (also covers "district was reassigned smaller under us").
            var districtName = this.HomeDock.AssignedDistrictName;
            var district = string.IsNullOrWhiteSpace(districtName) ? null : DistrictAssignment.FindDistrictByName(districtName);
            if (district != null)
            {
                var fallback = ResolveDestinationInDistrict(district, current);
                if (fallback != null && mover.SetDestination(fallback.Value))
                {
                    this.roamRetryIntervalSeconds = RoamRetryIntervalSeconds;
                    return;
                }
            }

            // Nothing reachable this round. Back off so a permanently-stuck drone costs
            // the server a bounded, decaying amount of pathfinding instead of a fixed
            // per-2s tax forever.
            this.roamRetryIntervalSeconds = MathF.Min(this.roamRetryIntervalSeconds * 2f, RoamRetryMaxIntervalSeconds);
        }

        /// <summary>
        /// Finds a point believed to be inside <paramref name="district"/>, anchored at
        /// <paramref name="anchor"/>. ASSUMPTION / placeholder heuristic: no district
        /// bounds/geometry accessor is confirmed available (see
        /// docs/solutions/best-practices/eco-013-reading-district-civics-data.md --
        /// only point-membership testing (<see cref="DistrictAssignment.IsPositionInDistrict"/>)
        /// is proven), and U8's Dependencies (U2/U3/U4) don't include U5's survey-grid
        /// work, which is the unit actually expected to walk a district's cells. This
        /// does a bounded expanding-ring search using ONLY the proven point-membership
        /// test -- correct but not necessarily efficient or exact, and a reasonable
        /// future replacement is U5's own grid-cell enumeration once it exists. Returns
        /// null if nothing within <see cref="DestinationSearchMaxRadius"/> world units
        /// of the anchor tests as inside the district.
        /// </summary>
        private static Vector3? ResolveDestinationInDistrict(District district, Vector3 anchor)
        {
            if (DistrictAssignment.IsPositionInDistrict(anchor, district))
                return anchor;

            for (var radius = DestinationSearchRingStep; radius <= DestinationSearchMaxRadius; radius += DestinationSearchRingStep)
            {
                for (var dx = -radius; dx <= radius; dx += DestinationSearchRingStep)
                {
                    var north = new Vector3(anchor.X + dx, anchor.Y, anchor.Z - radius);
                    if (DistrictAssignment.IsPositionInDistrict(north, district))
                        return north;

                    var south = new Vector3(anchor.X + dx, anchor.Y, anchor.Z + radius);
                    if (DistrictAssignment.IsPositionInDistrict(south, district))
                        return south;
                }

                for (var dz = -radius + DestinationSearchRingStep; dz <= radius - DestinationSearchRingStep; dz += DestinationSearchRingStep)
                {
                    var west = new Vector3(anchor.X - radius, anchor.Y, anchor.Z + dz);
                    if (DistrictAssignment.IsPositionInDistrict(west, district))
                        return west;

                    var east = new Vector3(anchor.X + radius, anchor.Y, anchor.Z + dz);
                    if (DistrictAssignment.IsPositionInDistrict(east, district))
                        return east;
                }
            }

            return null;
        }
    }
}
