using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class HoldLedgerTests
    {
        [Fact]
        public void ZeroMoved_ProducesRefusal_WholeLoadRetained()
        {
            var plan = HoldLedger.Plan(holdQuantity: 50, moved: 0);

            Assert.Equal(UnloadOutcome.Refused, plan.Outcome);
            Assert.Equal(0, plan.Moved);
            Assert.Equal(50, plan.Remaining);
        }

        [Fact]
        public void PartialMove_ReportsExactRemainder()
        {
            var plan = HoldLedger.Plan(holdQuantity: 50, moved: 30);

            Assert.Equal(UnloadOutcome.Partial, plan.Outcome);
            Assert.Equal(30, plan.Moved);
            Assert.Equal(20, plan.Remaining);
        }

        [Fact]
        public void FullMove_ReportsZeroRemaining()
        {
            var plan = HoldLedger.Plan(holdQuantity: 50, moved: 50);

            Assert.Equal(UnloadOutcome.Full, plan.Outcome);
            Assert.Equal(50, plan.Moved);
            Assert.Equal(0, plan.Remaining);
        }

        [Fact]
        public void EmptyHold_ZeroMoved_IsFull_NotRefused()
        {
            // An empty hold with nothing to move is trivially "done", not "refused" --
            // there was nothing to push in the first place.
            var plan = HoldLedger.Plan(holdQuantity: 0, moved: 0);

            Assert.Equal(UnloadOutcome.Full, plan.Outcome);
        }

        [Fact]
        public void HasRoomFor_ZeroHeadroom_ReportsFalse()
        {
            Assert.False(HoldLedger.HasRoomFor(currentHoldQuantity: 16, holdCapacity: 16, nextYield: 1));
        }

        [Fact]
        public void HasRoomFor_HeadroomEqualsWholeYield_ReportsTrue()
        {
            Assert.True(HoldLedger.HasRoomFor(currentHoldQuantity: 12, holdCapacity: 16, nextYield: 4));
        }

        [Fact]
        public void HasRoomFor_YieldExceedsRemainingCapacity_ReportsFalse()
        {
            Assert.False(HoldLedger.HasRoomFor(currentHoldQuantity: 14, holdCapacity: 16, nextYield: 4));
        }
    }
}
