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

        // --- U1: per-plot findings rows (KTD1, R16/R20) ---

        [Fact]
        public void Findings_ProjectOneRowPerPlotPerOre_EachRowNamingItsOwnPlot()
        {
            var record = new SurveyRecord(PlotSize);

            // Plot (0,0): iron. Plot (2,0) (x = 16..23): gold.
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(16, 58, 1, Gold, 6, AreaA);

            var rows = record.Findings(AreaA).ToList();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.True(r.HasPlot));
            var iron = Assert.Single(rows.Where(r => r.OreType == Iron));
            var gold = Assert.Single(rows.Where(r => r.OreType == Gold));
            Assert.Equal(new PlotCoord(0, 0), iron.Plot);
            Assert.Equal(new PlotCoord(2, 0), gold.Plot);
            Assert.Equal(1, iron.Count);
            Assert.Equal(1, gold.Count);
        }

        [Fact]
        public void Findings_SameOreInTwoPlots_ProjectsTwoRows_NotOneAreaTotalRow()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(2, 60, 1, Iron, 4, AreaA);
            record.RecordSample(16, 64, 1, Iron, 6, AreaA);

            var rows = record.Findings(AreaA).Where(r => r.OreType == Iron).ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal(2, rows.Single(r => r.Plot.Equals(new PlotCoord(0, 0))).Count);
            Assert.Equal(1, rows.Single(r => r.Plot.Equals(new PlotCoord(2, 0))).Count);
        }

        [Fact]
        public void Findings_TwoOresInOnePlot_ProjectTwoRowsForThatPlot_NotOneMergedRow()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(2, 58, 1, Gold, 6, AreaA);

            var rows = record.Findings(AreaA).ToList();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(new PlotCoord(0, 0), r.Plot));
            Assert.Equal(new[] { Gold, Iron }, rows.Select(r => r.OreType).OrderBy(o => o).ToArray());
        }

        [Fact]
        public void Findings_ForOnePlot_ReturnsOnlyThatPlotsRows()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);      // plot (0,0)
            record.RecordSample(16, 58, 1, Gold, 6, AreaA);     // plot (2,0)
            record.RecordSample(32, 50, 1, Limestone, 9, AreaA); // plot (4,0)

            var rows = record.Findings(AreaA, new PlotCoord(2, 0)).ToList();

            var only = Assert.Single(rows);
            Assert.Equal(Gold, only.OreType);
            Assert.Equal(new PlotCoord(2, 0), only.Plot);
        }

        [Fact]
        public void Findings_ForAPlotWithNoSamples_IsEmpty()
        {
            var record = new SurveyRecord(PlotSize);
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);

            Assert.Empty(record.Findings(AreaA, new PlotCoord(9, 9)));
        }

        [Fact]
        public void Findings_EmptyRecord_ProjectsNoRows_SoThePersistGuardStillRefuses()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0));

            Assert.Empty(record.Findings(AreaA));
            Assert.Empty(record.Findings(AreaA, new PlotCoord(0, 0)));
            Assert.Equal(0f, record.Coverage(area)); // the coverage-zero clobber guard's input
        }

        // --- U1: area totals re-derived from the rows (KTD1) ---

        [Fact]
        public void AreaTotals_ForAnOre_SumTheRowCountsAcrossPlots()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(2, 60, 1, Iron, 4, AreaA);
            record.RecordSample(16, 64, 1, Iron, 6, AreaA);
            record.RecordSample(17, 64, 1, Iron, 6, AreaA);
            record.RecordSample(18, 64, 1, Iron, 6, AreaA);

            var rows = record.Findings(AreaA).ToList();
            var total = Assert.Single(SurveyRecord.AreaTotals(rows));

            Assert.Equal(record.MaterialFinding(AreaA, Iron).Count, total.Count);
            Assert.Equal(5, total.Count);
            Assert.Equal(AreaA, total.AreaId);
            Assert.False(total.HasPlot); // an area total belongs to no single plot
        }

        [Fact]
        public void AreaTotals_KeepTheShallowestOccurrence_AndBracketTheDepthRange()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 40, 1, Iron, 20, AreaA);   // deep, plot (0,0)
            record.RecordSample(16, 70, 1, Iron, 3, AreaA);   // shallowest, plot (2,0)
            record.RecordSample(17, 55, 1, Iron, 11, AreaA);  // middle, plot (2,0)

            var total = Assert.Single(SurveyRecord.AreaTotals(record.Findings(AreaA)));
            var direct = record.MaterialFinding(AreaA, Iron);

            Assert.Equal(direct.Position, total.Position);
            Assert.Equal(new BlockPos(16, 70, 1), total.Position);
            Assert.Equal(3, total.DepthBelowSurface);
            Assert.Equal(20, total.DepthMax);
        }

        [Fact]
        public void AreaTotals_SeparateTheOres_AndKeepTheAreaAttribution()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(16, 58, 1, Gold, 6, AreaA);
            record.RecordSample(17, 58, 1, Gold, 6, AreaA);

            var totals = SurveyRecord.AreaTotals(record.Findings(AreaA)).ToList();

            Assert.Equal(2, totals.Count);
            Assert.Equal(1, totals.Single(t => t.OreType == Iron).Count);
            Assert.Equal(2, totals.Single(t => t.OreType == Gold).Count);
            Assert.All(totals, t => Assert.Equal(AreaA, t.AreaId));
        }

        [Fact]
        public void AreaTotals_OfNoRows_IsEmpty()
        {
            Assert.Empty(SurveyRecord.AreaTotals(System.Linq.Enumerable.Empty<SurveyFinding>()));
            Assert.Empty(SurveyRecord.AreaTotals(null));
        }

        [Fact]
        public void AreaTotals_Concentration_IsTheRatioOverTheSampledBlocksTheRowsAccountFor()
        {
            var record = new SurveyRecord(PlotSize);

            // Plot (0,0): 1 iron out of 2 sampled blocks. Plot (2,0): 1 iron out of 4 sampled.
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            record.RecordSample(1, 59, 1, null, 5, AreaA);
            record.RecordSample(16, 60, 1, Iron, 4, AreaA);
            record.RecordSample(17, 60, 1, null, 4, AreaA);
            record.RecordSample(18, 60, 1, null, 4, AreaA);
            record.RecordSample(19, 60, 1, null, 4, AreaA);

            var total = Assert.Single(SurveyRecord.AreaTotals(record.Findings(AreaA)));

            // 2 iron blocks over the 6 sampled blocks in the plots that carry iron.
            Assert.Equal(2f / 6f, total.Concentration, 4);
        }

        // --- U1: the findings-version marker that detects a pre-U1 save (KTD1) ---

        [Fact]
        public void FindingsVersion_Zero_IsStale_SoAnOldSavesFindingsAreDiscarded()
        {
            Assert.True(FindingsVersion.IsStale(0));
        }

        [Fact]
        public void FindingsVersion_Current_IsNotStale_EvenForAnAreaHoldingPlotZeroZero()
        {
            // The marker is an explicit version, never an absent plot: plot (0,0) is a real plot
            // near the world origin, so it can never stand in for "this row predates U1".
            Assert.False(FindingsVersion.IsStale(FindingsVersion.Current));
            Assert.True(FindingsVersion.Current > 0);

            var record = new SurveyRecord(PlotSize);
            record.RecordSample(1, 60, 1, Iron, 4, AreaA);
            var row = Assert.Single(record.Findings(AreaA));
            Assert.Equal(new PlotCoord(0, 0), row.Plot);
            Assert.True(row.HasPlot);
        }

        [Fact]
        public void FindingsVersion_AFutureVersion_IsNotTreatedAsStale()
        {
            Assert.False(FindingsVersion.IsStale(FindingsVersion.Current + 1));
        }

        // --- U1: the plot rides on the finding itself ---

        [Fact]
        public void SurveyFinding_CreatedWithoutAPlot_ReportsHasPlotFalse()
        {
            var f = SurveyFinding.Create(AreaA, Iron, 3, new BlockPos(1, 2, 3), 4, 9, 0.5f);

            Assert.True(f.Found);
            Assert.False(f.HasPlot);
            Assert.Equal(default(PlotCoord), f.Plot);
        }

        [Fact]
        public void SurveyFinding_CreatedInAPlot_CarriesItAndComparesOnIt()
        {
            var a = SurveyFinding.CreateInPlot(AreaA, new PlotCoord(3, -2), Iron, 3, new BlockPos(1, 2, 3), 4, 9, 0.5f);
            var b = SurveyFinding.CreateInPlot(AreaA, new PlotCoord(3, -2), Iron, 3, new BlockPos(1, 2, 3), 4, 9, 0.5f);
            var elsewhere = SurveyFinding.CreateInPlot(AreaA, new PlotCoord(4, -2), Iron, 3, new BlockPos(1, 2, 3), 4, 9, 0.5f);

            Assert.True(a.HasPlot);
            Assert.Equal(new PlotCoord(3, -2), a.Plot);
            Assert.Equal(a, b);
            Assert.NotEqual(a, elsewhere);
            Assert.NotEqual(a, SurveyFinding.Create(AreaA, Iron, 3, new BlockPos(1, 2, 3), 4, 9, 0.5f));
        }

        [Fact]
        public void SurveyFinding_NotFound_HasNoPlot()
        {
            Assert.False(SurveyFinding.NotFound.HasPlot);
        }
    }
}
