using System;
using System.Collections.Generic;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Logging;
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

        /// <summary>
        /// Frame rate the HRVSTR clips were authored at. Every lead-in below is quoted in
        /// frames, taken from the FBX importer's clip ranges, so re-timing the animation is a
        /// change to one number here plus the frame counts -- not a hunt for magic seconds.
        /// </summary>
        private const float AnimationFramesPerSecond = 30f;

        private static float AnimationSeconds(int frames) => frames / AnimationFramesPerSecond;

        /// <summary>
        /// How long the drone stays put after being dispatched, so the sequence that plays on
        /// the pad can finish before it travels: 05_Mode_Select (44) + the arm select (59, the
        /// longer of mining and harvest).
        ///
        /// 01_Flying_Start (119) is deliberately NOT waited out. It is the lift-off, and a
        /// lift-off the drone spends motionless is a lift-off that visibly does nothing --
        /// followed by the drone gaining its altitude afterwards, in a fraction of a second,
        /// which is what read as a teleport upward. Releasing the hold here instead lets the
        /// climb happen underneath the clip, and the climb is flown at the mover's vertical
        /// rate so it takes about as long as the clip does.
        ///
        /// KNOWN, SHIPPED IN 0.2.0, NOT A MYSTERY: this trades one artifact for another. The
        /// jump is gone and the drone now visibly leaves before the take-off has finished,
        /// because releasing the hold releases horizontal travel along with the climb. The fix
        /// is to gate those separately -- hold horizontal movement for the full 44 + 59 + 119
        /// while letting the vertical leg run -- which the mover already has the pieces for
        /// (climbOnDeparture makes the climb its own leg, SpeedFor flies it slowly). Deferred
        /// past the release rather than reverted, because reverting only restores the jump.
        /// </summary>
        private static readonly float TakeOffLeadInSeconds = AnimationSeconds(44 + 59);

        /// <summary>
        /// How long the drone stays put before travelling home or to the next area, so it
        /// finishes putting its arm away: the work stop (44) + 17_WorkMode_ToFlying (59).
        /// </summary>
        private static readonly float WorkExitLeadInSeconds = AnimationSeconds(44 + 59);

        /// <summary>
        /// How long the drone waits after parking before its first removal, so the arm is out
        /// before the ground changes. The exit transition's mirror, and the same length.
        ///
        /// Without it the first block went on the tick the drone arrived, while the client was
        /// still playing the flying loop: the terrain changed and the work animation caught up
        /// afterwards. The survey drone had the same gap and nobody could see it, because a
        /// survey changes nothing in the world to be early against.
        /// </summary>
        private static readonly float WorkEntryLeadInSeconds = AnimationSeconds(44 + 59);

        private readonly DroneStateMachine stateMachine = new DroneStateMachine();

        private string lastKnownAssignedArea;
        private float secondsSinceLastReturnRetry;

        // Deliberately NOT serialized: it must read false on every load, because that is exactly
        // when the state machine and the drone's persisted position can disagree.
        private bool hasReconciledPositionAfterLoad;

        // How many dispatches in a row have failed to get the drone moving, and how long it has
        // been parked since the budget ran out. See CannotReachAssignedArea.
        private int consecutiveDispatchFailures;
        private float secondsSinceDispatchBlocked;

        /// <summary>
        /// How many failed dispatches in a row before the drone stops trying. Small on purpose:
        /// a dispatch that fails from the pad fails for a reason that will not change between one
        /// tick and the next, and the whole point is to notice that quickly rather than to
        /// persevere.
        /// </summary>
        private const int MaxConsecutiveDispatchFailures = 3;

        /// <summary>
        /// How many plots one dispatch may probe before giving up for this tick. Comfortably above
        /// the 40-plot area cap, so in practice it bounds a pathological case rather than a real
        /// one -- an entirely unreachable area costs one pathfind per plot, once, and is then
        /// parked by the dispatch budget rather than re-probed every tick.
        /// </summary>
        private const int MaxPlotsProbedPerDispatch = 64;

        /// <summary>
        /// Whether the current assignment has ever got the drone moving toward a plot. Separates
        /// "this area cannot be entered at all" from "the plots that are left cannot be entered",
        /// which want different reports: the first is the area's problem, the second is the tail
        /// of an otherwise normal run.
        /// </summary>
        private bool reachedAssignedAreaOnce;

        /// <summary>
        /// How long a blocked drone waits before trying once more. Long, because the only thing
        /// that can change the answer is the world -- terrain filled in, an obstruction removed --
        /// and none of that happens on a tick boundary.
        /// </summary>
        private const float BlockedDispatchRetryIntervalSeconds = 60f;

        /// <summary>
        /// True once the drone has given up reaching its assigned area for now: it is parked at
        /// its dock, still assigned, and no longer retrying every tick.
        ///
        /// This exists because "parked" and "cannot get there" are different facts and the status
        /// enum only carries one of them. Without it the panel had to choose between reporting the
        /// physical truth (Idle, at the dock) and the useful one (unreachable), and the code that
        /// flip-flopped between them is what the player saw as a drone docking and undocking
        /// once a second forever.
        /// </summary>
        public bool CannotReachAssignedArea => this.consecutiveDispatchFailures >= MaxConsecutiveDispatchFailures;

        // The current job strategy (KTD3) -- what "one tick's work" at a parked plot means,
        // and which plot is next. Recreated fresh on every (re)dispatch (see DispatchToArea),
        // which is what resets its internal plot-list progress; the lifecycle itself keeps
        // no plot-list state of its own.
        private IJobStrategy strategy;

        // The plot whose work lead-in has already played, so the arm is not re-deployed on every
        // tick of the same plot. Null whenever the drone is not parked and working.
        private PlotCoord? workEntryPlot;

        // How many consecutive arrival attempts at the CURRENT target plot have failed. Owned
        // by the lifecycle (KTD3: "the lifecycle keeps... the arrival-attempt counter and its
        // cap"), reset whenever a new target is reached or a target is abandoned.
        private int plotArrivalAttempts;

        // How far the return leg has escalated (R11). Only the dock-bound leg climbs this ladder;
        // outbound failures still report Unreachable, because failing to reach a survey area is a
        // normal outcome and failing to get home is not.
        private ReturnTier returnTier = ReturnTier.Normal;

        /// <summary>Current lifecycle status (R15) -- Idle/EnRoute/Surveying/Unreachable.</summary>
        public DroneStatus Status => this.stateMachine.Status;

        /// <summary>What the drone is travelling toward -- the half of EnRoute that says which way.</summary>
        public DroneTravelTarget TravelTarget => this.stateMachine.TravelTarget;

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
            this.stateMachine.Status == DroneStatus.OnStation
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
            if (!this.Parent.TryGetComponent<DroneMoverComponent>(out var mover))
                return;

            // Above the dock guard, not below it. An orphaned drone still has an animator,
            // and leaving it frozen on whatever it was last doing -- with both mode booleans
            // false, which the controller's mode-select has no branch for -- is worse than
            // showing it hovering. RefreshAnimationStates handles a missing dock itself.
            this.RefreshAnimationStates(mover);

            // The push above sits before every early return below, not after the switch. The
            // unserviceable gate and the assignment-change branch both return, and those are
            // exactly the transitions the art most needs to show -- a drone recalled for lack
            // of fuel that kept playing its survey animation would be the most confusing
            // possible readout. Pushing there costs one tick of latency (the states describe
            // the situation as of the start of this tick), which is the same trade
            // DroneMoverComponent already makes for MoveSpeed.

            // The dock guard this class's doc comment has always promised, and which was missing
            // until the first live restart found it. Everything below dereferences HomeDock, and it
            // is null in two real situations:
            //
            //   - The first tick after a world load. The drone WorldObject persists but its HomeDock
            //     is a live reference that does not, so the dock re-attaches it from the serialized
            //     object id in DroneDockObject.RestoreDroneLinkOnce, on the DOCK's first tick.
            //     WorldObjectManager.TickAll iterates objects with a plain ForEach and guarantees no
            //     ordering between a drone and its dock, so the drone can and does tick first. One
            //     inert tick, then the dock re-links and the next tick proceeds normally.
            //   - A dock demolished while its drone was out, which leaves the reference dangling
            //     (hence IsDestroyed as well as null -- every leg below paths to a dead dock's
            //     coordinates). An orphaned drone goes inert instead of pathing forever.
            //
            // Below RefreshAnimationStates on purpose: an orphaned drone still has an animator, and
            // freezing it mid-pose with both mode booleans false -- a combination the controller's
            // mode-select has no branch for -- reads worse than showing it hovering.
            if (this.HomeDock == null || this.HomeDock.IsDestroyed) return;

            // The drone works whichever area its own declared tool assigns it to (KTD9, U10):
            // a survey area for a survey drone, a mining area reference for a mining drone.
            // The token is opaque to the state machine — it only drives change-detection and
            // the target label. It encodes the assignment AND an edit/reassignment epoch, so
            // editing or reassigning changes the token and re-dispatches the drone (fresh
            // pathfinding + fresh strategy) exactly like an unassign+reassign.
            var assignedToken = this.CurrentAssignedToken();

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

                // A new assignment is a fresh chance: whatever made the last one unreachable
                // says nothing about this one.
                this.consecutiveDispatchFailures = 0;
                this.secondsSinceDispatchBlocked = 0f;
                this.reachedAssignedAreaOnce = false;

                if (!string.IsNullOrWhiteSpace(assignedToken))
                {
                    // Idle/Surveying/EnRoute -> EnRoute to the newly-assigned area (R13
                    // reassignment re-paths immediately from the drone's current position).
                    this.DispatchToArea(mover);
                }
                else
                {
                    // Unassigned. Drop the strategy: it describes work on an area this dock no
                    // longer has, and leaving it alive is what let R24's idle-at-dock resume
                    // re-dispatch a drone that had just been told to stop.
                    this.strategy = null;

                    if (this.stateMachine.Status == DroneStatus.OnStation ||
                        (this.stateMachine.Status == DroneStatus.EnRoute && this.stateMachine.TravelTarget == DroneTravelTarget.District))
                    {
                        // Area unassigned while actively pursuing one -- start the return-to-dock
                        // leg (R6: Surveying -> EnRoute(dock) -> Idle). DroneTravelTarget.District
                        // is the state machine's generic "toward the assigned region" target.
                        this.BeginReturnToDock(mover, viaDistrictCleared: true);
                    }
                    else
                    {
                        // Idle/EnRoute(dock)/Unreachable: nothing was in progress to interrupt,
                        // but the drone may be sitting somewhere it should not be -- settle it.
                        this.SettleAtDockIfHome(mover);
                    }
                }

                return;
            }

            // Reconcile the freshly-loaded state machine against where the drone physically is.
            //
            // The drone's POSITION is serialized; the state machine is not -- it is a plain field
            // and comes back Idle, which means "parked at the dock with nothing to do". A drone
            // that was out working when the server saved therefore loads believing it is home
            // while hovering over its work area, and nothing ever contradicts that: with no
            // assignment the change-detection above sees null == null and takes no branch, and
            // Idle's own branch only acts on a drone that IS at its dock.
            //
            // An assigned drone is rescued by accident -- its token reads as a change against the
            // null field and re-dispatches it -- which is why this only ever showed up on
            // unassigned drones, left hovering over someone else's excavation until picked up by
            // hand.
            //
            // Placed after the change-detection so a dispatch always wins: that branch returns
            // before this point, and by the next tick the drone is EnRoute rather than Idle.
            if (!this.hasReconciledPositionAfterLoad)
            {
                this.hasReconciledPositionAfterLoad = true;

                if (this.stateMachine.Status == DroneStatus.Idle && !this.IsAtHomeDock())
                {
                    this.LastDispatchNote = "returning to dock after a server restart left it away";
                    this.BeginReturnToDock(mover, viaDistrictCleared: false);
                    return;
                }
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
                            this.strategy?.OnArrivedHome();
                            this.ResetReturnLadder(mover);
                        }
                        else
                            this.HandleNoPath(mover);
                    }
                    break;

                case DroneStatus.Unreachable:
                    this.TickUnreachableRetry(mover);
                    break;

                case DroneStatus.OnStation:
                    // F2/R6/KTD3: the drone visits each plot of its area in turn, parking at the
                    // plot centre and doing one tick of the job strategy's own work there (survey
                    // sampling, or -- once a mining strategy exists -- a mining tick) before moving
                    // on, then returns to the dock when the strategy reports no plots remain.
                    this.TickOnStation(mover);
                    break;

                default:
                    // Idle. R24: a drone at the dock with an unfinished job is a dispatch
                    // condition, not a terminal state -- after an unload, ask the SAME
                    // strategy instance for its next target rather than waiting for the
                    // assigned-area token to change (which would also reset shaft progress).
                    //
                    // Gated on there still BEING an assignment. Without that, unassigning a drone
                    // whose strategy had not finished put it in a loop: it flew home, reached
                    // Idle, was re-dispatched by this branch, failed to resolve an area it no
                    // longer had, went Unreachable, and settled hovering over its own pad. It
                    // burned no fuel and looked docked to the panel, so the only visible symptom
                    // was that the docking animation never played -- Unreachable is not Idle, and
                    // the animator's at-home flag requires Idle.
                    if (this.strategy != null && !this.strategy.IsComplete && this.IsAtHomeDock()
                        && !string.IsNullOrWhiteSpace(assignedToken)
                        && this.DispatchBudgetAllowsRetry())
                        this.DispatchToArea(mover);
                    break;
            }
        }

        /// <summary>
        /// Last value pushed per animation state name, so a state is only written when it
        /// actually changes. <c>SetAnimatedState</c> is a synced write and this runs every tick;
        /// pushing four unchanged booleans per tick per drone is pure network churn with no
        /// consumer. Mirrors DroneDockObject's <c>lastPushedWorking</c>, widened to a map because
        /// there are four of them.
        ///
        /// Not serialized on purpose: an empty map after a restart means the first tick re-pushes
        /// everything, which is what a freshly-connected client needs anyway.
        /// </summary>
        private readonly Dictionary<string, bool> lastPushedAnimationStates = new Dictionary<string, bool>();

        /// <summary>
        /// Seconds between forced re-pushes of every animation state, regardless of change.
        ///
        /// Change-gating alone loses any state that never changes again. The engine delivers an
        /// animated state only to clients that hold the object at the moment it is written -- there
        /// is no replay of current values when a client later builds the object (the client's
        /// InvokeChangedStateEvent runs on change, and nothing re-sends the standing value). A
        /// drone's tool booleans are written once, on its first tick, and are constant forever
        /// after; a player who was not already watching that exact tick therefore never receives
        /// them, and the animator sits in mode-select waiting for a branch that will never come.
        ///
        /// Re-pushing the whole set periodically costs four synced writes per drone per interval
        /// and repairs any client that arrived late, reconnected, or streamed the object in from
        /// out of view.
        /// </summary>
        private const float AnimationStateRepushIntervalSeconds = 5f;

        private float secondsSinceAnimationRepush;

        /// <summary>
        /// TEMPORARY DIAGNOSTIC. While true, every genuine change to a drone's animation
        /// booleans is announced in chat to everyone online, so the contract can be watched
        /// live instead of sampled with /drone status.
        ///
        /// Toggled by <c>/drone animwatch</c>. Off by default, and static rather than
        /// per-drone so one toggle covers every drone in the world.
        ///
        /// Delete this and its command once the animation work is settled -- it is a
        /// debugging aid, not a feature, and it broadcasts to every player on the server.
        /// </summary>
        internal static bool AnnounceAnimationStateChanges;

        /// <summary>
        /// What was last ANNOUNCED, kept apart from what was last PUSHED.
        ///
        /// The push cache is cleared periodically to force a re-send, which would otherwise
        /// read as four fresh "changes" every interval and bury the real transitions in
        /// repeats. This one is never cleared, so chat only reports a value that actually
        /// moved.
        /// </summary>
        private readonly Dictionary<string, bool> lastAnnouncedAnimationStates = new Dictionary<string, bool>();

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
            //
            // Position AND idleness. A dispatched drone is no longer "at home" even while it
            // is still standing on the pad: the take-off sequence has to start playing before
            // it can travel, and gating that on movement would mean the drone must move before
            // the animation that covers moving is allowed to begin.
            //
            // The projection resolves "home AND working" separately -- a survey plot beside the
            // dock falls inside the arrival radius -- and it owns that rule because it can be
            // tested there.
            var atHomeDock = this.IsAtHomeDock()
                             && this.stateMachine.Status == DroneStatus.Idle;

            // A drone that does not declare a tool animates with the mining arm rather than
            // freezing at mode-select, so a drone class added later that forgets the
            // interface still moves -- visibly wrong beats visibly broken.
            var usesHarvestTool = this.Parent is IDroneToolbearer toolbearer
                                  && toolbearer.Tool == DroneTool.Harvest;

            var state = DroneAnimationState.From(
                this.stateMachine.Status,
                mover.IsMoving,
                atHomeDock,
                usesHarvestTool);

            // Periodically forget what was pushed, so the next loop re-sends everything even
            // if nothing changed. See AnimationStateRepushIntervalSeconds: a state that never
            // changes again reaches nobody who was not already holding the object when it was
            // first written.
            var deltaTime = TickDeltaSeconds();

            this.secondsSinceAnimationRepush += deltaTime;
            if (this.secondsSinceAnimationRepush >= AnimationStateRepushIntervalSeconds)
            {
                this.secondsSinceAnimationRepush = 0f;
                this.lastPushedAnimationStates.Clear();
            }

            foreach (var (name, value) in state.AsNamedValues())
            {
                this.AnnounceIfChanged(name, value);

                if (this.lastPushedAnimationStates.TryGetValue(name, out var last) && last == value)
                    continue;

                this.lastPushedAnimationStates[name] = value;
                this.Parent.SetAnimatedState(name, value);
            }
        }

        /// <summary>
        /// TEMPORARY DIAGNOSTIC. Announces a genuine change in one animation boolean, so the
        /// server/client contract can be watched as it happens rather than sampled after the
        /// fact. See <see cref="AnnounceAnimationStateChanges"/>; delete both together.
        ///
        /// Reports the first observation as well as later flips: "never pushed" and "pushed
        /// false" look identical from the animator's side but are different faults, and the
        /// first announcement is the only evidence that separates them.
        /// </summary>
        private void AnnounceIfChanged(string name, bool value)
        {
            if (!AnnounceAnimationStateChanges) return;

            var known = this.lastAnnouncedAnimationStates.TryGetValue(name, out var previous);
            if (known && previous == value) return;

            this.lastAnnouncedAnimationStates[name] = value;

            var drone = this.Parent?.Name ?? "drone";
            var what  = known ? $"{previous} -> {value}" : $"first push: {value}";

            // Colour so a transition is findable in a scrolling chat log: green went true,
            // orange went false. Rich text in chat is the same shape the Gift Machine mod
            // uses, which is where this delivery pattern comes from.
            var colour  = value ? "green" : "orange";
            var message = $"<color=\"{colour}\">[anim] {drone}: {name} {what}</color>";

            // Player, not User: an online player's session is what actually receives chat,
            // and that is the call the Gift Machine mod proved. The owner rather than
            // everyone online, because UserManager.OnlineUsers is typed from an assembly
            // this mod does not reference. An unowned or offline drone still reports below.
            if (this.Parent is IDroneOwnable ownable && ownable.HasOwner)
                UserManager.FindUserByID(ownable.OwnerId)?.Player?.MsgLocStr(message);

            Log.WriteLineLoc($"[anim] {drone}: {name} {what}");
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
            // R42: checked before every dispatch, not only at each plot arrival --
            // MiningStrategy's own re-check (TryGetNextTarget) covers the latter, but a
            // halted job must never even leave the dock for its first hop.
            if (this.CurrentJobKind() == DroneJobKind.Mining && MiningHalt.IsHalted)
            {
                this.LastDispatchNote = "mining is halted server-wide";
                return;
            }

            this.stateMachine.OnDistrictAssigned("job:" + this.CurrentAssignedToken());

            // Only build a fresh strategy when there is none, or the previous one finished --
            // a resume dispatch (R24: idle-at-dock with an unfinished job) reuses the SAME
            // instance so shaft/sweep progress survives the round trip home.
            //
            // IsExhausted, not IsComplete: a full hold reads complete (there is nothing to offer
            // right now) but must NOT discard the strategy, or the resume trip rebuilds the shaft
            // plan against the excavated floor and starts a second shaft inside the first.
            //
            // ...and the third condition, which those two used to be assumed to cover. A
            // strategy captures its area reference at construction, so one built for a previous
            // area keeps consulting THAT area however many times the dock is reassigned. An
            // unfinished strategy is neither null nor exhausted, so assigning a new area reused
            // it wholesale: the drone flew to the new area's plots and asked the old area
            // whether they were surveyed. Reassigning was therefore useless as a recovery from
            // any stuck job -- the exact remedy a player reaches for first.
            //
            // Compared by area id rather than by the assignment token, because the token also
            // changes on a plain server restart (this field is not serialized), and rebuilding
            // there would discard a ledger the restart is supposed to preserve.
            var assignedAreaId = this.HomeDock.AssignedMiningArea?.AreaId;
            var strategyIsForAnotherArea =
                assignedAreaId != null &&
                this.strategy is MiningStrategy builtStrategy &&
                builtStrategy.SourceAreaId != assignedAreaId.Value;

            var needsFreshStrategy =
                this.strategy == null || this.strategy.IsExhausted || strategyIsForAnotherArea;

            var area = this.CurrentTargetArea(out var noAreaReason, out var terminalReason);
            if (area == null)
            {
                this.LastDispatchNote = noAreaReason;

                // A vanished or redrawn area is not a routing failure and must not be retried
                // like one.
                if (terminalReason != null)
                    this.EndJobForMissingArea(mover, terminalReason.Value);
                else
                    this.FailDispatch(mover);

                return;
            }

            if (needsFreshStrategy)
            {
                this.strategy = this.BuildStrategy(area);
                this.plotArrivalAttempts = 0;
            }

            if (this.strategy == null)
            {
                this.LastDispatchNote = "no job strategy for this drone's declared tool";
                this.FailDispatch(mover);
                return;
            }

            // Ask the STRATEGY where to go, not the area.
            //
            // This used to path to the nearest plot centre in the whole area, which had two
            // consequences. A mining drone flew to whichever plot was closest whether or not it
            // had been surveyed, ignoring the survey/mined freshness its own NextPlot enforces.
            // And a plot the pathfinder could not enter was chosen again on every dispatch, since
            // retiring it in the strategy changed nothing about what "nearest" meant -- so one
            // walled-off plot stalled the entire area.
            //
            // Now each unreachable plot is retired here and the next one tried, in the strategy's
            // own order. Retired immediately rather than after MaxPlotArrivalAttempts: within a
            // single tick the world and the drone's position are both fixed, so re-asking the
            // pathfinder the same question can only get the same answer. The plot-to-plot hop in
            // TickOnStation keeps its own retry count, where the drone HAS moved in between.
            Vector3? started = null;
            var plotsProbed = 0;

            while (this.strategy.TryGetNextTarget(out var plot))
            {
                if (++plotsProbed > MaxPlotsProbedPerDispatch) break;

                var centre = new PlotPos(plot.X, plot.Z).CenterWorldPos;
                var candidate = new Vector3(centre.x, this.Parent.Position.Y, centre.y);

                // The dock's columns are exempt on the way OUT as well as the way home: the
                // drone starts parked on the pad, and every pad cell reports occupied, so
                // without this its first step off the dock has nowhere legal to go. The
                // target itself is not in the set, so this cannot make an occupied
                // destination legal -- a survey point inside someone's building still fails.
                //
                // Leaving the dock climbs before it travels (climbOnDeparture): the pad sits below
                // travelling altitude, so without a climb leg of its own the drone would gain that
                // height on the diagonal of its first horizontal step -- in a fraction of a second,
                // which is what read as a jump. Only from the dock; a drone lifting off a work area
                // is already at the height its route continues from.
                if (mover.SetDestination(candidate, this.HomeDock.OccupiedColumns,
                                         climbOnDeparture: this.IsAtHomeDock()))
                {
                    started = candidate;
                    break;
                }

                this.strategy.OnArrivalFailed();
            }

            if (started == null)
            {
                // Nothing to go to. Before reading that as a failure, ask the strategy whether it
                // had anything to offer in the first place.
                //
                // Assigning a mining drone to an already-mined area is a legitimate question --
                // "is there anything left here, or does it need re-surveying?" -- and the answer
                // is a completed job and a docked drone, not a no-path. MiningJob.TryComplete
                // already reaches Complete on the first tick (no plot is surveyed-fresh, so none
                // is offered); it was the lifecycle that turned that answer into Unreachable by
                // treating an empty target list as a routing failure.
                //
                // A full hold lands here too and wants the same handling: the drone belongs at its
                // dock, not routed through no-path. Only the wording differs.
                if (this.strategy.IsComplete)
                {
                    this.LastDispatchNote = this.strategy.IsExhausted
                        ? "nothing left to mine here -- re-survey the area to go deeper"
                        : "hold full -- returning to unload";

                    this.HomeDock.PersistMiningJob();

                    if (this.IsAtHomeDock()) this.SettleAtDockIfHome(mover);
                    else this.BeginReturnToDock(mover, viaDistrictCleared: true);

                    return;
                }

                // Nothing was reachable. Which of two things that means depends on whether this
                // assignment has EVER got the drone somewhere.
                if (plotsProbed > 0 && !this.reachedAssignedAreaOnce)
                {
                    // Every plot tried, none reachable, and none ever was. That is a property of
                    // the area rather than a run of bad luck, so it is reported at once instead of
                    // after three rounds of the same discovery.
                    this.LastDispatchNote = "no plot in the assigned area can be reached";
                    this.consecutiveDispatchFailures = MaxConsecutiveDispatchFailures;
                    this.HandleNoPath(mover);
                    return;
                }

                this.LastDispatchNote = plotsProbed > 0
                    ? "every remaining plot is unreachable"
                    : "assigned area has no plot to target";

                this.FailDispatch(mover);
                return;
            }

            var target = started.Value;
            this.reachedAssignedAreaOnce = true;
            this.plotArrivalAttempts = 0;

            // Hold on the pad until the take-off sequence has played. The path is already
            // computed, so a failure above is still reported now rather than after the wait.
            // A drone leaving a work area is finishing its arm-stow instead, which is shorter.
            mover.HoldFor(this.IsAtHomeDock() ? TakeOffLeadInSeconds : WorkExitLeadInSeconds);

            this.LastDispatchNote = $"dispatched to area point {target.X:F0},{target.Z:F0}";
        }

        /// <summary>Seconds since the last tick, or a plausible fallback when the manager has not measured one yet.</summary>
        private static float TickDeltaSeconds()
        {
            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            return manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;
        }

        /// <summary>
        /// Whether the idle-at-dock resume may try again this tick.
        ///
        /// Always yes until the budget runs out; after that, once per
        /// <see cref="BlockedDispatchRetryIntervalSeconds"/>. The slow retry matters: the only
        /// thing that can turn an unreachable area reachable is the world changing -- a pit filled
        /// in, an obstruction cleared -- so giving up permanently would need a player to notice and
        /// reassign, while retrying every tick is the loop this exists to stop.
        /// </summary>
        private bool DispatchBudgetAllowsRetry()
        {
            if (!this.CannotReachAssignedArea) return true;

            this.secondsSinceDispatchBlocked += TickDeltaSeconds();
            if (this.secondsSinceDispatchBlocked < BlockedDispatchRetryIntervalSeconds) return false;

            // Spend the wait and allow exactly one attempt. If it fails it lands back here with a
            // fresh timer rather than a shorter one.
            this.secondsSinceDispatchBlocked = 0f;
            return true;
        }

        /// <summary>
        /// A dispatch attempt failed to get the drone moving. Spends one unit of the budget, then
        /// hands off to the ordinary no-path handling.
        ///
        /// The budget is what makes the loop terminate. Every failure path here ends the same way
        /// -- Unreachable, an immediate return leg that trivially succeeds because the drone never
        /// left its dock, arrival, Idle -- and Idle at the dock with an unfinished job is a
        /// dispatch condition (R24). So the drone re-dispatched, failed identically, and cycled
        /// once a second forever: red [unreachable], then idle, then red again.
        ///
        /// Previous fixes each removed a CAUSE of the first failure. None of them removed the
        /// cycle, so the next cause to come along reproduced it exactly -- most recently a survey
        /// drone pointed at a plot inside an eighteen-layer pit.
        /// </summary>
        private void FailDispatch(DroneMoverComponent mover)
        {
            this.consecutiveDispatchFailures++;
            this.HandleNoPath(mover);
        }

        /// <summary>
        /// True when the drone's assigned area (survey or mining, by its declared tool)
        /// contains <paramref name="pos"/> (U8/U10/KTD9). The single membership seam every
        /// arrival/roam check goes through.
        /// </summary>
        private bool IsPositionInAssignedRegion(Vector3 pos)
        {
            var area = this.CurrentTargetArea(out _);
            return area != null && area.ContainsWorldColumn(
                (int)MathF.Round(pos.X), (int)MathF.Round(pos.Z), PlotUtil.PropertyPlotLength);
        }

        /// <summary>The job kind this drone's declared tool selects (KTD3), or null for an undeclared/unrecognised tool.</summary>
        private DroneJobKind? CurrentJobKind() =>
            this.Parent is IDroneToolbearer bearer ? bearer.Job : null;

        /// <summary>The change-detection token for whichever assignment this drone's job kind reads (U10).</summary>
        private string CurrentAssignedToken() =>
            this.CurrentJobKind() == DroneJobKind.Mining
                ? this.HomeDock.AssignedMiningAreaToken
                : this.HomeDock.AssignedAreaToken;

        /// <summary>
        /// Resolves the Eco-free area this drone is currently assigned to work, regardless
        /// of job kind. A mining drone resolves through its <see cref="MiningAreaRef"/>
        /// (KTD2's three-outcome policy -- a not-yet-resolved or already-invalidated
        /// reference reports no area, same as an unresolved survey assignment).
        /// </summary>
        private SurveyArea CurrentTargetArea(out string noAreaReason) =>
            this.CurrentTargetArea(out noAreaReason, out _);

        /// <summary>
        /// As <see cref="CurrentTargetArea(out string)"/>, but also reports the reason the
        /// assignment is <em>terminally</em> unusable, rather than merely unresolved this tick.
        ///
        /// Runs the same <see cref="AreaResolutionPolicy"/> the mining strategy runs, and that
        /// agreement is the point. This method used to test only the lookup signal while the
        /// strategy tested the signal AND the change token, so the two disagreed about what a
        /// live assignment is: the looser test decided whether to fly, the stricter one decided
        /// whether to work, and a drone cleared to fly but not to work parks on its plot
        /// forever with the mining animation running and a job that never leaves Idle.
        ///
        /// KTD2's three outcomes exist to separate deletion from a load-ordering miss; folding
        /// both into a bare null also made a deleted area read as a routing failure, and
        /// routing failures are retried forever by design.
        /// </summary>
        private SurveyArea CurrentTargetArea(out string noAreaReason, out MiningEndReason? terminalReason)
        {
            noAreaReason = null;
            terminalReason = null;

            if (this.CurrentJobKind() == DroneJobKind.Mining)
            {
                var reference = this.HomeDock.AssignedMiningArea;
                if (reference == null)
                {
                    noAreaReason = "no mining area assigned";
                    return null;
                }

                var signal = reference.Resolve(out _, out var miningEntry);
                var outcome = AreaResolutionPolicy.Resolve(
                    signal,
                    reference.StoredChangeToken,
                    miningEntry == null ? null : MiningAreaRef.CurrentChangeToken(miningEntry));

                switch (outcome)
                {
                    case AreaResolutionOutcome.StillValid:
                        return miningEntry.ToSurveyArea();

                    case AreaResolutionOutcome.Invalidated:
                        if (signal == AreaLookupSignal.ConfirmedGone)
                        {
                            noAreaReason = "assigned mining area is gone";
                            terminalReason = MiningEndReason.AreaGone;
                        }
                        else
                        {
                            noAreaReason = "assigned mining area was redrawn";
                            terminalReason = MiningEndReason.AreaRedrawn;
                        }

                        return null;

                    default:
                        noAreaReason = "assigned mining area did not resolve";
                        return null;
                }
            }

            var entry = this.HomeDock.AssignedSurveyArea;
            if (entry == null)
            {
                noAreaReason = "assigned survey area did not resolve";
                return null;
            }

            return entry.ToSurveyArea();
        }

        /// <summary>
        /// The v1 mining tier's shaft depth (R12, KD4) -- matches the survey drone's own
        /// sensor reach, which is not a coincidence: a plot is worth the same depth to
        /// either drone kind at this tier.
        ///
        /// Read from <see cref="DroneTier"/> rather than declared here, because the pathfinder's
        /// step height is derived from the same number and the two must not drift: a drone whose
        /// shaft is deeper than its descent limit cannot re-enter the hole it just dug.
        /// </summary>
        private const int MiningTierDepth = DroneTier.MiningShaftDepth;

        /// <summary>
        /// Approximate hold capacity in items (KTD5: roughly 400-500 items per trip across
        /// 16 slots). A live-tunable estimate, not a hard engine limit -- the real limit is
        /// each linked destination's own stack capacity, which <see cref="CargoUnloader"/>
        /// discovers by attempting the push rather than predicting it.
        /// </summary>
        private const int MiningHoldCapacityEstimate = 16 * 400;

        /// <summary>Builds the job strategy for this drone's declared tool (KTD3, U10) -- survey unchanged since U14; mining wired here for the first time.</summary>
        private IJobStrategy BuildStrategy(SurveyArea area)
        {
            if (this.CurrentJobKind() == DroneJobKind.Mining)
            {
                // A job's ledger is keyed to its area's plots, so it cannot be carried across a
                // reassignment -- the outcomes would be recorded against plot coordinates from
                // a different area. Zero reads as "unknown" (a save predating the field) and
                // resumes, because discarding real progress is worse than resuming a job whose
                // area cannot be confirmed.
                var assignedAreaId = this.HomeDock.AssignedMiningArea?.AreaId ?? 0;
                var jobIsForAnotherArea =
                    this.HomeDock.MiningJobAreaId != 0 && this.HomeDock.MiningJobAreaId != assignedAreaId;

                var job = this.HomeDock.MiningJob;
                if (job == null
                    || job.Status == MiningJobStatus.Complete
                    || job.Status == MiningJobStatus.Ended
                    || jobIsForAnotherArea)
                {
                    job = new MiningJob(area.EnumeratePlots());
                    this.HomeDock.MiningJob = job;
                    this.HomeDock.MiningJobAreaId = assignedAreaId;
                }

                if (this.HomeDock.AssignedMiningArea == null
                    || this.HomeDock.GetComponent(typeof(PublicStorageComponent), DroneCargo.HoldName) is not PublicStorageComponent holdComponent
                    || !this.HomeDock.TryGetComponent<LinkComponent>(out var link))
                    return null;

                return new MiningStrategy(
                    this.HomeDock,
                    this.HomeDock.AssignedMiningArea,
                    job,
                    new EcoWorldSampler(),
                    new EcoBlockClassifier(),
                    new MiningRemovalService(),
                    new YieldTable(minableYield: RubbleObject.MaxAmountPerBlock, excavatableYield: 1),
                    Item.Get<MiningArmItem>(),
                    holdComponent.Storage,
                    link,
                    PlotUtil.PropertyPlotLength,
                    MiningTierDepth,
                    MiningHoldCapacityEstimate);
            }

            return this.Parent.TryGetComponent<OreSensorComponent>(out var sensor)
                ? new SurveyStrategy(this.HomeDock, sensor)
                : null;
        }

        /// <summary>
        /// The assigned area is terminally unusable -- confirmed gone (AE7: the survey dock
        /// that published it was picked up, or the area was deleted) or redrawn under the job.
        /// Ends the job with a named reason and brings the drone home; the hold is left
        /// untouched, so what it already mined is kept.
        ///
        /// Deliberately NOT <see cref="HandleNoPath"/>. Routing that back through the
        /// no-path path is what produced the observed hang: Unreachable, an immediate return
        /// leg to the dock the drone was already standing on, arrival, Idle, and then R24's
        /// idle-at-dock resume dispatching straight back into the same missing area. The job
        /// never ended, so the strategy never read complete, so the resume never stopped --
        /// the drone bobbed beside its dock indefinitely while the panel showed an idle job
        /// with no stop reason.
        /// </summary>
        private void EndJobForMissingArea(DroneMoverComponent mover, MiningEndReason reason)
        {
            this.HomeDock.MiningJob?.End(reason);
            this.HomeDock.PersistMiningJob();

            if (this.IsAtHomeDock())
            {
                // Already home. Settle in place rather than flying a zero-length return leg.
                this.SettleAtDockIfHome(mover);
                return;
            }

            this.BeginReturnToDock(mover, viaDistrictCleared: true);
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
            // The park point, not the dock's placement point: with a 4x4 pad the two are the
            // same column but the park point is the surface the drone lands on, and it is
            // what the spawn and teleport rungs already use. Routing the walking leg
            // somewhere else is how R13 ends up observable only when a drone is teleported.
            //
            // The dock occupies its own columns by design, so the drone has to be allowed
            // through them to dock at all.
            var found = mover.SetDestination(this.HomeDock.DroneParkPosition, this.HomeDock.OccupiedColumns,
                                             landAtDestination: true);
            if (found)
            {
                // Stow the arm and settle into the flying loop before travelling. A drone
                // recalled from a work area is mid-work-loop; one that never left the dock has
                // nothing to stow, and IsAtHomeDock separates the two.
                mover.HoldFor(this.IsAtHomeDock() ? TakeOffLeadInSeconds : WorkExitLeadInSeconds);

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
                this.strategy?.OnArrivedHome();
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
            if (!mover.SetDestination(this.HomeDock.DroneParkPosition, this.HomeDock.OccupiedColumns,
                                      landAtDestination: true))
                return false;

            // Same courtesy on the escalated rungs: a retry that finally finds a route should
            // still leave properly rather than snapping into motion.
            mover.HoldFor(this.IsAtHomeDock() ? TakeOffLeadInSeconds : WorkExitLeadInSeconds);
            return true;
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
            // Home is checked FIRST, and without regard to movement. Arrival used to be tested
            // only inside the moving branch, so a drone that went Unreachable while already
            // standing at its dock could never leave the state: it is not moving, so it fell to
            // the periodic retry, which re-pathed a zero-length route to where it already was,
            // "succeeded", and declared nothing. It hovered over its own dock indefinitely, and
            // unassigning could not clear it because the loop was not driven by the assignment --
            // which is what forced the assign-a-different-area workaround.
            //
            // This is the common case now rather than a corner: an outbound dispatch that fails
            // leaves the drone Unreachable exactly where it stands, and where it stands is usually
            // the dock it just set out from.
            if (this.IsAtHomeDock())
            {
                this.SettleAtDockIfHome(mover);
                return;
            }

            if (mover.IsMoving) return; // a return leg is in flight; let it fly.

            var deltaTime = TickDeltaSeconds();

            this.secondsSinceLastReturnRetry += deltaTime;
            if (this.secondsSinceLastReturnRetry < ReturnRetryIntervalSeconds)
                return;

            this.secondsSinceLastReturnRetry = 0f;
            this.AttemptReturnLegOnly(mover);
        }

        /// <summary>
        /// How far off the park point the drone may sit before settling snaps it down. Squared,
        /// to keep the comparison off the square root.
        /// </summary>
        private const float ParkSnapToleranceSquared = 0.25f * 0.25f;

        /// <summary>
        /// Turns a drone that is home in POSITION into one that is home in every sense: parked on
        /// the pad, Idle, return ladder reset. No-op when it is not at the dock.
        ///
        /// A flown return leg lands by itself -- its final waypoint IS the park position. The paths
        /// that END at the dock without flying one do not: a no-path result stops the mover wherever
        /// the drone happens to be, and an unassignment while already home never moves it at all.
        /// Both leave it at travelling altitude above the pad, where the panel reads docked and the
        /// model hovers.
        ///
        /// Snapping rather than pathing down, deliberately. The drone is already over its own dock;
        /// a landing leg here would ask the pathfinder to route into the dock's own occupied columns
        /// to travel a metre or two, which is the case that produced no-path results in the first
        /// place.
        /// </summary>
        private void SettleAtDockIfHome(DroneMoverComponent mover)
        {
            if (!this.IsAtHomeDock()) return;

            var park = this.HomeDock.DroneParkPosition;
            var here = this.Parent.Position;
            var dx = here.X - park.X;
            var dy = here.Y - park.Y;
            var dz = here.Z - park.Z;

            if ((dx * dx) + (dy * dy) + (dz * dz) > ParkSnapToleranceSquared)
                mover.TeleportTo(park);
            else
                mover.Stop();

            this.stateMachine.OnReturnedToDock();
            this.strategy?.OnArrivedHome();
            this.ResetReturnLadder(mover);
            this.workEntryPlot = null;
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
            // Guarded, unlike its callers inside Tick(): this is public now, and a caller
            // that has not already passed Tick()'s dock guard would otherwise dereference a
            // null or destroyed dock. A drone with no dock is not home -- it is stranded.
            if (this.HomeDock == null || this.HomeDock.IsDestroyed)
                return false;

            var drone = this.Parent.Position;
            var dock = this.HomeDock.Position;
            var dx = drone.X - dock.X;
            var dz = drone.Z - dock.Z;
            return ((dx * dx) + (dz * dz)) <= DockArrivalRadius * DockArrivalRadius;
        }

        /// <summary>
        /// Retries the strategy's home-arrival hook (unload) on the dock's own throttled
        /// tick (KTD11, R27) -- not a one-shot on arrival. A refused or partial push at
        /// arrival otherwise never gets retried: nothing else calls
        /// <see cref="IJobStrategy.OnArrivedHome"/> again once the drone has already
        /// physically arrived and gone Idle. Safe to call on every throttled tick
        /// regardless of state: it only does anything while the drone reads Idle and
        /// physically home, and OnArrivedHome itself is a cheap no-op on an already-empty
        /// hold (the survey strategy's own implementation never does anything at all).
        /// </summary>
        public void RetryUnloadIfWaiting()
        {
            if (this.strategy == null) return;
            if (this.stateMachine.Status != DroneStatus.Idle) return;
            if (!this.IsAtHomeDock()) return;

            this.strategy.OnArrivedHome();
        }

        /// <summary>
        /// Horizontal distance within which the drone counts as docked, measured from the
        /// dock's own position.
        ///
        /// Must clear the pad's half-diagonal, not just its edge. The pad is 4x4 centred on
        /// the dock, so its far corners are about 2.83 away -- a drone that settled on one
        /// of them under the old 2.0 radius would have walked all the way home, failed the
        /// arrival test, and been routed into Unreachable at its own dock.
        ///
        /// Sized to the geometry with a small margin, not generously. This radius is also
        /// what the Unreachable retry uses to decide a drone has arrived while it is still
        /// moving, so every unit of slack past the pad is a window where the drone is called
        /// home before it is home.
        /// </summary>
        private const float DockArrivalRadius = 3f;

        /// <summary>
        /// Drives one tick while parked at the assigned area's current plot (F2/R6/KTD3). The
        /// lifecycle owns "am I in the plot I was asked for", sending the drone to the plot
        /// centre, and the arrival-attempt counter and its cap; the job strategy owns the plot
        /// list and order, and what one tick of parked work means (survey sampling, or a future
        /// mining tick). When the strategy reports no plots remain, the drone heads home.
        ///
        /// Parking at the centre and letting the strategy work the whole plot from there means
        /// the drone's physical footprint is irrelevant (the roaming WorldObject is a
        /// placeholder size), and visiting plots discretely -- rather than continuously pathing
        /// between waypoints -- keeps a non-contiguous area's disjoint plots independent: an
        /// unreachable plot is skipped, not a stall.
        /// </summary>
        private void TickOnStation(DroneMoverComponent mover)
        {
            if (this.strategy == null)
                return;

            if (!this.strategy.TryGetNextTarget(out var plot))
            {
                if (this.strategy.IsComplete)
                {
                    this.LastDispatchNote = "job complete -- returning to dock";
                    this.BeginReturnToDock(mover, viaDistrictCleared: true);
                }
                // else: not ready yet (e.g. the assigned area hasn't resolved this tick) --
                // retry next tick without disturbing anything.
                return;
            }

            var plotSide = PlotUtil.PropertyPlotLength;
            var pos = this.Parent.Position;
            var dronePlot = PlotCoord.FromWorldColumn(
                (int)MathF.Round(pos.X), (int)MathF.Round(pos.Z), plotSide);

            if (!dronePlot.Equals(plot))
            {
                // Travelling to this plot's centre. Keep going while already moving.
                this.workEntryPlot = null;
                if (mover.IsMoving)
                    return;

                var center = new PlotPos(plot.X, plot.Z).CenterWorldPos;
                var reachable = mover.SetDestination(new Vector3(center.x, pos.Y, center.y), this.HomeDock.OccupiedColumns);
                this.plotArrivalAttempts++;

                // A hop to the next plot is travel like any other: stow the arm and reach the
                // flying loop before sliding. Without this the work loop played over a drone
                // moving sideways, which is what "the animation is still going while it moves"
                // actually was.
                if (reachable) mover.HoldFor(WorkExitLeadInSeconds);

                if (!reachable || this.plotArrivalAttempts > MaxPlotArrivalAttempts)
                {
                    // Unreachable / can't settle inside it -> report the skip so the strategy
                    // advances past it and the rest of the area still gets worked (covers
                    // non-contiguous islands and walled-off plots).
                    this.LastDispatchNote = $"skipped unreachable plot {plot.X},{plot.Z}";
                    this.plotArrivalAttempts = 0;
                    this.strategy.OnArrivalFailed();
                }
                return;
            }

            // Arm out before the ground changes. Arming is per plot, not per area: every hop
            // stows the arm on the way out (see the WorkExitLeadInSeconds hold above), so every
            // arrival has to deploy it again.
            if (this.workEntryPlot == null || !this.workEntryPlot.Value.Equals(plot))
            {
                this.workEntryPlot = plot;
                mover.HoldFor(WorkEntryLeadInSeconds);
                return;
            }

            if (mover.IsHolding) return;

            this.plotArrivalAttempts = 0;
            var outcome = this.strategy.TickParkedWork();
            if (outcome != ParkedWorkOutcome.StillWorking)
                this.LastDispatchNote = outcome == ParkedWorkOutcome.PlotDone
                    ? $"worked plot {plot.X},{plot.Z}"
                    : $"abandoned plot {plot.X},{plot.Z}";
        }
    }

    // ParkedWorkOutcome and IJobStrategy (KTD3's four-call contract) live in JobStrategy.cs.
}
