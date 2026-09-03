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

        // --- U9: what an edit preserves, drops, and adds (R20, R21, R22) ---
        //
        // A row of plots on Z=0 throughout, so raster order (Z then X) is just X ascending and a
        // cursor index reads as "how many plots in". Where a test needs a plot that sorts BEFORE
        // the row, it uses Z=-1, which raster order puts ahead of everything.

        private static PlotCoord[] Row(int from, int count) =>
            Enumerable.Range(from, count).Select(i => new PlotCoord(i, 0)).ToArray();

        [Fact]
        public void EditExtendingTenPlotsToTwelve_RetainsAllTen_AndAddsTwo()
        {
            var plan = AreaEdit.Plan(Row(0, 10), Row(0, 12));

            Assert.Equal(10, plan.Retained.Count);
            Assert.Empty(plan.Removed);
            Assert.Equal(new[] { new PlotCoord(10, 0), new PlotCoord(11, 0) }, plan.Added);
            Assert.True(plan.AddsOnly);
        }

        [Fact]
        public void EditRemovingThreePlots_KeepsTheOtherSeven_AndNamesExactlyTheThree()
        {
            var plan = AreaEdit.Plan(Row(0, 10), Row(0, 7));

            Assert.Equal(Row(0, 7), plan.Retained);
            Assert.Equal(new[] { new PlotCoord(7, 0), new PlotCoord(8, 0), new PlotCoord(9, 0) }, plan.Removed);
            Assert.Empty(plan.Added);
            Assert.False(plan.AddsOnly);
        }

        /// <summary>
        /// R20/R22. Coverage falls by the share the NEW plots represent and by nothing else: ten
        /// surveyed plots read 100% of ten and 83% of twelve. The retained plots' survey is still
        /// there, which is the whole visible difference from the wholesale clear this replaces.
        /// </summary>
        [Fact]
        public void ExtendingASurveyedArea_MovesCoverageOnlyByTheAddedPlotsShare()
        {
            Assert.Equal(100d * 10d / 12d, AreaEdit.RescaleCoverage(100f, plotsBefore: 10, plotsAfter: 12), 3);
        }

        /// <summary>
        /// R20. Removing three surveyed plots from a fully surveyed area of ten leaves seven
        /// surveyed plots out of seven. The caller has already taken the removed plots' share out
        /// over the OLD count (100 - 30 = 70), so the rescale restates 70% of ten as 100% of seven.
        /// </summary>
        [Fact]
        public void RemovingSurveyedPlots_LeavesTheRestFullySurveyed()
        {
            Assert.Equal(100d, AreaEdit.RescaleCoverage(70f, plotsBefore: 10, plotsAfter: 7), 3);
        }

        [Fact]
        public void CoverageOfAnAreaEditedToNothing_IsZeroRatherThanADivideByZero()
        {
            Assert.Equal(0f, AreaEdit.RescaleCoverage(100f, plotsBefore: 10, plotsAfter: 0));
            Assert.Equal(0f, AreaEdit.RescaleCoverage(0f, plotsBefore: 0, plotsAfter: 5));
        }

        /// <summary>
        /// R22. A pass three plots into a ten-plot row, edited to twelve: the sweep is still on
        /// the plot it was on, at the column it was on. The added plots sort after it, so nothing
        /// rewinds and the drone does not re-fly the two plots behind it.
        /// </summary>
        [Fact]
        public void PassMidSweep_SurvivesAnEditThatOnlyAppends_KeepingItsPlaceAndItsColumn()
        {
            var cursor = AreaEdit.RemapSweep(Row(0, 10), Row(0, 12), plotIndex: 3, columnCursor: 17);

            Assert.Equal(3, cursor.PlotIndex);
            Assert.Equal(17, cursor.ColumnCursor);
        }

        /// <summary>
        /// R22. The same pass when the edit added a plot that sorts BEHIND the cursor. The added
        /// plot is unsurveyed and would never be reached, so the sweep rewinds to it -- the same
        /// cure an outside change's reset takes. Re-flying the plots in between costs travel only:
        /// their samples are still in the record and dedupe on arrival.
        /// </summary>
        [Fact]
        public void PassMidSweep_RewindsToAnAddedPlotThatSortsBehindTheCursor()
        {
            var before = Row(0, 10);
            var after = before.Concat(new[] { new PlotCoord(4, -1) }).ToArray();

            var cursor = AreaEdit.RemapSweep(before, after, plotIndex: 3, columnCursor: 17);

            // (4, -1) sorts first in raster order, so the whole row shifts one along and the
            // sweep goes back to index 0 -- the added plot itself.
            Assert.Equal(0, cursor.PlotIndex);
            Assert.Equal(0, cursor.ColumnCursor);
        }

        /// <summary>
        /// R22. The cursor is carried across by NAME, not by index. Removing two plots from the
        /// FRONT of the row leaves the sweep standing on the very same plot -- three in before,
        /// one in after -- rather than two plots further along, which is what leaving the index
        /// alone would have done.
        /// </summary>
        [Fact]
        public void PassMidSweep_KeepsItsPlot_WhenAnEditRemovesGroundBehindIt()
        {
            var cursor = AreaEdit.RemapSweep(Row(0, 10), Row(2, 8), plotIndex: 3, columnCursor: 17);

            Assert.Equal(1, cursor.PlotIndex);
            Assert.Equal(17, cursor.ColumnCursor);
        }

        /// <summary>
        /// R22. When the edit removes the plot the sweep was standing on, the pass resumes at the
        /// first plot after it that survived -- everything before that has already been flown --
        /// and starts that plot from its first column, because the column numbering was the old
        /// plot's.
        /// </summary>
        [Fact]
        public void PassStandingOnARemovedPlot_ResumesAtTheNextPlotThatSurvived()
        {
            var after = Row(0, 3).Concat(Row(5, 5)).ToArray();   // 3 and 4 taken out

            var cursor = AreaEdit.RemapSweep(Row(0, 10), after, plotIndex: 3, columnCursor: 17);

            Assert.Equal(new PlotCoord(5, 0), SweepOrder.RasterOrder(after)[cursor.PlotIndex]);
            Assert.Equal(0, cursor.ColumnCursor);
        }

        /// <summary>
        /// R22. A sweep that had finished its old plot list stays finished over an edit that only
        /// removed ground -- there is nothing left unvisited -- but an edit that ADDS ground pulls
        /// it back to the added plot, because that plot has never been sampled.
        /// </summary>
        [Fact]
        public void FinishedSweep_StaysFinishedOnARemoval_AndReopensForAnAddition()
        {
            var finishedAfterRemoval = AreaEdit.RemapSweep(Row(0, 10), Row(0, 7), plotIndex: 10, columnCursor: 0);
            Assert.Equal(7, finishedAfterRemoval.PlotIndex);   // == plot count: past the end

            var reopened = AreaEdit.RemapSweep(Row(0, 10), Row(0, 12), plotIndex: 10, columnCursor: 0);
            Assert.Equal(10, reopened.PlotIndex);              // the first of the two new plots
            Assert.Equal(0, reopened.ColumnCursor);
        }

        /// <summary>
        /// U9 step 5, KTD6. A claim is keyed to the assignment, not to the geometry, so an edit
        /// leaves it standing over whatever plots the area holds now: the added plots join it with
        /// no ceremony, and exactly the removed plots fall out of it. Those are what a release
        /// message has to name.
        /// </summary>
        [Fact]
        public void AnEditThatOnlyAdds_ReleasesNothingFromTheClaim_AndTheAddedPlotsJoinIt()
        {
            var plan = AreaEdit.Plan(Row(0, 10), Row(0, 12));

            Assert.Empty(plan.ReleasedFromClaim);
            Assert.Equal(new[] { new PlotCoord(10, 0), new PlotCoord(11, 0) }, plan.Added);
        }

        [Fact]
        public void AnEditThatRemovesClaimedPlots_ReleasesExactlyThose_AndLeavesTheClaimOverTheRest()
        {
            var plan = AreaEdit.Plan(Row(0, 10), Row(0, 7));

            Assert.Equal(new[] { new PlotCoord(7, 0), new PlotCoord(8, 0), new PlotCoord(9, 0) }, plan.ReleasedFromClaim);
            Assert.Equal(Row(0, 7), plan.Retained);
        }

        [Fact]
        public void EditPartition_DedupesBothSides_TheWayASurveyAreaDoes()
        {
            var plan = AreaEdit.Plan(
                new[] { new PlotCoord(0, 0), new PlotCoord(0, 0), new PlotCoord(1, 0) },
                new[] { new PlotCoord(1, 0), new PlotCoord(1, 0) });

            Assert.Equal(new[] { new PlotCoord(1, 0) }, plan.Retained);
            Assert.Equal(new[] { new PlotCoord(0, 0) }, plan.Removed);
            Assert.Empty(plan.Added);
        }
    }
}
