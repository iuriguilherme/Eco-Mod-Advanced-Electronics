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
        private readonly DroneDockObject sourceDock;
        private readonly SurveyAreaEntry sourceArea;
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

        public MiningStrategy(
            DroneDockObject homeDock,
            DroneDockObject sourceDock,
            SurveyAreaEntry sourceArea,
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
            this.sourceDock = sourceDock;
            this.sourceArea = sourceArea;
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

        public bool IsComplete => this.job.Status == MiningJobStatus.Complete || this.job.Status == MiningJobStatus.Ended;

        private bool IsSurveyed(PlotCoord plot) =>
            PlotFreshness.IsMineable(this.sourceArea.ReadSurveyedStamps().StampFor(plot), this.homeDock.ReadMinedStamps().StampFor(plot));

        public bool TryGetNextTarget(out PlotCoord plot)
        {
            plot = default;

            if (MiningHalt.IsHalted)
            {
                this.job.End(MiningEndReason.Halted);
                return false;
            }

            if (this.job.Status == MiningJobStatus.Idle)
                this.job.Dispatch();

            if (this.job.Status != MiningJobStatus.Working)
                return false;

            if (this.job.TryComplete(this.IsSurveyed))
                return false;

            var next = this.job.NextPlot(this.IsSurveyed);
            if (next == null)
                return false;

            plot = next.Value;
            return true;
        }

        public ParkedWorkOutcome TickParkedWork()
        {
            if (!this.TryGetNextTarget(out var target))
                return ParkedWorkOutcome.PlotDone; // nothing left to do here; lifecycle will re-ask for a target.

            // Re-check the citizen stamp at each plot arrival (KTD9, R33, R37), not once per dispatch.
            if (!this.homeDock.RecheckStamp())
            {
                this.job.End(MiningEndReason.StampInvalid);
                return ParkedWorkOutcome.PlotFailed;
            }

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

            if (result.Outcome == RemovalOutcome.Refused)
            {
                this.job.MarkSkipped(target, RefusalMapping.ToSkipCategory(result.RefusalStage));
                this.currentShaftPlot = null;
                return ParkedWorkOutcome.PlotFailed;
            }

            // R24/AE1: a full hold interrupts the shaft; the resume point (shaftResumeIndex)
            // already stored above lets the same shaft continue after the next unload.
            var holdQuantity = this.hold.NonEmptyStacks.Sum(s => s.Quantity);
            if (holdQuantity >= this.holdCapacity)
                return ParkedWorkOutcome.PlotDone; // lifecycle asks for a target again; TryGetNextTarget will re-offer this same plot once resumed, since it is still Unworked.

            return ParkedWorkOutcome.StillWorking;
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
            if (this.job.Status != MiningJobStatus.Working && this.job.Status != MiningJobStatus.WaitingToUnload)
                return;

            var plan = CargoUnloader.TryUnload(this.hold, this.link, this.homeDock.StampedCitizen);
            if (plan.Outcome == UnloadOutcome.Full)
                this.job.OnUnloadSucceeded();
            else
                this.job.OnUnloadRefused();
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
