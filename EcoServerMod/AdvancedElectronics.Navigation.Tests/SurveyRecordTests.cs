using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class SurveyRecordTests
    {
        private const int PlotSize = 8;
        private const string Iron = "IronOre";
        private const string Gold = "GoldOre";
        private const string Copper = "CopperOre";
        private const int AreaA = 1;
        private const int AreaB = 2;

        private static SurveyArea Area(int id, params PlotCoord[] plots) => new SurveyArea(id, "area" + id, plots);

        // --- Best finding: single ore, precise location, depth, concentration (AE5) ---

        [Fact]
        public void OreSampledInOneArea_BestFinding_CarriesPrecisePositionDepthAndConcentration()
        {
            var record = new SurveyRecord(PlotSize);

            // Plot (0,0): one ore block out of three sampled -> concentration 1/3.
            record.RecordSample(1, 60, 1, Iron, depthBelowSurface: 4, areaId: AreaA);
            record.RecordSample(2, 64, 1, null, depthBelowSurface: 0, areaId: AreaA);
            record.RecordSample(3, 64, 1, null, depthBelowSurface: 0, areaId: AreaA);

            var finding = record.BestFinding(AreaA, Iron);

            Assert.True(finding.Found);
            Assert.Equal(AreaA, finding.AreaId);
            Assert.Equal(Iron, finding.OreType);
            Assert.Equal(new BlockPos(1, 60, 1), finding.Position); // block-precise, not a plot coordinate
            Assert.Equal(4, finding.DepthBelowSurface);
            Assert.Equal(1f / 3f, finding.Concentration, precision: 5);
        }

        [Fact]
        public void SeveralOreTypes_EachGetsItsOwnFinding_WithFinerThanPlotLocationAndDepth()
        {
            var record = new SurveyRecord(PlotSize);

            // Plot (0,0): iron. Plot (2,0) (x=16..23): gold. Plot (4,0) (x=32..): copper.
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(17, 55, 1, Gold, 9, AreaA);
            record.RecordSample(33, 40, 1, Copper, 24, AreaA);

            var findings = record.Findings(AreaA).ToList();

            Assert.Equal(3, findings.Count);
            Assert.All(findings, f => Assert.True(f.Found));
            Assert.All(findings, f => Assert.Equal(AreaA, f.AreaId));
            // Each finding's position is a precise block, and each carries a depth.
            var iron = findings.Single(f => f.OreType == Iron);
            Assert.Equal(new BlockPos(1, 60, 1), iron.Position);
            Assert.Equal(4, iron.DepthBelowSurface);
            Assert.Equal(9, findings.Single(f => f.OreType == Gold).DepthBelowSurface);
            Assert.Equal(24, findings.Single(f => f.OreType == Copper).DepthBelowSurface);
        }

        // --- Concentration ranking: ratio wins over raw count (ported bug-catcher) ---

        [Fact]
        public void BestFinding_PicksHigherConcentrationPlot_NotHigherRawCount()
        {
            var record = new SurveyRecord(PlotSize);

            // Plot (0,0): 10 ore of 100 sampled -> 0.10. Every block below stays inside
            // plot (0,0) (x in [0,8), z in [0,8)); distinct y-levels give 100 distinct
            // positions without leaking into a neighbouring plot.
            for (int i = 0; i < 10; i++)
                record.RecordSample(i % 8, 64, i / 8, Iron, 5, AreaA); // 10 ore at y=64
            var placed = 0;
            for (int y = 63; placed < 90; y--)
                for (int x = 0; x < 8 && placed < 90; x++)
                    for (int z = 0; z < 8 && placed < 90; z++, placed++)
                        record.RecordSample(x, y, z, null, 0, AreaA); // 90 barren, same plot

            // Plot (2,0) (x=16..23): 3 ore of 5 sampled -> 0.60.
            record.RecordSample(16, 64, 1, Iron, 6, AreaA);
            record.RecordSample(17, 64, 1, Iron, 6, AreaA);
            record.RecordSample(18, 64, 1, Iron, 6, AreaA);
            record.RecordSample(19, 64, 1, null, 0, AreaA);
            record.RecordSample(20, 64, 1, null, 0, AreaA);

            var finding = record.BestFinding(AreaA, Iron);

            Assert.True(finding.Found);
            Assert.True(finding.Concentration > 0.5f);
            // The reported dig block is in the high-concentration plot (x 16..23).
            Assert.InRange(finding.Position.X, 16, 23);
        }

        // --- Shallowest sighting is the dig target ---

        [Fact]
        public void BestFinding_ReportsShallowestOreBlockInTheDensestPlot()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, depthBelowSurface: 12, areaId: AreaA);
            record.RecordSample(1, 55, 1, Iron, depthBelowSurface: 17, areaId: AreaA);
            record.RecordSample(2, 68, 1, Iron, depthBelowSurface: 4, areaId: AreaA);

            var finding = record.BestFinding(AreaA, Iron);

            Assert.Equal(4, finding.DepthBelowSurface);
            Assert.Equal(new BlockPos(2, 68, 1), finding.Position);
        }

        // --- Sampling idempotency (ported invariant) ---

        [Fact]
        public void SameBlockSampledTwice_CountsOnce_TowardConcentrationAndCoverage()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            record.RecordSample(4, 64, 4, Iron, 3, AreaA);
            record.RecordSample(4, 64, 4, Iron, 3, AreaA); // same exact block
            record.RecordSample(4, 64, 4, Iron, 3, AreaA); // and again

            var finding = record.BestFinding(AreaA, Iron);

            Assert.True(finding.Found);
            Assert.Equal(1f, finding.Concentration); // 1 ore / 1 sampled, not 3/3 inflated
            Assert.Equal(1f, record.Coverage(area));  // one plot, sampled once
        }

        [Fact]
        public void SameBlockResampledWithDifferentOre_KeepsFirstResult_PositionIsDedupeKey()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(7, 64, 7, Iron, 2, AreaA);
            record.RecordSample(7, 64, 7, Gold, 2, AreaA); // ignored: (7,64,7) already recorded

            Assert.True(record.BestFinding(AreaA, Iron).Found);
            Assert.False(record.BestFinding(AreaA, Gold).Found);
        }

        // --- Attribution across areas (AE2) ---

        [Fact]
        public void FindingsFromTwoAreas_StayAttributable_FilterToOneExcludesTheOther()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(1, 60, 1, Gold, 4, AreaB); // same column, different area — distinct position not needed since areas differ

            // Give area B a gold finding at its own distinct block.
            record.RecordSample(50, 40, 50, Gold, 20, AreaB);

            var aFindings = record.Findings(AreaA).ToList();
            var bFindings = record.Findings(AreaB).ToList();

            Assert.All(aFindings, f => Assert.Equal(AreaA, f.AreaId));
            Assert.All(bFindings, f => Assert.Equal(AreaB, f.AreaId));
            Assert.Contains(aFindings, f => f.OreType == Iron);
            Assert.DoesNotContain(aFindings, f => f.OreType == Gold);
            Assert.Contains(bFindings, f => f.OreType == Gold);
            Assert.DoesNotContain(bFindings, f => f.OreType == Iron);
        }

        [Fact]
        public void BestFinding_ForAnOreOnlyInAnotherArea_ReturnsNotFound()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);

            Assert.False(record.BestFinding(AreaB, Iron).Found);
        }

        // --- Coverage (R7a): zero vs surveyed-empty vs partial ---

        [Fact]
        public void Coverage_OfAreaWithNoSamples_IsZero()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0), new PlotCoord(1, 0));

            Assert.Equal(0f, record.Coverage(area));
        }

        [Fact]
        public void Coverage_SurveyedButNoOre_IsFull_AndDistinctFromNotSurveyed()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            // A sampled-but-no-ore block: the plot is surveyed, just barren.
            record.RecordSample(3, 64, 3, null, 0, AreaA);

            Assert.Equal(1f, record.Coverage(area));        // fully surveyed
            Assert.Empty(record.Findings(AreaA));           // but nothing found — legible as "surveyed, empty"
        }

        [Fact]
        public void Coverage_PartiallyWalkedArea_IsTheFractionOfPlotsTouched()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0), new PlotCoord(1, 0), new PlotCoord(2, 0), new PlotCoord(3, 0));

            // Sample in two of the four plots.
            record.RecordSample(3, 64, 3, null, 0, AreaA);   // plot (0,0)
            record.RecordSample(12, 64, 3, Iron, 5, AreaA);  // plot (1,0)

            Assert.Equal(0.5f, record.Coverage(area));
        }

        [Fact]
        public void Coverage_CountsOnlyPlotsInsideTheArea_NotStraySamplesOutsideIt()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            record.RecordSample(3, 64, 3, Iron, 5, AreaA);    // inside the area
            record.RecordSample(99, 64, 99, Iron, 5, AreaA);  // recorded to the area but in a plot the area doesn't cover

            // Coverage denominator is the area's plots; the stray sample doesn't push it above 1.
            Assert.Equal(1f, record.Coverage(area));
        }

        // --- Empty / not-found result shape ---

        [Fact]
        public void BestFinding_EmptyRecord_ReturnsNotFoundWithZeroedFields()
        {
            var record = new SurveyRecord(PlotSize);
            var finding = record.BestFinding(AreaA, Iron);

            Assert.False(finding.Found);
            Assert.Equal(0, finding.DepthBelowSurface);
        }

        [Fact]
        public void Constructor_RejectsNonPositivePlotSize()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SurveyRecord(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SurveyRecord(-8));
        }

        // --- ClearArea (reassign / delete support, R1a/R3a) ---

        [Fact]
        public void ClearArea_DropsThatAreasFindings_LeavesOthersIntact()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(50, 40, 50, Gold, 20, AreaB);

            record.ClearArea(AreaA);

            Assert.Empty(record.Findings(AreaA));
            Assert.Contains(record.Findings(AreaB), f => f.OreType == Gold);
        }
    }
}
