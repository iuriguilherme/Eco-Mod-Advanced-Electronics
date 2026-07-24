using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class SurveyAreaTests
    {
        // Eco's property plot is 8 world blocks per axis (PlotUtil.PropertyPlotLength
        // = Chunk.Size / 2). Tests use 8 to match the real boundary, but any positive
        // value works since plotSize is injected.
        private const int PlotSize = 8;

        // --- World-column -> plot mapping: deterministic, gapless (ported invariant) ---

        [Fact]
        public void WorldColumn_OnPlotBoundary_MapsToThePlotThatStartsThere()
        {
            // x=8 is the boundary between plot 0 ([0,8)) and plot 1 ([8,16)).
            Assert.Equal(new PlotCoord(1, 0), PlotCoord.FromWorldColumn(8, 0, PlotSize));
            Assert.Equal(new PlotCoord(0, 0), PlotCoord.FromWorldColumn(7, 0, PlotSize));
            Assert.Equal(new PlotCoord(0, 0), PlotCoord.FromWorldColumn(0, 0, PlotSize));
        }

        [Fact]
        public void WorldColumn_NegativeCoordinates_FloorTowardNegativeInfinity_NoGapAtOrigin()
        {
            // Truncating division would map both -1 and 7 into plot 0, opening a gap
            // at the origin. Floor division must place -1 in plot -1.
            Assert.Equal(new PlotCoord(-1, 0), PlotCoord.FromWorldColumn(-1, 0, PlotSize));
            Assert.Equal(new PlotCoord(-1, 0), PlotCoord.FromWorldColumn(-8, 0, PlotSize));
            Assert.Equal(new PlotCoord(-2, 0), PlotCoord.FromWorldColumn(-9, 0, PlotSize));
            Assert.Equal(new PlotCoord(0, 0), PlotCoord.FromWorldColumn(0, 0, PlotSize));
            Assert.Equal(new PlotCoord(0, 0), PlotCoord.FromWorldColumn(7, 0, PlotSize));
        }

        [Fact]
        public void WorldColumn_Mapping_IsDeterministic()
        {
            Assert.Equal(
                PlotCoord.FromWorldColumn(20, 20, PlotSize),
                PlotCoord.FromWorldColumn(20, 20, PlotSize));
        }

        // --- Membership ---

        [Fact]
        public void PositionInsideAnAreaPlot_ReportsInside_OutsidePlotReportsOutside()
        {
            // Area covers plots (0,0) and (1,0): world x in [0,16), z in [0,8).
            var area = new SurveyArea(1, "North Field", new[]
            {
                new PlotCoord(0, 0),
                new PlotCoord(1, 0),
            });

            Assert.True(area.ContainsWorldColumn(3, 3, PlotSize));   // plot (0,0)
            Assert.True(area.ContainsWorldColumn(12, 1, PlotSize));  // plot (1,0)
            Assert.False(area.ContainsWorldColumn(3, 12, PlotSize)); // plot (0,1) — not in the area
            Assert.False(area.ContainsWorldColumn(20, 3, PlotSize)); // plot (2,0) — not in the area
        }

        [Fact]
        public void Membership_OnPlotBoundary_IsDeterministic()
        {
            var area = new SurveyArea(1, "Edge", new[] { new PlotCoord(1, 0) });

            // x=8 is the first column of plot 1; x=7 is the last column of plot 0.
            Assert.True(area.ContainsWorldColumn(8, 0, PlotSize));
            Assert.False(area.ContainsWorldColumn(7, 0, PlotSize));
            Assert.True(area.ContainsWorldColumn(15, 0, PlotSize));
            Assert.False(area.ContainsWorldColumn(16, 0, PlotSize));
        }

        // --- Plot cap (R1b) ---

        [Fact]
        public void AreaExactlyAtCap_IsAccepted_OnePlotOver_IsRejected()
        {
            var atCap = new SurveyArea(1, "At cap", new[]
            {
                new PlotCoord(0, 0), new PlotCoord(1, 0), new PlotCoord(2, 0),
            });
            var overCap = new SurveyArea(2, "Over cap", new[]
            {
                new PlotCoord(0, 0), new PlotCoord(1, 0), new PlotCoord(2, 0), new PlotCoord(3, 0),
            });

            Assert.True(atCap.WithinPlotCap(maxPlots: 3));
            Assert.False(overCap.WithinPlotCap(maxPlots: 3));
        }

        [Fact]
        public void DuplicatePlotsInInput_AreDeduped_ForCapAndCount()
        {
            var area = new SurveyArea(1, "Dupes", new[]
            {
                new PlotCoord(0, 0), new PlotCoord(0, 0), new PlotCoord(1, 0),
            });

            Assert.Equal(2, area.PlotCount);
            Assert.True(area.WithinPlotCap(maxPlots: 2));
        }

        [Fact]
        public void PlotCount_ReflectsTheDrawnPlots()
        {
            var area = new SurveyArea(1, "Five", Enumerable.Range(0, 5).Select(i => new PlotCoord(i, 0)));
            Assert.Equal(5, area.PlotCount);
        }
    }
}
