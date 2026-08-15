using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class AreaResolutionTests
    {
        [Fact]
        public void Found_MatchingToken_ContinuesTheJob()
        {
            var outcome = AreaResolutionPolicy.Resolve(AreaLookupSignal.Found, "area:1:0", "area:1:0");
            Assert.Equal(AreaResolutionOutcome.StillValid, outcome);
        }

        [Fact]
        public void NotYetResolved_LeavesTheJobAndItsReasonUntouched()
        {
            var outcome = AreaResolutionPolicy.Resolve(AreaLookupSignal.NotYetResolved, "area:1:0", "area:1:0");
            Assert.Equal(AreaResolutionOutcome.NotYetResolved, outcome);
        }

        [Fact]
        public void ConfirmedGone_EndsTheJob()
        {
            var outcome = AreaResolutionPolicy.Resolve(AreaLookupSignal.ConfirmedGone, "area:1:0", null);
            Assert.Equal(AreaResolutionOutcome.Invalidated, outcome);
        }

        [Fact]
        public void Found_ChangedToken_InvalidatesLikeARedraw()
        {
            // The area still resolves, but its change token moved -- a redraw happened.
            var outcome = AreaResolutionPolicy.Resolve(AreaLookupSignal.Found, "area:1:0", "area:1:1");
            Assert.Equal(AreaResolutionOutcome.Invalidated, outcome);
        }
    }
}
