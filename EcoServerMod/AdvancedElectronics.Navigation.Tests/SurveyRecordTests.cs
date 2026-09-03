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

        // --- U4: the at-bedrock observation, recorded per column and folded per plot (KTD4) ---
        //
        // A two-block plot size is used deliberately so a plot is four columns and the
        // fold can be stated exhaustively; the shipped plot is eight blocks a side.
        private const int TinyPlot = 2;

        [Fact]
        public void PlotWhoseEveryColumnRestsOnBedrock_ReadsAtBedrock()
        {
            var record = new SurveyRecord(TinyPlot);
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);
            record.RecordColumnBedrock(AreaA, 1, 1, true);

            Assert.True(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
        }

        [Fact]
        public void PlotWithThreeOfFourColumnsAtBedrock_DoesNotReadAtBedrock()
        {
            var record = new SurveyRecord(TinyPlot);
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);
            record.RecordColumnBedrock(AreaA, 1, 1, false);

            Assert.False(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
        }

        [Fact]
        public void PlotWithAColumnNeverObserved_DoesNotReadAtBedrock()
        {
            // A pass that stopped part-way through a plot has not proven the plot.
            var record = new SurveyRecord(TinyPlot);
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);

            Assert.False(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
        }

        [Fact]
        public void PlotWithNoObservationAtAll_DoesNotReadAtBedrock()
        {
            var record = new SurveyRecord(TinyPlot);

            Assert.False(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
        }

        [Fact]
        public void ReObservingAColumn_ReplacesItsEarlierAnswer()
        {
            // The ground changes between passes; the newest observation is the true one.
            var record = new SurveyRecord(TinyPlot);
            record.RecordColumnBedrock(AreaA, 0, 0, false);
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);
            record.RecordColumnBedrock(AreaA, 1, 1, true);

            Assert.True(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
        }

        [Fact]
        public void BedrockPlots_ListsOnlyThePlotsEveryColumnOfWhichIsAtBedrock()
        {
            var record = new SurveyRecord(TinyPlot);
            // plot (0,0): all four at bedrock.
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);
            record.RecordColumnBedrock(AreaA, 1, 1, true);
            // plot (1,0): one column still has ground over the floor.
            record.RecordColumnBedrock(AreaA, 2, 0, true);
            record.RecordColumnBedrock(AreaA, 3, 0, true);
            record.RecordColumnBedrock(AreaA, 2, 1, true);
            record.RecordColumnBedrock(AreaA, 3, 1, false);

            Assert.Equal(new[] { new PlotCoord(0, 0) }, record.BedrockPlots(AreaA).ToArray());
        }

        [Fact]
        public void BedrockObservations_AreScopedToTheirArea()
        {
            var record = new SurveyRecord(TinyPlot);
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);
            record.RecordColumnBedrock(AreaA, 1, 1, true);

            Assert.True(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
            Assert.False(record.PlotRestsOnBedrock(AreaB, new PlotCoord(0, 0)));
            Assert.Empty(record.BedrockPlots(AreaB));
        }

        [Fact]
        public void ClearArea_DropsThatAreasBedrockObservations()
        {
            var record = new SurveyRecord(TinyPlot);
            record.RecordColumnBedrock(AreaA, 0, 0, true);
            record.RecordColumnBedrock(AreaA, 1, 0, true);
            record.RecordColumnBedrock(AreaA, 0, 1, true);
            record.RecordColumnBedrock(AreaA, 1, 1, true);

            record.ClearArea(AreaA);

            Assert.False(record.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
            Assert.Empty(record.BedrockPlots(AreaA));
        }

        // --- U7: a resurvey clears; a resumed pass does not (R10-R13, R25) ---

        /// <summary>
        /// Covers AE2, R12. The stale-findings fault, written as the failing case the U7
        /// execution note asks for: a record that already holds a block, re-sampled after
        /// that block changed. The clear a newly started pass performs (R10) has to reach
        /// the SAMPLED-BLOCK set as well as the findings, or the second pass is silently
        /// deduped against the first and reports material the ground no longer holds.
        ///
        /// A second area overlapping the same plot is what makes the fault reachable
        /// rather than hypothetical (R35 allows exactly this): the sampled-block set was
        /// area-blind, so clearing one area could not drop a block another area's geometry
        /// still covered.
        /// </summary>
        [Fact]
        public void ResampledBlockWhoseMaterialChanged_ReportsWhatTheNewPassSaw_NotWhatTheOldOneDid()
        {
            var record = new SurveyRecord(PlotSize);

            record.RecordSample(4, 64, 4, Iron, 3, AreaA);
            record.RecordSample(5, 64, 5, Iron, 3, AreaB); // AreaB covers plot (0, 0) too

            // A player digs the iron out and backfills with limestone. The resurvey starts,
            // clearing AreaA (R10), and samples the same block again.
            record.ClearArea(AreaA);
            record.RecordSample(4, 64, 4, Limestone, 3, AreaA);

            Assert.False(record.MaterialFinding(AreaA, Iron).Found);
            Assert.True(record.MaterialFinding(AreaA, Limestone).Found);
            Assert.Equal(1f, record.Coverage(Area(AreaA, new PlotCoord(0, 0))));
        }

        /// <summary>
        /// Sweeps one column the way <c>OreSensorComponent.SampleColumn</c> does: surface height,
        /// at-bedrock observation, then <paramref name="depth"/> blocks downward from the surface,
        /// with ore at <paramref name="oreDepth"/> if one is named. Column-shaped on purpose --
        /// the persisted pass record is one row per column, so a test that samples loose blocks
        /// would not exercise the shape that actually round-trips.
        /// </summary>
        private static void SweepColumn(SurveyRecord record, int areaId, int x, int z,
            int surfaceY, int depth, string ore = null, int oreDepth = -1, bool restsOnBedrock = false)
        {
            record.RecordSurface(areaId, x, z, surfaceY);
            record.RecordColumnBedrock(areaId, x, z, restsOnBedrock);
            for (var d = 0; d < depth; d++)
                record.RecordSample(x, surfaceY - d, z, d == oreDepth ? ore : null, d, areaId);
        }

        /// <summary>Rehydrates a fresh record from what the area would have persisted: the pass's columns, its findings rows, its cursor.</summary>
        private static SurveyRecord Rehydrate(SurveyRecord source, int areaId, int plotSize)
        {
            var columns = source.PassColumns(areaId).ToList();
            var rows = source.Findings(areaId).ToList();
            var cursor = source.SweepCursorFor(areaId);

            var restored = new SurveyRecord(plotSize);
            foreach (var column in columns)
                restored.RestorePassColumn(areaId, column);
            foreach (var row in rows)
                restored.RestoreFinding(areaId, row);
            restored.SetSweepCursor(areaId, cursor.PlotIndex, cursor.ColumnCursor);
            return restored;
        }

        /// <summary>
        /// Covers AE7, R11. A newly started pass clears the area, so coverage reads zero however
        /// complete the previous pass was, and climbs from there. It never shows a figure blending
        /// the two passes.
        /// </summary>
        [Fact]
        public void NewlyStartedPass_ReadsZeroCoverage_ThenClimbs()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA, new PlotCoord(0, 0), new PlotCoord(1, 0));

            SweepColumn(record, AreaA, 0, 0, 64, 15, Iron, 3);
            SweepColumn(record, AreaA, 8, 0, 64, 15);
            Assert.Equal(1f, record.Coverage(area));

            record.ClearArea(AreaA); // R10: a newly started resurvey

            Assert.Equal(0f, record.Coverage(area));
            Assert.Empty(record.Findings(AreaA));

            SweepColumn(record, AreaA, 0, 0, 64, 15);
            Assert.Equal(0.5f, record.Coverage(area));
        }

        /// <summary>
        /// R25. A pass stopped at 40% resumes at 40%, not zero: the persisted per-column record
        /// rebuilds the sampled-block set and the per-plot sampled counts, which is what coverage
        /// is measured over.
        /// </summary>
        [Fact]
        public void PassInterruptedAtFortyPercent_ResumesAtFortyPercent_NotZero()
        {
            var record = new SurveyRecord(PlotSize);
            var area = Area(AreaA,
                new PlotCoord(0, 0), new PlotCoord(1, 0), new PlotCoord(2, 0), new PlotCoord(3, 0), new PlotCoord(4, 0));

            SweepColumn(record, AreaA, 0, 0, 64, 15, Iron, 2);
            SweepColumn(record, AreaA, 8, 0, 70, 15);
            record.SetSweepCursor(AreaA, 2, 0);
            Assert.Equal(0.4f, record.Coverage(area));

            var resumed = Rehydrate(record, AreaA, PlotSize);

            Assert.Equal(0.4f, resumed.Coverage(area));
            Assert.Equal(2, resumed.SweepCursorFor(AreaA).PlotIndex);
            Assert.Equal(0, resumed.SweepCursorFor(AreaA).ColumnCursor);
        }

        /// <summary>
        /// R25. A resumed pass does not re-sample blocks the stopped pass recorded: the rebuilt
        /// sampled-block set still dedupes them, so ground already covered cannot be counted twice
        /// (nor, with the cursor, re-flown).
        /// </summary>
        [Fact]
        public void ResumedPass_DoesNotReSampleBlocksTheStoppedPassRecorded()
        {
            var record = new SurveyRecord(PlotSize);
            SweepColumn(record, AreaA, 0, 0, 64, 15, Iron, 2);

            var resumed = Rehydrate(record, AreaA, PlotSize);
            var before = resumed.Findings(AreaA).Single(f => f.OreType == Iron);

            // The drone flies the same column again -- the block is already recorded, so nothing
            // moves: not the count, not the sampled denominator behind the concentration.
            for (var d = 0; d < 15; d++)
                resumed.RecordSample(0, 64 - d, 0, d == 2 ? Iron : null, d, AreaA);

            var after = resumed.Findings(AreaA).Single(f => f.OreType == Iron);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before.Concentration, after.Concentration);
        }

        /// <summary>
        /// R25. The record round-trips through its projection with its sampled set, its column
        /// observations and its cursor intact -- the whole of what a stopped pass knew.
        /// </summary>
        [Fact]
        public void Record_RoundTripsThroughItsProjection_WithSampledSetAndCursorIntact()
        {
            var record = new SurveyRecord(TinyPlot);
            SweepColumn(record, AreaA, 0, 0, 64, 4, Iron, 1, restsOnBedrock: true);
            SweepColumn(record, AreaA, 1, 0, 64, 4, restsOnBedrock: true);
            SweepColumn(record, AreaA, 0, 1, 64, 4, restsOnBedrock: true);
            SweepColumn(record, AreaA, 1, 1, 64, 4, restsOnBedrock: true);
            record.SetSweepCursor(AreaA, 1, 3);

            var restored = Rehydrate(record, AreaA, TinyPlot);

            Assert.Equal(record.Coverage(Area(AreaA, new PlotCoord(0, 0))), restored.Coverage(Area(AreaA, new PlotCoord(0, 0))));
            Assert.Equal(record.MedianSurfaceLevel(AreaA), restored.MedianSurfaceLevel(AreaA));
            Assert.True(restored.PlotRestsOnBedrock(AreaA, new PlotCoord(0, 0)));
            Assert.Equal(new[] { new PlotCoord(0, 0) }, restored.BedrockPlots(AreaA).ToArray());
            Assert.Equal(1, restored.SweepCursorFor(AreaA).PlotIndex);
            Assert.Equal(3, restored.SweepCursorFor(AreaA).ColumnCursor);

            var before = record.Findings(AreaA).Single();
            var after = restored.Findings(AreaA).Single();
            Assert.Equal(before.OreType, after.OreType);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before.Position, after.Position);
            Assert.Equal(before.DepthBelowSurface, after.DepthBelowSurface);
            Assert.Equal(before.DepthMax, after.DepthMax);
            Assert.Equal(before.Concentration, after.Concentration);
        }

        /// <summary>
        /// R25. A rehydrated record does not clobber the persisted findings snapshot it was
        /// rehydrated from. The dock projects the LIVE record back over the area every readout
        /// tick, so a record restored with samples but WITHOUT their findings would read as
        /// covered-and-empty and overwrite a good snapshot with nothing on the first tick after a
        /// restart. Restoring both halves is what makes the write-back a no-op.
        /// </summary>
        [Fact]
        public void RehydratedRecord_ProjectsBackThePersistedFindings_NotAnEmptySnapshot()
        {
            var record = new SurveyRecord(PlotSize);
            SweepColumn(record, AreaA, 0, 0, 64, 15, Iron, 2);
            SweepColumn(record, AreaA, 8, 0, 64, 15, Gold, 5);
            var persisted = record.Findings(AreaA).ToList();

            var restored = Rehydrate(record, AreaA, PlotSize);
            var writtenBack = restored.Findings(AreaA).ToList();

            Assert.Equal(persisted.Count, writtenBack.Count);
            Assert.NotEmpty(writtenBack);
            foreach (var row in persisted)
                Assert.Contains(writtenBack, w => w.Plot.Equals(row.Plot) && w.OreType == row.OreType && w.Count == row.Count);

            // And the empty case the persist guard is there for stays empty, so the guard still fires.
            Assert.Empty(new SurveyRecord(PlotSize).Findings(AreaA));
        }

        /// <summary>
        /// Covers AE3, R13. The inversion the plan flags as most likely to be coded backwards: a
        /// resurvey clears the area's findings and leaves the MINED stamps, so an area mined at
        /// 200 and resurveyed at 300 is mineable again -- 300 has a 200 left to postdate.
        ///
        /// Mirrors the sequence <c>SurveyAreaEntry.ClearFindings</c> performs on the Eco side,
        /// where the guarantee is structural: the clear touches Findings, SurveyedStamps,
        /// BedrockPlotCoords and the sweep, and never MinedStamps.
        /// </summary>
        [Fact]
        public void Resurvey_ClearsTheFindings_AndLeavesTheMinedStamps()
        {
            var plot = new PlotCoord(0, 0);
            var record = new SurveyRecord(PlotSize);
            var surveyed = new PlotStampAccumulator();
            var mined = new PlotStampAccumulator();

            SweepColumn(record, AreaA, 0, 0, 64, 15, Iron, 2);
            surveyed.Record(plot, 100);
            mined.Record(plot, 200);
            Assert.False(PlotFreshness.IsMineable(surveyed.StampFor(plot), mined.StampFor(plot)));

            // A newly started resurvey: findings, live record and surveyed stamps go (R10); the
            // mined stamps are not among the things a survey may falsify (R13).
            record.ClearArea(AreaA);
            surveyed = new PlotStampAccumulator();

            Assert.Empty(record.Findings(AreaA));
            Assert.Equal(200, mined.StampFor(plot));

            // The pass completes at 300 and the area is mineable again.
            surveyed.Record(plot, 300);
            Assert.Equal(200, mined.StampFor(plot));
            Assert.True(PlotFreshness.IsMineable(surveyed.StampFor(plot), mined.StampFor(plot)));
        }
    }
}
