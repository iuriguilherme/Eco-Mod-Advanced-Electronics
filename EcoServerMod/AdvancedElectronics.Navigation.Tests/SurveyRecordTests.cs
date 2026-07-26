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
        private const string Limestone = "Limestone";
        private const int AreaA = 1;
        private const int AreaB = 2;

        private static SurveyArea Area(int id, params PlotCoord[] plots) => new SurveyArea(id, "area" + id, plots);

        // --- Quantity-led finding: area-total count, shallowest location, depth range (KTD2) ---

        [Fact]
        public void MaterialSampledInOneArea_Finding_CarriesCountShallowestPositionAndDepthRange()
        {
            var record = new SurveyRecord(PlotSize);

            // Three limestone blocks at different depths in one plot.
            record.RecordSample(1, 60, 1, Limestone, depthBelowSurface: 4, areaId: AreaA);
            record.RecordSample(1, 58, 1, Limestone, depthBelowSurface: 6, areaId: AreaA);
            record.RecordSample(1, 52, 1, Limestone, depthBelowSurface: 12, areaId: AreaA);

            var finding = record.MaterialFinding(AreaA, Limestone);

            Assert.True(finding.Found);
            Assert.Equal(AreaA, finding.AreaId);
            Assert.Equal(Limestone, finding.OreType);
            Assert.Equal(3, finding.Count);                              // area-total quantity
            Assert.Equal(new BlockPos(1, 60, 1), finding.Position);      // shallowest occurrence
            Assert.Equal(4, finding.DepthBelowSurface);                  // shallowest depth (== DepthMin)
            Assert.Equal(12, finding.DepthMax);                         // deepest depth
        }

        [Fact]
        public void Count_TotalsAcrossEveryPlotInTheArea_NotJustOnePlot()
        {
            var record = new SurveyRecord(PlotSize);

            // Plot (0,0): 2 iron. Plot (2,0) (x=16..23): 3 iron. Area total = 5.
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(2, 60, 1, Iron, 4, AreaA);
            record.RecordSample(16, 64, 1, Iron, 6, AreaA);
            record.RecordSample(17, 64, 1, Iron, 6, AreaA);
            record.RecordSample(18, 64, 1, Iron, 6, AreaA);

            var finding = record.MaterialFinding(AreaA, Iron);

            Assert.Equal(5, finding.Count);
        }

        [Fact]
        public void SeveralMaterials_EachGetsItsOwnFinding_WithCountAndDepthRange()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(17, 55, 1, Gold, 9, AreaA);
            record.RecordSample(33, 40, 1, Limestone, 24, AreaA);

            var findings = record.Findings(AreaA).ToList();

            Assert.Equal(3, findings.Count);
            Assert.All(findings, f => Assert.True(f.Found));
            Assert.All(findings, f => Assert.Equal(1, f.Count));
            Assert.Equal(4, findings.Single(f => f.OreType == Iron).DepthBelowSurface);
            Assert.Equal(9, findings.Single(f => f.OreType == Gold).DepthBelowSurface);
            Assert.Equal(24, findings.Single(f => f.OreType == Limestone).DepthBelowSurface);
        }

        // --- Shallowest sighting is the dig target, across the whole area ---

        [Fact]
        public void Finding_ReportsShallowestOccurrenceAcrossPlots_AndBracketsDepthRange()
        {
            var record = new SurveyRecord(PlotSize);

            // Deeper occurrence in plot (0,0); shallower in plot (2,0).
            record.RecordSample(1, 55, 1, Iron, depthBelowSurface: 17, areaId: AreaA);
            record.RecordSample(1, 60, 1, Iron, depthBelowSurface: 12, areaId: AreaA);
            record.RecordSample(18, 68, 1, Iron, depthBelowSurface: 4, areaId: AreaA);

            var finding = record.MaterialFinding(AreaA, Iron);

            Assert.Equal(4, finding.DepthBelowSurface);
            Assert.Equal(new BlockPos(18, 68, 1), finding.Position);
            Assert.Equal(17, finding.DepthMax);
        }

        // --- Sampling idempotency (ported invariant) ---

        [Fact]
        public void SameBlockSampledTwice_CountsOnce_TowardQuantityAndCoverage()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            record.RecordSample(4, 64, 4, Iron, 3, AreaA);
            record.RecordSample(4, 64, 4, Iron, 3, AreaA); // same exact block
            record.RecordSample(4, 64, 4, Iron, 3, AreaA); // and again

            var finding = record.MaterialFinding(AreaA, Iron);

            Assert.True(finding.Found);
            Assert.Equal(1, finding.Count);            // 1 block, not 3 inflated
            Assert.Equal(1f, record.Coverage(area));   // one plot, sampled once
        }

        [Fact]
        public void SameBlockResampledWithDifferentMaterial_KeepsFirstResult_PositionIsDedupeKey()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(7, 64, 7, Iron, 2, AreaA);
            record.RecordSample(7, 64, 7, Gold, 2, AreaA); // ignored: (7,64,7) already recorded

            Assert.True(record.MaterialFinding(AreaA, Iron).Found);
            Assert.False(record.MaterialFinding(AreaA, Gold).Found);
        }

        // --- Attribution across areas (AE2) ---

        [Fact]
        public void FindingsFromTwoAreas_StayAttributable_FilterToOneExcludesTheOther()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(1, 60, 1, Gold, 4, AreaB);
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
        public void MaterialFinding_ForAMaterialOnlyInAnotherArea_ReturnsNotFound()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);

            Assert.False(record.MaterialFinding(AreaB, Iron).Found);
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
        public void Coverage_SurveyedButNoMaterial_IsFull_AndDistinctFromNotSurveyed()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            record.RecordSample(3, 64, 3, null, 0, AreaA);

            Assert.Equal(1f, record.Coverage(area));
            Assert.Empty(record.Findings(AreaA));
        }

        [Fact]
        public void Coverage_PartiallyWalkedArea_IsTheFractionOfPlotsTouched()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0), new PlotCoord(1, 0), new PlotCoord(2, 0), new PlotCoord(3, 0));

            record.RecordSample(3, 64, 3, null, 0, AreaA);   // plot (0,0)
            record.RecordSample(12, 64, 3, Iron, 5, AreaA);  // plot (1,0)

            Assert.Equal(0.5f, record.Coverage(area));
        }

        [Fact]
        public void Coverage_CountsOnlyPlotsInsideTheArea_NotStraySamplesOutsideIt()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            record.RecordSample(3, 64, 3, Iron, 5, AreaA);
            record.RecordSample(99, 64, 99, Iron, 5, AreaA);

            Assert.Equal(1f, record.Coverage(area));
        }

        // --- Empty / not-found result shape ---

        [Fact]
        public void MaterialFinding_EmptyRecord_ReturnsNotFoundWithZeroedFields()
        {
            var record = new SurveyRecord(PlotSize);
            var finding = record.MaterialFinding(AreaA, Iron);

            Assert.False(finding.Found);
            Assert.Equal(0, finding.Count);
            Assert.Equal(0, finding.DepthBelowSurface);
        }

        // --- Median surface level ---

        [Fact]
        public void MedianSurfaceLevel_OfOddColumnSet_IsTheMiddleValue()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSurface(AreaA, 0, 0, 60);
            record.RecordSurface(AreaA, 1, 0, 70);
            record.RecordSurface(AreaA, 2, 0, 64);

            Assert.Equal(64, record.MedianSurfaceLevel(AreaA));
        }

        [Fact]
        public void MedianSurfaceLevel_OfEvenColumnSet_AveragesTheTwoMiddles_AndIsRobustToOutliers()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSurface(AreaA, 0, 0, 60);
            record.RecordSurface(AreaA, 1, 0, 62);
            record.RecordSurface(AreaA, 2, 0, 64);
            record.RecordSurface(AreaA, 3, 0, 200); // a cliff column does not skew the median

            Assert.Equal(63, record.MedianSurfaceLevel(AreaA)); // (62+64)/2, not dragged up by 200
        }

        [Fact]
        public void MedianSurfaceLevel_IsPerColumnDeduped_AndPerArea()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSurface(AreaA, 5, 5, 61);
            record.RecordSurface(AreaA, 5, 5, 61); // same column again — no double-count
            record.RecordSurface(AreaB, 5, 5, 99);

            Assert.Equal(61, record.MedianSurfaceLevel(AreaA));
            Assert.Equal(99, record.MedianSurfaceLevel(AreaB));
        }

        [Fact]
        public void MedianSurfaceLevel_NoData_IsNull_AndClearAreaDropsIt()
        {
            var record = new SurveyRecord(PlotSize);
            Assert.Null(record.MedianSurfaceLevel(AreaA));

            record.RecordSurface(AreaA, 0, 0, 60);
            record.ClearArea(AreaA);
            Assert.Null(record.MedianSurfaceLevel(AreaA));
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
