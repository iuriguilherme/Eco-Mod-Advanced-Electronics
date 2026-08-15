using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class MiningJobTests
    {
        private static readonly PlotCoord P00 = new PlotCoord(0, 0);
        private static readonly PlotCoord P10 = new PlotCoord(1, 0);
        private static readonly PlotCoord P01 = new PlotCoord(0, 1);

        private static bool AllSurveyed(PlotCoord _) => true;

        [Fact]
        public void FreshJob_StartsIdle_MovesToWorkingOnDispatch()
        {
            var job = new MiningJob(new[] { P00 });
            Assert.Equal(MiningJobStatus.Idle, job.Status);

            job.Dispatch();

            Assert.Equal(MiningJobStatus.Working, job.Status);
        }

        [Fact]
        public void FinishingAPlot_MarksItWorked_OffersNextUnworkedPlot()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            var next = job.NextPlot(AllSurveyed);
            Assert.Equal(P00, next); // raster order: Z then X

            job.MarkWorked(P00);

            Assert.Equal(PlotOutcome.Worked, job.OutcomeOf(P00));
            Assert.Equal(P10, job.NextPlot(AllSurveyed));
        }

        [Fact]
        public void RefusedUnload_MovesWorkingToWaitingToUnload_LaterSuccessReturnsToWorking()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();

            job.OnUnloadRefused();
            Assert.Equal(MiningJobStatus.WaitingToUnload, job.Status);

            job.OnUnloadSucceeded();
            Assert.Equal(MiningJobStatus.Working, job.Status);
        }

        [Fact]
        public void JobWhoseEveryPlotIsSkipped_ReachesComplete_ZeroWorkedEveryPlotSkipped()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            job.MarkSkipped(P00, SkipCategory.Property);
            Assert.False(job.TryComplete(AllSurveyed));

            job.MarkSkipped(P10, SkipCategory.Unreachable);
            Assert.True(job.TryComplete(AllSurveyed));

            Assert.Equal(MiningJobStatus.Complete, job.Status);
            Assert.Equal(0, job.WorkedCount);
            Assert.Equal(2, job.SkippedCount);
        }

        [Fact]
        public void EachSkipCategory_RecordedAndCountedSeparately_NoDoubleCounting()
        {
            var plots = new[] { P00, P10, P01, new PlotCoord(2, 0), new PlotCoord(2, 1) };
            var job = new MiningJob(plots);
            job.Dispatch();

            job.MarkSkipped(plots[0], SkipCategory.Unreachable);
            job.MarkSkipped(plots[1], SkipCategory.Property);
            job.MarkSkipped(plots[2], SkipCategory.SettlementLaw);
            job.MarkSkipped(plots[3], SkipCategory.Obstructed);
            job.MarkSkipped(plots[4], SkipCategory.Other);

            var counts = job.SkipCountsByCategory();
            Assert.Equal(1, counts[SkipCategory.Unreachable]);
            Assert.Equal(1, counts[SkipCategory.Property]);
            Assert.Equal(1, counts[SkipCategory.SettlementLaw]);
            Assert.Equal(1, counts[SkipCategory.Obstructed]);
            Assert.Equal(1, counts[SkipCategory.Other]);
            Assert.Equal(job.SkippedCount, counts.Values.Sum());
        }

        [Theory]
        [InlineData(RemovalRefusalStage.Property, SkipCategory.Property)]
        [InlineData(RemovalRefusalStage.SettlementLaw, SkipCategory.SettlementLaw)]
        [InlineData(RemovalRefusalStage.Pretest, SkipCategory.Obstructed)]
        [InlineData(RemovalRefusalStage.Unrecognised, SkipCategory.Other)]
        public void RefusalMapping_ReturnsExpectedCategory_UnrecognisedFallsBackToOther(RemovalRefusalStage stage, SkipCategory expected)
        {
            Assert.Equal(expected, RefusalMapping.ToSkipCategory(stage));
        }

        [Fact]
        public void WorkedPlot_NotReOfferedAsNextPlot()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();
            job.MarkWorked(P00);

            Assert.Null(job.NextPlot(AllSurveyed));
        }

        [Fact]
        public void SkippedPlot_NotReOfferedWithinSameJob()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();
            job.MarkSkipped(P00, SkipCategory.Obstructed);

            Assert.Null(job.NextPlot(AllSurveyed));
        }

        [Fact]
        public void EndingFromWorkingAndFromWaitingToUnload_EachPreservesLedgerAndCarriesEndReason()
        {
            var jobA = new MiningJob(new[] { P00, P10 });
            jobA.Dispatch();
            jobA.MarkWorked(P00);
            jobA.End(MiningEndReason.AreaGone);

            Assert.Equal(MiningJobStatus.Ended, jobA.Status);
            Assert.Equal(MiningEndReason.AreaGone, jobA.EndReason);
            Assert.Equal(PlotOutcome.Worked, jobA.OutcomeOf(P00));

            var jobB = new MiningJob(new[] { P00, P10 });
            jobB.Dispatch();
            jobB.MarkWorked(P00);
            jobB.OnUnloadRefused();
            jobB.End(MiningEndReason.Halted);

            Assert.Equal(MiningJobStatus.Ended, jobB.Status);
            Assert.Equal(MiningEndReason.Halted, jobB.EndReason);
            Assert.Equal(PlotOutcome.Worked, jobB.OutcomeOf(P00));
        }

        [Fact]
        public void ReMarkingAlreadyWorkedPlot_IsIdempotent_DoesNotInflateCount()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();

            job.MarkWorked(P00);
            job.MarkWorked(P00);

            Assert.Equal(1, job.WorkedCount);
        }

        // Supplemental: R17/AE6 -- an unsurveyed plot is never offered and never blocks
        // completion, so an area with plots the survey drone has not reached still
        // completes once every SURVEYED plot is worked or skipped.
        [Fact]
        public void UnsurveyedPlot_NeverOffered_NeverBlocksCompletion()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            bool IsSurveyed(PlotCoord p) => p.Equals(P00); // P10 never surveyed

            Assert.Equal(P00, job.NextPlot(IsSurveyed));

            job.MarkWorked(P00);

            Assert.Null(job.NextPlot(IsSurveyed));
            Assert.True(job.TryComplete(IsSurveyed));
            Assert.Equal(PlotOutcome.Unworked, job.OutcomeOf(P10));
        }

        [Fact]
        public void TryComplete_NoOp_WhenNotWorking()
        {
            var job = new MiningJob(new[] { P00 });
            Assert.False(job.TryComplete(AllSurveyed)); // still Idle

            job.Dispatch();
            job.MarkWorked(P00);
            job.TryComplete(AllSurveyed);
            Assert.False(job.TryComplete(AllSurveyed)); // already Complete
        }
    }
}
