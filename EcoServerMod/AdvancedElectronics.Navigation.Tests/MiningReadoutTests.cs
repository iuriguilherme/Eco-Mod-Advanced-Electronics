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
    }
}
