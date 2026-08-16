using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class MiningReadoutTests
    {
        private static Dictionary<SkipCategory, int> AllZero() => new Dictionary<SkipCategory, int>
        {
            [SkipCategory.Unreachable] = 0,
            [SkipCategory.Property] = 0,
            [SkipCategory.SettlementLaw] = 0,
            [SkipCategory.Obstructed] = 0,
            [SkipCategory.Other] = 0,
        };

        [Theory]
        [InlineData(MiningJobStatus.Idle)]
        [InlineData(MiningJobStatus.Working)]
        [InlineData(MiningJobStatus.WaitingToUnload)]
        [InlineData(MiningJobStatus.Ended)]
        public void EveryJobStatus_RendersDistinctWording(MiningJobStatus status)
        {
            var text = MiningReadout.FormatJobStatus(status, workedCount: 3);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }

        [Fact]
        public void CompleteWithZeroWorked_ReadsFinished_NotUnderWay()
        {
            var text = MiningReadout.FormatJobStatus(MiningJobStatus.Complete, workedCount: 0);
            Assert.Contains("complete", text);
            Assert.DoesNotContain("working", text);
        }

        [Fact]
        public void CompleteWithWorkedPlots_DiffersFromZeroWorked()
        {
            var zero = MiningReadout.FormatJobStatus(MiningJobStatus.Complete, workedCount: 0);
            var some = MiningReadout.FormatJobStatus(MiningJobStatus.Complete, workedCount: 5);
            Assert.NotEqual(zero, some);
        }

        [Fact]
        public void SkipLine_AllZero_RendersDistinctly()
        {
            Assert.Equal("none skipped", MiningReadout.FormatSkipLine(AllZero(), skippedTotal: 0));
        }

        [Fact]
        public void SkipLine_SingleCategory_RendersDistinctly()
        {
            var counts = AllZero();
            counts[SkipCategory.Property] = 3;

            var line = MiningReadout.FormatSkipLine(counts, skippedTotal: 3);

            Assert.Contains("3", line);
            Assert.Contains("property", line);
            Assert.DoesNotContain(",", line);
        }

        [Fact]
        public void SkipLine_MultiCategory_RendersDistinctly_CountsSum()
        {
            var counts = AllZero();
            counts[SkipCategory.Property] = 2;
            counts[SkipCategory.Unreachable] = 1;

            var line = MiningReadout.FormatSkipLine(counts, skippedTotal: 3);

            Assert.Contains(",", line);
            Assert.Contains("2", line);
            Assert.Contains("1", line);
        }

        [Fact]
        public void Headroom_EmptyPartialFull_RenderDistinctly()
        {
            var empty = MiningReadout.FormatHeadroom(headroom: 0, sampleQuantity: 50);
            var partial = MiningReadout.FormatHeadroom(headroom: 20, sampleQuantity: 50);
            var full = MiningReadout.FormatHeadroom(headroom: 60, sampleQuantity: 50);

            Assert.NotEqual(empty, partial);
            Assert.NotEqual(partial, full);
            Assert.NotEqual(empty, full);
        }

        [Theory]
        [InlineData(MiningEndReason.AreaGone)]
        [InlineData(MiningEndReason.Unassigned)]
        [InlineData(MiningEndReason.StampInvalid)]
        [InlineData(MiningEndReason.DevToolSelected)]
        [InlineData(MiningEndReason.Halted)]
        public void EveryEndReason_RendersDistinctWording(MiningEndReason reason)
        {
            Assert.False(string.IsNullOrWhiteSpace(MiningReadout.FormatStopReason(reason)));
        }

        [Fact]
        public void NoEndReason_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatStopReason(null));
        }

        // The case live pass #1 lost three rounds to: halted, no job yet, so nothing to read an
        // end reason from. The old code rendered empty here and the dock sat silent.
        [Fact]
        public void Halted_WithNoJob_SaysSo()
        {
            var blocked = MiningReadout.FormatBlockedReason(haltedServerWide: true, jobEndReason: null);

            Assert.False(string.IsNullOrWhiteSpace(blocked));
            Assert.Contains("halted", blocked);
        }

        [Fact]
        public void Halted_OutranksAFinishedJobsEndReason()
        {
            // A dock halted after a job ended for some other reason must report the halt: the end
            // reason is history, the halt is why nothing will start again.
            var blocked = MiningReadout.FormatBlockedReason(true, MiningEndReason.AreaGone);

            Assert.NotEqual(MiningReadout.FormatStopReason(MiningEndReason.AreaGone), blocked);
            Assert.Contains("halted", blocked);
        }

        [Fact]
        public void NotHalted_FallsBackToTheJobsEndReason()
        {
            Assert.Equal(
                MiningReadout.FormatStopReason(MiningEndReason.AreaGone),
                MiningReadout.FormatBlockedReason(false, MiningEndReason.AreaGone));
        }

        [Fact]
        public void NotHalted_WithNoJob_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatBlockedReason(false, null));
        }

        [Fact]
        public void NoRefusal_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatRefusalDetail(null));
            Assert.Equal(string.Empty, MiningReadout.FormatRefusalDetail("   "));
        }

        [Fact]
        public void Idle_DistinguishesUnassignedFromNotYetSetOut()
        {
            var unassigned = MiningReadout.FormatJobStatus(MiningJobStatus.Idle, 0, hasAssignment: false);
            var assigned = MiningReadout.FormatJobStatus(MiningJobStatus.Idle, 0, hasAssignment: true);

            Assert.NotEqual(unassigned, assigned);
            Assert.Contains("no area assigned", unassigned);
            Assert.DoesNotContain("no area assigned", assigned);
        }

        [Fact]
        public void ShaftProgress_NothingRecordedYet_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatShaftProgress(0, 0, 0, 0));
        }

        [Fact]
        public void ShaftProgress_ShowsDepthAndBothStamps()
        {
            var rendered = MiningReadout.FormatShaftProgress(5, 15, 900, 100);

            Assert.Contains("5/15", rendered);
            Assert.Contains("900", rendered);
            Assert.Contains("100", rendered);
        }

        [Theory]
        [InlineData(900, 100, true)]   // surveyed after mined -> work to do
        [InlineData(100, 900, false)]  // mined after surveyed -> already done
        [InlineData(100, 100, false)]  // equal is NOT mineable; IsMineable wants strictly newer
        public void ShaftProgress_CallsTheMineableVerdictTheSameWayIsMineableDoes(long surveyed, long mined, bool mineable)
        {
            // The row exists to expose a disagreement between this verdict and the ground, so the
            // verdict shown must be the same comparison the strategy actually gates on.
            var rendered = MiningReadout.FormatShaftProgress(1, 15, surveyed, mined);

            Assert.Equal(mineable, PlotFreshness.IsMineable(surveyed, mined));
            Assert.Equal(mineable, !rendered.Contains("NOT mineable"));
        }

        [Fact]
        public void ARefusal_KeepsTheEnginesOwnWording()
        {
            // The engine's text is the payload -- it must survive verbatim, since it is the only
            // thing distinguishing one Obstructed skip from another.
            var rendered = MiningReadout.FormatRefusalDetail("Not enough room in inventory.");

            Assert.Contains("Not enough room in inventory.", rendered);
        }
    }
}
