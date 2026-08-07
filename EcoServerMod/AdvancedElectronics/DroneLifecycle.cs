using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;
using Eco.Shared.Serialization;
using Eco.Shared.Voxel;

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

        // Park-and-sweep tuning (see TickSurveyParkAndSweep). While parked in a plot the drone
        // sweeps one plot-row of columns per tick, so a plot is fully surveyed over ~plotSide ticks
        // -- bounded per-tick cost, matching the old sampler's conservative one-column pacing while
        // actually covering the whole plot. If a plot center cannot be reached after this many
        // arrival attempts the plot is skipped, so a walled-off or non-contiguous plot never stalls
        // the sweep of the rest of the area.
        private const int MaxPlotArrivalAttempts = 5;

        private readonly DroneStateMachine stateMachine = new DroneStateMachine();

        private string lastKnownAssignedArea;
        private float secondsSinceLastReturnRetry;

        // Park-and-sweep progress across the assigned area's plots. Rebuilt whenever the drone is
        // (re)dispatched to an area (sweepInitialized reset in DispatchToArea).
        private List<PlotCoord> sweepPlots;
        private int sweepPlotIndex;
        private int sweepColumnCursor;
        private int sweepArrivalAttempts;
        private bool sweepInitialized;

        // How far the return leg has escalated (R11). Only the dock-bound leg climbs this ladder;
        // outbound failures still report Unreachable, because failing to reach a survey area is a
        // normal outcome and failing to get home is not.
        private ReturnTier returnTier = ReturnTier.Normal;

        /// <summary>Current lifecycle status (R15) -- Idle/EnRoute/Surveying/Unreachable.</summary>
        public DroneStatus Status => this.stateMachine.Status;

        /// <summary>
        /// True while the drone is doing work that costs fuel, dock parts, and its own condition
        /// (R9): travelling to the assigned area, or surveying it. The dock-bound leg is
        /// deliberately excluded, so a shortage can never strand the drone that same shortage
        /// recalled.
        /// </summary>
        // NOT [Serialized]: this is derived from the state machine every time it is read, so
        // there is nothing to write back into on load. Eco's serializer needs a settable member.
        // Animator-facing booleans like this one are pushed live via SetAnimatedState (see
        // DroneAnimationState) -- they are signals, not saved state.
        public bool IsWorking =>
            this.stateMachine.Status == DroneStatus.Surveying
            || (this.stateMachine.Status == DroneStatus.EnRoute
                && this.stateMachine.TravelTarget == DroneTravelTarget.District);

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

        /// <summary>
        /// Human-readable record of the last dispatch/roam decision, surfaced by
        /// <c>/drone status</c>. Exists so a single live session distinguishes the
        /// failure modes that all collapse into the same Unreachable status —
        /// destination-not-resolved (district not found or empty) versus
        /// destination-resolved-but-no-path — instead of costing a restart per guess.
        /// </summary>
        public string LastDispatchNote { get; private set; } = "no dispatch yet";

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

            // Before every early return below, not after the switch. The unserviceable gate and
            // the assignment-change branch both return, and those are exactly the transitions the
            // art most needs to show -- a drone recalled for lack of fuel that kept playing its
            // survey animation would be the most confusing possible readout. Pushing here costs
            // one tick of latency (the states describe the situation as of the start of this
            // tick), which is the same trade DroneMoverComponent already makes for MoveSpeed.
            this.RefreshAnimationStates(mover);

            // The drone surveys the dock's assigned survey area (KTD9). The token is opaque to the
            // state machine — it only drives change-detection and the target label. It encodes the
            // area id AND an edit epoch, so editing the assigned area's plots changes the token and
            // re-dispatches the drone (fresh pathfinding + sweep of the new shape) exactly like an
            // unassign+reassign.
            var assignedToken = this.HomeDock.AssignedAreaToken;

            // Serviceability gates dispatch, not assignment (R10, R12). An unserviceable dock keeps
            // its assignment -- so the panel can say "out of fuel" rather than looking unassigned --
            // and recalls a drone that is already out. Refuelling or repairing resumes the same area
            // with its coverage intact (R13), because nothing about the assignment was discarded.
            //
            // This runs before the change-detection below on purpose: becoming unserviceable is not
            // an assignment change, so it would otherwise not be noticed until the player touched
            // the assignment.
            if (!this.HomeDock.IsServiceable)
            {
                if (this.IsWorking) this.BeginReturnToDock(mover, viaDistrictCleared: true);

                // Remember the token anyway, so regaining service re-dispatches through the normal
                // change path rather than needing the player to reassign.
                this.lastKnownAssignedArea = null;
                return;
            }

            if (!string.Equals(assignedToken, this.lastKnownAssignedArea, StringComparison.Ordinal))
            {
                this.lastKnownAssignedArea = assignedToken;

                if (!string.IsNullOrWhiteSpace(assignedToken))
                {
                    // Idle/Surveying/EnRoute -> EnRoute to the newly-assigned area (R13
                    // reassignment re-paths immediately from the drone's current position).
                    this.DispatchToArea(mover);
                }
                else if (this.stateMachine.Status == DroneStatus.Surveying ||
                         (this.stateMachine.Status == DroneStatus.EnRoute && this.stateMachine.TravelTarget == DroneTravelTarget.District))
                {
                    // Area unassigned while actively pursuing one -- start the return-to-dock
                    // leg (R6: Surveying -> EnRoute(dock) -> Idle). DroneTravelTarget.District
                    // is the state machine's generic "toward the assigned region" target.
                    this.BeginReturnToDock(mover, viaDistrictCleared: true);
                }
                // else: unassigned while already Idle/EnRoute(dock)/Unreachable -- nothing
                // was in progress to interrupt.

                return;
            }

            switch (this.stateMachine.Status)
            {
                case DroneStatus.EnRoute when this.stateMachine.TravelTarget == DroneTravelTarget.District:
                    if (!mover.IsMoving)
                    {
                        if (this.IsPositionInAssignedRegion(this.Parent.Position))
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
                        if (this.IsAtHomeDock())
                        {
                            this.stateMachine.OnReturnedToDock();
                            this.ResetReturnLadder(mover);
                        }
                        else
                            this.HandleNoPath(mover);
                    }
                    break;

                case DroneStatus.Unreachable:
                    this.TickUnreachableRetry(mover);
                    break;

                case DroneStatus.Surveying:
                    // F2/R6: the drone visits each plot of its area in turn, parking at the plot
                    // centre and surveying the whole plot's grid (park-and-sweep) before moving on,
                    // then returns to the dock when every plot is covered.
                    if (this.Parent.TryGetComponent<OreSensorComponent>(out var sensor))
                        this.TickSurveyParkAndSweep(mover, sensor);
                    break;

                default:
                    // Idle: nothing to drive (R6 -- no district, no work).
                    break;
            }
        }

        /// <summary>
        /// Last value pushed per animation state name, so a state is only written when it
        /// actually changes. <c>SetAnimatedState</c> is a synced write and this runs every tick;
        /// pushing five unchanged booleans per tick per drone is pure network churn with no
        /// consumer. Mirrors DroneDockObject's <c>lastPushedWorking</c>, widened to a map because
        /// there are five of them.
        ///
        /// Not serialized on purpose: an empty map after a restart means the first tick re-pushes
        /// everything, which is what a freshly-connected client needs anyway.
        /// </summary>
        private readonly Dictionary<string, bool> lastPushedAnimationStates = new Dictionary<string, bool>();

        /// <summary>
        /// Projects the current lifecycle situation onto the drone's five animation booleans and
        /// pushes the ones that changed.
        ///
        /// The mapping itself lives in <see cref="DroneAnimationState.From"/> over in the
        /// dependency-free navigation project, so it is unit-testable without a server; this
        /// method is only the Eco-side plumbing -- gather inputs, push deltas.
        /// </summary>
        private void RefreshAnimationStates(DroneMoverComponent mover)
        {
            // Home is a position question, not an assignment one (KD3). A drone that finished
            // its area and flew back is still assigned, so an assignment-derived flag would
            // never read as home again. A drone with no dock at all is treated as not home --
            // it is stranded, and the flying loop is the honest animation for that.
            var atHomeDock = this.HomeDock != null
                             && !this.HomeDock.IsDestroyed
                             && this.IsAtHomeDock();

            var state = DroneAnimationState.From(
                this.stateMachine.Status,
                mover.IsMoving,
                atHomeDock,
                usesHarvestTool: false);

            foreach (var (name, value) in state.AsNamedValues())
            {
                if (this.lastPushedAnimationStates.TryGetValue(name, out var last) && last == value)
                    continue;

                this.lastPushedAnimationStates[name] = value;
                this.Parent.SetAnimatedState(name, value);
            }
        }

        /// <summary>
        /// Dispatches toward the dock's assigned survey area from the drone's CURRENT position
        /// (R13) -- never the dock, so a reassignment re-paths immediately from wherever the
        /// drone already is. The state machine transitions immediately and optimistically (see
        /// <see cref="DroneStateMachine.OnDistrictAssigned"/> -- its generic "region assigned"
        /// event, named from the pre-area design); path success/failure is reported back via a
        /// separate follow-up call. The target is a synthetic "area:&lt;id&gt;" label; membership
        /// and geometry come from the area's own plots.
        /// </summary>
        private void DispatchToArea(DroneMoverComponent mover)
        {
            this.stateMachine.OnDistrictAssigned("area:" + this.HomeDock.AssignedSurveyAreaId);

            // A (re)dispatch starts a fresh sweep of the newly-targeted area's plots.
            this.sweepInitialized = false;

            // No survey reset on reassign (KTD11): findings are per-area and persist with the
            // area, so switching the drone between areas keeps each area's own data. The sensor
            // attributes new samples to whichever area is assigned; a reassign is just a new
            // attribution target, not a wipe.
            var entry = this.HomeDock.AssignedSurveyArea;
            if (entry == null)
            {
                this.LastDispatchNote = "assigned survey area did not resolve";
                this.HandleNoPath(mover);
                return;
            }

            var destination = ResolveDestinationInArea(entry.ToSurveyArea(), this.Parent.Position);
            if (destination == null)
            {
                this.LastDispatchNote = $"survey area '{entry.Name}' has no plot to target";
                this.HandleNoPath(mover);
                return;
            }

            var target = destination.Value;
            // The dock's columns are exempt on the way OUT as well as the way home: the
            // drone starts parked on the pad, and every pad cell reports occupied, so
            // without this its first step off the dock has nowhere legal to go. The
            // target itself is not in the set, so this cannot make an occupied
            // destination legal -- a survey point inside someone's building still fails.
            if (!mover.SetDestination(target, this.HomeDock.OccupiedColumns))
            {
                this.LastDispatchNote = $"no path from {this.Parent.Position.X:F0},{this.Parent.Position.Z:F0} to area point {target.X:F0},{target.Z:F0}";
                this.HandleNoPath(mover);
                return;
            }

            this.LastDispatchNote = $"dispatched to area point {target.X:F0},{target.Z:F0}";
        }

        /// <summary>
        /// True when the drone's assigned survey area contains <paramref name="pos"/> (U8/KTD9).
        /// The single membership seam every arrival/roam check goes through.
        /// </summary>
        private bool IsPositionInAssignedRegion(Vector3 pos) =>
            this.HomeDock.IsPositionInAssignedArea(pos);

        /// <summary>
        /// The point inside <paramref name="area"/> nearest <paramref name="anchor"/>, by
        /// enumerating the area's
        /// own plots and converting each to its world center via
        /// <see cref="PlotPos.CenterWorldPos"/>. Returns the anchor unchanged when it is already
        /// inside the area. No distance cap; keeps the anchor's Y (the pathfinder is
        /// ground-following, so only X/Z matter).
        /// </summary>
        private static Vector3? ResolveDestinationInArea(SurveyArea area, Vector3 anchor)
        {
            if (area.ContainsWorldColumn((int)MathF.Round(anchor.X), (int)MathF.Round(anchor.Z), PlotUtil.PropertyPlotLength))
                return anchor;

            var bestDistanceSq = float.MaxValue;
            Vector3? best = null;

            foreach (var plot in area.EnumeratePlots())
            {
                var center = new PlotPos(plot.X, plot.Z).CenterWorldPos;
                var dx = center.x - anchor.X;
                var dz = center.y - anchor.Z;
                var distanceSq = (dx * dx) + (dz * dz);
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                best = new Vector3(center.x, anchor.Y, center.y);
            }

            return best;
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
            // The dock occupies its own columns by design, so the drone has to be allowed
            // through them to dock at all.
            var found = mover.SetDestination(this.HomeDock.Position, this.HomeDock.OccupiedColumns);
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
            if (this.TryReturnAt(ReturnEscalation.For(this.returnTier), mover)) return;

            // This rung failed. Loosen by exactly one and leave the looser rung for the next
            // retry: walking the whole ladder in a single pass would teleport the drone the
            // first time a path lookup came back empty, which is not "progressively relax".
            if (ReturnEscalation.TryNext(this.returnTier, out var next))
                this.returnTier = next.Tier;

            // OnNoPathFound() is idempotent while already Unreachable (see its doc) -- safe to
            // call again; it does not re-signal a fresh return attempt.
            this.stateMachine.OnNoPathFound();
        }

        /// <summary>
        /// One rung of the return ladder (R11). Ordinary rungs re-path at a looser climb height;
        /// the hover and clip rungs abandon pathing for a straight line; the last rung places the
        /// drone at the dock. Returns false only when this rung found nothing to do.
        /// </summary>
        private bool TryReturnAt(ReturnAttempt attempt, DroneMoverComponent mover)
        {
            if (attempt.Teleports)
            {
                mover.TeleportTo(this.HomeDock.DroneParkPosition);
                this.stateMachine.OnReturnedToDock();
                this.ResetReturnLadder(mover);
                return true;
            }

            if (attempt.IgnoresObstacles)
            {
                // No pathfinding left to fail: a straight line to the dock, through whatever is
                // in the way. Only reached after routing has already failed at every tighter rung.
                mover.SetDirectDestination(this.HomeDock.DroneParkPosition);
                return true;
            }

            mover.SetClimbHeight(attempt.MaxStepHeight);
            return mover.SetDestination(this.HomeDock.Position, this.HomeDock.OccupiedColumns);
        }

        /// <summary>
        /// Puts the ladder and the mover's climb height back to ordinary. Called whenever the
        /// drone is home or is sent out again, so a hard-won return never leaves the next
        /// outbound leg pathing with relaxed constraints.
        /// </summary>
        private void ResetReturnLadder(DroneMoverComponent mover)
        {
            this.returnTier = ReturnTier.Normal;
            mover.SetClimbHeight(ReturnEscalation.OrdinaryMaxStepHeight);
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
                if (this.IsAtHomeDock())
                {
                    this.stateMachine.OnReturnedToDock();
                    this.ResetReturnLadder(mover);
                }
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
        /// Has the drone made it home? Compares HORIZONTAL distance to the dock, not the
        /// exact 3D point.
        ///
        /// The drone can never occupy the dock's own position: it stands one cell above
        /// the ground surface, while the dock's Position is its placement point, and the
        /// dock's block occupies the column the drone is walking into. An exact 3D
        /// distance check against a 0.1 tolerance therefore never succeeds -- the drone
        /// would walk home, stop, fail the check, and get routed to HandleNoPath into
        /// Unreachable instead of docking (R6/R15). Horizontal proximity is the honest
        /// test for "arrived at the dock".
        /// </summary>
        public bool IsAtHomeDock()
        {
            var drone = this.Parent.Position;
            var dock = this.HomeDock.Position;
            var dx = drone.X - dock.X;
            var dz = drone.Z - dock.Z;
            return ((dx * dx) + (dz * dz)) <= DockArrivalRadius * DockArrivalRadius;
        }

        /// <summary>
        /// Horizontal distance within which the drone counts as docked. Sized so the
        /// drone standing in an adjacent column (the dock occupies its own) still
        /// registers as home.
        /// </summary>
        private const float DockArrivalRadius = 2f;

        /// <summary>
        /// Park-and-sweep survey pass (F2/R6). Instead of roaming random points and sampling only
        /// the columns the path happened to cross, the drone visits each plot of its assigned area
        /// in raster order: it paths to the plot's centre, and once standing in the plot it sweeps
        /// EVERY column of that plot (<paramref name="sensor"/>.SampleColumn), one plot-row per tick,
        /// then advances to the next plot. When every plot is covered it returns to the dock.
        ///
        /// Parking at the centre and scanning the whole plot means the drone's physical footprint is
        /// irrelevant (the roaming WorldObject is a placeholder size), and visiting plots discretely
        /// -- rather than continuously pathing between waypoints -- keeps a non-contiguous area's
        /// disjoint plots independent: an unreachable plot is skipped, not a stall.
        /// </summary>
        private void TickSurveyParkAndSweep(DroneMoverComponent mover, OreSensorComponent sensor)
        {
            if (!this.sweepInitialized)
            {
                var entry = this.HomeDock.AssignedSurveyArea;
                if (entry == null)
                    return;

                // Raster order (by Z then X) gives a stable, roughly lawn-mower visitation.
                this.sweepPlots = entry.ToSurveyArea().EnumeratePlots()
                    .OrderBy(p => p.Z).ThenBy(p => p.X)
                    .ToList();
                this.sweepPlotIndex = 0;
                this.sweepColumnCursor = 0;
                this.sweepArrivalAttempts = 0;
                this.sweepInitialized = true;
            }

            if (this.sweepPlots == null || this.sweepPlots.Count == 0)
                return;

            // Every plot covered -> the survey pass is done; head home (Surveying -> EnRoute(dock)).
            if (this.sweepPlotIndex >= this.sweepPlots.Count)
            {
                this.LastDispatchNote = "survey complete -- returning to dock";
                this.BeginReturnToDock(mover, viaDistrictCleared: true);
                return;
            }

            var plotSide = PlotUtil.PropertyPlotLength;
            var plot = this.sweepPlots[this.sweepPlotIndex];
            var pos = this.Parent.Position;

            var dronePlot = PlotCoord.FromWorldColumn(
                (int)MathF.Round(pos.X), (int)MathF.Round(pos.Z), plotSide);

            if (!dronePlot.Equals(plot))
            {
                // Travelling to this plot's centre. Keep going while already moving.
                if (mover.IsMoving)
                    return;

                var center = new PlotPos(plot.X, plot.Z).CenterWorldPos;
                var reachable = mover.SetDestination(new Vector3(center.x, pos.Y, center.y), this.HomeDock.OccupiedColumns);
                this.sweepArrivalAttempts++;

                if (!reachable || this.sweepArrivalAttempts > MaxPlotArrivalAttempts)
                {
                    // Unreachable / can't settle inside it -> skip so the rest of the area still gets
                    // surveyed (covers non-contiguous islands and walled-off plots).
                    this.LastDispatchNote = $"skipped unreachable plot {plot.X},{plot.Z}";
                    this.AdvancePlot();
                }
                return;
            }

            // Parked in the target plot: sweep one row of its columns this tick. A plot is
            // PropertyPlotLength on a side (5) -> PropertyPlotArea (25) columns in its 2D grid.
            this.sweepArrivalAttempts = 0;
            var total = PlotUtil.PropertyPlotArea;
            var baseX = plot.X * plotSide;
            var baseZ = plot.Z * plotSide;
            var record = this.HomeDock.SurveyRecord;
            var areaId = this.HomeDock.AssignedSurveyAreaId;

            for (var k = 0; k < plotSide && this.sweepColumnCursor < total; k++, this.sweepColumnCursor++)
            {
                var dx = this.sweepColumnCursor % plotSide;
                var dz = this.sweepColumnCursor / plotSide;
                sensor.SampleColumn(baseX + dx, baseZ + dz, record, areaId);
            }

            if (this.sweepColumnCursor >= total)
            {
                this.LastDispatchNote = $"swept plot {this.sweepPlotIndex + 1}/{this.sweepPlots.Count}";
                this.AdvancePlot();
            }
        }

        /// <summary>Advances the park-and-sweep cursor to the next plot.</summary>
        private void AdvancePlot()
        {
            this.sweepPlotIndex++;
            this.sweepColumnCursor = 0;
            this.sweepArrivalAttempts = 0;
        }
    }
}
