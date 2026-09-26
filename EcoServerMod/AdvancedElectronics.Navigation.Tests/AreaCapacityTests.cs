using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// The one per-dock area limit and the two tab cursors' reach (U10, R19, KTD7).
    ///
    /// <para>
    /// Everything else in U10 is picker and tab glue that holds Eco types, so this is the part
    /// that can be proven here: the cap itself, the over-limit case a pre-fold save produces, and
    /// the clamp that keeps each tab's cursor inside the count of the kind THAT TAB shows rather
    /// than inside the dock's whole collection.
    /// </para>
    /// <para>
    /// The over-limit case is the one worth pinning. Folding eight farms onto a dock that already
    /// held ten survey areas leaves eighteen, and KTD7's answer is that the dock keeps all of them
    /// and may add none — so a rule written as "exactly ten" rather than "fewer than ten" would
    /// either delete a player's areas or let an eleventh through.
    /// </para>
    /// </summary>
    public class AreaCapacityTests
    {
        [Fact]
        public void TheLimitIsTen()
        {
            Assert.Equal(10, AreaCapacity.MaxAreasPerDock);
        }

        [Fact]
        public void ADockUnderTheLimitMayAdd()
        {
            Assert.True(AreaCapacity.MayAdd(0));
            Assert.True(AreaCapacity.MayAdd(9));
        }

        [Fact]
        public void ADockAtTheLimitMayNotAdd()
        {
            Assert.False(AreaCapacity.MayAdd(AreaCapacity.MaxAreasPerDock));
        }

        /// <summary>Covers KTD7: the fold can leave a dock above the limit, and that dock adds nothing.</summary>
        [Fact]
        public void ADockOverTheLimitMayNotAddEither()
        {
            Assert.False(AreaCapacity.MayAdd(12));
            Assert.False(AreaCapacity.MayAdd(18));
        }

        [Fact]
        public void OverLimitByCountsOnlyTheExcess()
        {
            Assert.Equal(0, AreaCapacity.OverLimitBy(3));
            Assert.Equal(0, AreaCapacity.OverLimitBy(AreaCapacity.MaxAreasPerDock));
            Assert.Equal(2, AreaCapacity.OverLimitBy(12));
        }

        /// <summary>
        /// Covers U10 step 3. A cursor must reach every area the worst pre-fold dock can carry —
        /// ten survey areas plus eight folded farms — or KTD7's promise to keep them is a promise
        /// to keep them out of reach.
        /// </summary>
        [Fact]
        public void TheCursorReachesEveryAreaAPreFoldDockCanHold()
        {
            Assert.Equal(18, AreaCapacity.MaxAddressablePositions);
            Assert.True(AreaCapacity.MaxAddressablePositions >= AreaCapacity.MaxAreasPerDock + AreaCapacity.MaxLegacyFarmAreas);
        }

        [Fact]
        public void TheClampHoldsTheCursorInsideItsOwnKindsCount()
        {
            Assert.Equal(2, AreaCapacity.ClampToKindCount(2, 3));
            Assert.Equal(2, AreaCapacity.ClampToKindCount(7, 3));
            Assert.Equal(0, AreaCapacity.ClampToKindCount(-4, 3));
        }

        /// <summary>
        /// The empty tab. A dock whose areas are all of the OTHER kind shows none here, and the
        /// cursor rests at zero rather than at minus one — every caller renders that as "no areas
        /// yet" and none of them indexes with it.
        /// </summary>
        [Fact]
        public void TheClampRestsAtZeroWhenThatKindHasNoAreas()
        {
            Assert.Equal(0, AreaCapacity.ClampToKindCount(5, 0));
            Assert.Equal(0, AreaCapacity.ClampToKindCount(0, 0));
        }
    }
}
