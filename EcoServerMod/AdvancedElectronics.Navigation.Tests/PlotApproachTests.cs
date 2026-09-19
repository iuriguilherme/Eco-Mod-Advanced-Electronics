using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Which column of a plot the drone flies to. A farm plot was reported unreachable
    /// because a single blocked column -- a tree at the plot's centre -- stood where the
    /// drone aimed, while the other 24 columns were open.
    /// </summary>
    public class PlotApproachTests
    {
        private const int Side = 5;
        private static readonly PlotCoord Plot = new PlotCoord(44, 117);

        [Fact]
        public void AnOpenCentreIsTheColumnFlownTo()
        {
            var column = PlotApproach.FirstOpenColumn(Plot, Side, (x, z) => false);

            Assert.Equal((222, 587), column);
        }

        [Fact]
        public void ABlockedCentreGivesTheNearestOpenColumnInsteadOfNone()
        {
            var column = PlotApproach.FirstOpenColumn(Plot, Side, (x, z) => x == 222 && z == 587);

            Assert.NotNull(column);
            var (cx, cz) = column.Value;
            Assert.Equal(1, System.Math.Abs(cx - 222) + System.Math.Abs(cz - 587));
        }

        [Fact]
        public void EveryColumnBlockedGivesNone()
        {
            Assert.Null(PlotApproach.FirstOpenColumn(Plot, Side, (x, z) => true));
        }

        [Fact]
        public void TheColumnReturnedAlwaysLiesInsideThePlot()
        {
            var open = new HashSet<(int, int)> { (224, 589) };

            var column = PlotApproach.FirstOpenColumn(Plot, Side, (x, z) => !open.Contains((x, z)));

            Assert.Equal((224, 589), column);
        }
    }
}
