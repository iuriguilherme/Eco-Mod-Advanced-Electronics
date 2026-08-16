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
        }

        /// <summary>The job this strategy is driving -- exposed for the panel (U9), which persists its snapshot on the dock.</summary>
        public MiningJob Job => this.job;

        public bool IsComplete =>
            this.job.Status == MiningJobStatus.Complete || this.job.Status == MiningJobStatus.Ended || this.holdFull;

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
                    if (this.job.Status == MiningJobStatus.Working || this.job.Status == MiningJobStatus.WaitingToUnload)
                        this.job.End(MiningEndReason.AreaGone);
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

            if (!this.homeDock.RecheckStamp())
            {
                this.job.End(MiningEndReason.StampInvalid);
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

                this.currentShaftPlan = ShaftPlan.Create(target, this.tierDepth, this.sampler, this.plotSize);
                this.currentShaftPlot = target;
                this.shaftResumeIndex = 0;
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
                this.currentShaftPlot = null;
                return ParkedWorkOutcome.PlotDone;
            }

            var layer = layers[0];
            var classified = layer.Positions
                .Select(p => (Position: p, Classification: this.classifier.Classify(p.X, p.Y, p.Z)))
                .ToList();

            var removable = classified.Where(c => c.Classification != BlockClassification.NotRemovable).ToList();
            this.shaftResumeIndex += layer.Positions.Count;

            if (removable.Count == 0)
                return ParkedWorkOutcome.StillWorking; // this layer was e.g. a wall (AE5) or already empty; move on next tick.

            var result = this.removalService.Remove(
                removable.Select(c => (c.Position, c.Classification)).ToList(),
                this.homeDock.StampedCitizen,
                this.tool,
                this.hold,
                this.yieldTable,
                this.classifier);

            if (result.Outcome == RemovalOutcome.Refused && this.HoldIsFull())
            {
                // A refusal with a full hold is not an obstructed plot -- it is this drone having
                // nowhere to put the next block. Abandoning the plot here is what made a mining
                // run stop after five layers of fifteen and report blocked terrain for ground that
                // was clear (live pass #3).
                //
                // The plot stays Unworked and currentShaftPlot/shaftResumeIndex stay set, so the
                // same shaft resumes at the same layer after the next unload, exactly as the
                // capacity branch below intends.
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
                this.currentShaftPlot = null;
                return ParkedWorkOutcome.PlotFailed;
            }

            // R24/AE1: a full hold interrupts the shaft; the resume point (shaftResumeIndex)
            // already stored above lets the same shaft continue after the next unload. The
            // plot itself stays Unworked and currentShaftPlot is deliberately left set, so
            // resuming after unload re-enters this same plot at this same layer rather than
            // restarting it or skipping to a different one.
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
            if (this.currentShaftPlot is { } plot)
            {
                this.job.MarkSkipped(plot, SkipCategory.Unreachable);
                this.currentShaftPlot = null;
            }
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
