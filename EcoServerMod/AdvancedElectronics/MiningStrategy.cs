using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The mining drone's job strategy (U13, KTD1, KTD14): one tick's work at a parked
    /// plot is classify each position in the next shaft layer, submit the removable ones
    /// as one pack, record the outcome, and know when to go home. Implements the seam
    /// U14 defines; the lifecycle keeps travel, the plot-arrival test, and the
    /// arrival-attempt cap.
    ///
    /// Never unit-tested (every collaborator is an Eco type) -- its decision logic lives
    /// in U1 (<see cref="ShaftPlan"/>) and U2 (<see cref="MiningJob"/>), which are.
    /// </summary>
    public sealed class MiningStrategy : IJobStrategy
    {
        private readonly DroneDockObject homeDock;
        private readonly MiningAreaRef areaRef;
        private readonly MiningJob job;
        private readonly IWorldSampler sampler;
        private readonly IBlockClassifier classifier;
        private readonly MiningRemovalService removalService;
        private readonly YieldTable yieldTable;
        private readonly Item tool;
        private readonly Inventory hold;
        private readonly LinkComponent link;
        private readonly int plotSize;
        private readonly int tierDepth;
        private readonly int holdCapacity;

        private ShaftPlan currentShaftPlan;
        private PlotCoord? currentShaftPlot;
        private int shaftResumeIndex;

        /// <summary>The deepest Y the current pass may reach, persisted on the dock so it survives a load.</summary>
        private int? passFloorY;

        /// <summary>
        /// The plot most recently handed to the lifecycle by <see cref="TryGetNextTarget"/>,
        /// whether or not the drone ever reached it.
        ///
        /// currentShaftPlot cannot answer that: it is set inside TickParkedWork, which only runs
        /// once the drone has actually parked. An arrival that fails never gets there, so
        /// OnArrivalFailed had nothing to mark and silently recorded nothing -- and an unmarked
        /// plot is still Unworked, so it was offered again the next tick, forever. Live pass #5
        /// caught a drone hovering over its area on the working animation with the job reading
        /// "Working, worked 0, skipped 0" while the lifecycle reported "skipped unreachable plot".
        /// </summary>
        private PlotCoord? lastOfferedPlot;

        /// <summary>
        /// True from the tick the hold reaches capacity until it is fully unloaded (R24,
        /// AE1). This is what actually sends the drone home: <see cref="TryGetNextTarget"/>
        /// offers nothing while it is set, and <see cref="IsComplete"/> reads true for it
        /// too, so the lifecycle's "no target, but the strategy has nothing more right
        /// now" branch fires exactly as it does for a genuinely finished job -- the
        /// difference is this one resumes (R24's idle-at-dock dispatch condition) instead
        /// of staying home.
        /// </summary>
        private bool holdFull;

        public MiningStrategy(
            DroneDockObject homeDock,
            MiningAreaRef areaRef,
            MiningJob job,
            IWorldSampler sampler,
            IBlockClassifier classifier,
            MiningRemovalService removalService,
            YieldTable yieldTable,
            Item tool,
            Inventory hold,
            LinkComponent link,
            int plotSize,
            int tierDepth,
            int holdCapacity)
        {
            this.homeDock = homeDock;
            this.areaRef = areaRef;
            this.job = job;
            this.sampler = sampler;
            this.classifier = classifier;
            this.removalService = removalService;
            this.yieldTable = yieldTable;
            this.tool = tool;
            this.hold = hold;
            this.link = link;
            this.plotSize = plotSize;
            this.tierDepth = tierDepth;
            this.holdCapacity = holdCapacity;

            this.RestorePassInProgress();
        }

        /// <summary>
        /// Picks a half-cut pass back up after a load.
        ///
        /// Only the plot and the pass floor are restored; the plan itself is deliberately NOT
        /// reconstructed, because a plan is meant to describe the ground as it is now. What a
        /// re-plan cannot re-derive is how deep this pass had already committed to going, and
        /// without it the re-plan spends a fresh tierDepth below the pit floor -- observed live
        /// as "3x3 in layers 11-18" after restarting around layer 10.
        /// </summary>
        private void RestorePassInProgress()
        {
            if (!this.homeDock.TryReadPassInProgress(out var plot, out var floorY)) return;

            this.currentShaftPlot = plot;
            this.passFloorY = floorY;
        }

        /// <summary>
        /// Starts a pass on <paramref name="plot"/>: plans against the current surface, and
        /// records the floor that plan reaches so an interrupted pass stops in the same place.
        /// </summary>
        private void BeginPass(PlotCoord plot)
        {
            this.currentShaftPlan = ShaftPlan.Create(plot, this.tierDepth, this.sampler, this.plotSize);
            this.currentShaftPlot = plot;
            this.shaftResumeIndex = 0;
            this.passFloorY = this.currentShaftPlan.FloorY;

            if (this.passFloorY != null)
                this.homeDock.SavePassInProgress(plot, this.passFloorY.Value);
        }

        /// <summary>
        /// Re-enters a pass whose plan did not survive a load: re-plans against the ground as it
        /// is NOW, clamped to the recorded floor, and without the 3x3 surface opening -- the
        /// mouth is already cut, and layer 0 exists to open undisturbed ground.
        /// </summary>
        private void ResumePass(PlotCoord plot)
        {
            this.currentShaftPlan = ShaftPlan.Create(
                plot, this.tierDepth, this.sampler, this.plotSize, floorY: this.passFloorY);

            this.shaftResumeIndex = 0;
        }

        /// <summary>
        /// Forgets the current pass -- the plot finished, or was skipped. NOT called when a full
        /// hold interrupts the shaft: that keeps its resume point on purpose, so the trip home
        /// and back re-enters the same shaft at the same layer.
        /// </summary>
        private void EndPass()
        {
            this.currentShaftPlan = null;
            this.currentShaftPlot = null;
            this.shaftResumeIndex = 0;
            this.passFloorY = null;
            this.homeDock.ClearPassInProgress();
        }

        /// <summary>The job this strategy is driving -- exposed for the panel (U9), which persists its snapshot on the dock.</summary>
        public MiningJob Job => this.job;

        /// <summary>
        /// Which area this strategy was built against. The lifecycle compares it with the
        /// dock's current assignment: a strategy captures its area reference at construction,
        /// so one built for a previous area keeps consulting that one no matter what the dock
        /// now says.
        /// </summary>
        public int SourceAreaId => this.areaRef.AreaId;

        public bool IsComplete =>
            this.IsExhausted || this.holdFull;

        /// <summary>
        /// Only the job's own terminal states. A full hold is deliberately NOT exhaustion -- it is
        /// the reason to go home, and this strategy must survive the trip carrying
        /// currentShaftPlot and shaftResumeIndex, or the shaft restarts from the pit floor.
        /// </summary>
        public bool IsExhausted =>
            this.job.Status == MiningJobStatus.Complete || this.job.Status == MiningJobStatus.Ended;

        /// <summary>
        /// Re-resolves the source area fresh (KTD2's three-outcome policy), rather than
        /// trusting a reference captured once at strategy construction -- this is what
        /// lets a survey dock picked up mid-job (AE7) or a redrawn area actually end the
        /// job, instead of the strategy silently comparing stamps against a stale entry.
        /// </summary>
        private SurveyAreaEntry ResolveSourceArea()
        {
            var signal = this.areaRef.Resolve(out _, out var area);
            var outcome = AreaResolutionPolicy.Resolve(signal, this.areaRef.StoredChangeToken, area == null ? null : MiningAreaRef.CurrentChangeToken(area));

            switch (outcome)
            {
                case AreaResolutionOutcome.Invalidated:
                    // No state guard: MiningJob.End owns which states are terminal. Guarding
                    // here for Working/WaitingToUnload meant an Idle job -- the state a job is
                    // in until its first dispatch succeeds -- could never end against a
                    // vanished area, and an unendable job is an undispatchable one that still
                    // reports itself dispatchable.
                    //
                    // The two ways to be invalidated want different words from the panel: the
                    // area is gone, or it still exists and was reshaped under us.
                    this.job.End(signal == AreaLookupSignal.ConfirmedGone
                        ? MiningEndReason.AreaGone
                        : MiningEndReason.AreaRedrawn);
                    return null;
                case AreaResolutionOutcome.NotYetResolved:
                    return null;
                default:
                    return area;
            }
        }

        private bool IsSurveyed(SurveyAreaEntry sourceArea, PlotCoord plot) =>
            PlotFreshness.IsMineable(sourceArea.ReadSurveyedStamps().StampFor(plot), this.homeDock.ReadMinedStamps().StampFor(plot));

        public bool TryGetNextTarget(out PlotCoord plot)
        {
            plot = default;

            if (this.holdFull)
                return false; // full hold takes priority over offering more work -- see IsComplete.

            if (MiningHalt.IsHalted)
            {
                this.job.End(MiningEndReason.Halted);
                return false;
            }

            var stampRefusal = this.homeDock.StampRefusalReason();
            if (stampRefusal != null)
            {
                this.job.End(stampRefusal.Value);
                return false;
            }

            var sourceArea = this.ResolveSourceArea();
            if (sourceArea == null)
                return false; // either not-yet-resolved (retry) or just ended (Invalidated) -- both report no target.

            bool IsSurveyed(PlotCoord p) => this.IsSurveyed(sourceArea, p);

            if (this.job.Status == MiningJobStatus.Idle)
                this.job.Dispatch();

            if (this.job.Status != MiningJobStatus.Working)
                return false;

            if (this.job.TryComplete(IsSurveyed))
                return false;

            var next = this.job.NextPlot(IsSurveyed);
            if (next == null)
                return false;

            plot = next.Value;
            this.lastOfferedPlot = plot;
            return true;
        }

        public ParkedWorkOutcome TickParkedWork()
        {
            // TryGetNextTarget re-checks the citizen stamp and re-resolves the source area on
            // every call (KTD9, R33, R37) -- both already ended the job and reported no
            // target if either failed, so a false return here needs no further diagnosis.
            if (!this.TryGetNextTarget(out var target))
                return ParkedWorkOutcome.PlotDone; // nothing left to do here; lifecycle will re-ask for a target.

            if (this.currentShaftPlot == null || !this.currentShaftPlot.Value.Equals(target))
            {
                // A fresh plot: not yet cut, or already cut to full depth (R16).
                if (this.job.OutcomeOf(target) != PlotOutcome.Unworked)
                    return ParkedWorkOutcome.PlotDone;

                this.BeginPass(target);
            }
            else if (this.currentShaftPlan == null)
            {
                // Same plot, no plan: a load happened mid-pass. Re-plan against the pit as it
                // stands, stopping at the floor this pass had already committed to.
                this.ResumePass(target);
            }

            var layers = this.currentShaftPlan.LayersFrom(this.shaftResumeIndex);

            // Diagnostic, recorded every tick of parked work: how deep this shaft has actually got,
            // and the two stamps IsMineable compares. Re-resolves the source area rather than
            // threading one down, because a stale copy is exactly what this is here to rule out.
            var progressArea = this.ResolveSourceArea();
            this.job.RecordShaftProgress(
                this.currentShaftPlan.Layers.Count - layers.Count,
                this.currentShaftPlan.Layers.Count,
                progressArea == null ? 0 : progressArea.ReadSurveyedStamps().StampFor(target),
                this.homeDock.ReadMinedStamps().StampFor(target));

            if (layers.Count == 0)
            {
                // Every layer of this plot is done.
                this.job.MarkWorked(target);
                this.homeDock.RecordMinedPlot(target, NextStampCounter());
                this.EndPass();
                return ParkedWorkOutcome.PlotDone;
            }

            var layer = layers[0];
            var classified = layer.Positions
                .Select(p => (Position: p, Classification: this.classifier.Classify(p.X, p.Y, p.Z)))
                .ToList();

            var removable = classified.Where(c => c.Classification != BlockClassification.NotRemovable).ToList();

            if (removable.Count == 0)
            {
                // Nothing to submit, so this layer IS finished -- advance past it or the shaft
                // stalls on a layer of wall (AE5) or already-empty ground.
                this.shaftResumeIndex += layer.Positions.Count;
                return ParkedWorkOutcome.StillWorking;
            }

            var result = this.removalService.Remove(
                removable.Select(c => (c.Position, c.Classification)).ToList(),
                this.homeDock.StampedCitizen,
                this.tool,
                this.hold,
                this.yieldTable,
                this.classifier);

            if (result.Outcome == RemovalOutcome.Refused && !this.hold.IsEmpty)
            {
                // A refusal while carrying anything is treated as "no room for this layer", not as
                // an obstructed plot. Gating this on HoldIsFull was too strict and is what kept the
                // run stopping at 5 layers of 15 (live pass #6: "Last refusal: Not enough room in
                // inventory" alongside a hold that was not yet full). The hold does not have to be
                // FULL to be unable to take another twenty-five positions at once, so predicting
                // fullness was the wrong question -- the engine already answered the right one by
                // refusing.
                //
                // Self-limiting, so this cannot spin: going home empties the hold, and a refusal
                // with an EMPTY hold falls through to the genuine-skip branch below, because lack
                // of room cannot be the cause when nothing is aboard. If the unload itself is
                // refused, OnArrivedHome leaves holdFull set and the drone waits at the dock (KD14)
                // rather than flying out to be refused again.
                //
                // The plot stays Unworked with currentShaftPlot and shaftResumeIndex intact, so the
                // same shaft resumes at the same layer after the next unload.
                this.holdFull = true;
                return ParkedWorkOutcome.PlotDone;
            }

            if (result.Outcome == RemovalOutcome.Refused)
            {
                // The engine's own refusal wording travels with the skip. Obstructed is the
                // removal service's FALLBACK stage -- everything that was not law and not
                // property -- so without this the panel could only ever say "obstructed", which
                // names the bucket and not the cause. Live pass #2 lost two of three plots to
                // exactly that, with the answer already computed and thrown away here.
                this.job.MarkSkipped(target, RefusalMapping.ToSkipCategory(result.RefusalStage), result.Message);
                this.EndPass();
                return ParkedWorkOutcome.PlotFailed;
            }

            // Only NOW is the layer finished. Advancing before submitting counted a layer whose
            // removal was then refused for room, so the resume skipped it outright -- live pass #9
            // found layers 6 and 10 unmined in every plot, exactly the two unload boundaries.
            this.shaftResumeIndex += layer.Positions.Count;

            // R24/AE1: a full hold interrupts the shaft; the resume point (shaftResumeIndex)
            // lets the same shaft continue after the next unload. The plot itself stays Unworked
            // and currentShaftPlot is deliberately left set, so resuming after unload re-enters
            // this same plot at this same layer rather than restarting it or skipping ahead.
            if (this.HoldIsFull())
            {
                this.holdFull = true;
                return ParkedWorkOutcome.PlotDone;
            }

            return ParkedWorkOutcome.StillWorking;
        }

        /// <summary>
        /// Whether the hold can take no more. Asks the inventory, and keeps the configured
        /// estimate only as an upper bound.
        ///
        /// The estimate alone was the whole test, and it never fired: it is 16 slots x 400, while
        /// sixteen slots of stacked blocks hold a small fraction of that. Its own doc comment
        /// admitted as much -- "a live-tunable estimate, not a hard engine limit" -- so the drone
        /// never noticed a full hold and instead learned it by having a dig refused, which
        /// TickParkedWork then filed as an obstructed plot. Stack capacity is a question the
        /// inventory can answer exactly, so ask it rather than predict it (the same reasoning
        /// CargoUnloader already applies in the other direction).
        /// </summary>
        private bool HoldIsFull()
        {
            if (this.hold.Stacks.All(s => s.Quantity > 0 && s.Item != null))
            {
                // Every slot occupied. Still not full if a stack has headroom, which only the
                // stack's own max size can say.
                if (this.hold.NonEmptyStacks.All(s => s.Quantity >= s.Item.MaxStackSize))
                    return true;
            }

            var holdQuantity = this.hold.NonEmptyStacks.Sum(s => s.Quantity);
            return holdQuantity >= this.holdCapacity;
        }

        public void OnArrivalFailed()
        {
            // Falls back to the last offered plot, because the common case is an arrival that
            // failed BEFORE the drone ever parked -- currentShaftPlot is set inside TickParkedWork
            // and is therefore still null exactly when this is most likely to be called. Marking
            // nothing leaves the plot Unworked, so the lifecycle is handed the same target again
            // the next tick and the drone loops over its area indefinitely.
            var plot = this.currentShaftPlot ?? this.lastOfferedPlot;
            if (plot == null) return;

            this.job.MarkSkipped(plot.Value, SkipCategory.Unreachable);
            this.EndPass();
            this.lastOfferedPlot = null;
        }

        public void OnArrivedHome()
        {
            // Unconditional: the hold's cargo needs to reach storage regardless of job
            // status -- including Complete or Ended, where the job finished (or was ended)
            // while the drone was still out and the last, often-largest load is still
            // riding home. job.OnUnloadSucceeded/OnUnloadRefused each already no-op for a
            // status they don't apply to, so only a genuinely in-progress job's status moves.
            var plan = CargoUnloader.TryUnload(this.hold, this.link, this.homeDock.StampedCitizen);

            if (plan.Outcome == UnloadOutcome.Full)
            {
                this.holdFull = false;
                this.job.OnUnloadSucceeded();
            }
            else
            {
                // Partial or refused: the hold is not empty, so it still counts as full for
                // dispatch purposes (KD14 -- the drone waits and keeps retrying rather than
                // heading back out half-loaded).
                this.holdFull = true;
                this.job.OnUnloadRefused();
            }
        }

        public void OnEnded(string reason)
        {
            if (this.job.Status == MiningJobStatus.Working || this.job.Status == MiningJobStatus.WaitingToUnload)
                this.job.End(MiningEndReason.Unassigned);
        }

        /// <summary>
        /// The shared monotonic counter both stamp kinds draw from (KTD12). World time in
        /// whole seconds is monotonic for the life of a save and comparable across the
        /// survey and mining docks without any coordination between them.
        /// </summary>
        private static long NextStampCounter() => (long)Eco.Simulation.Time.WorldTime.Seconds;
    }
}
