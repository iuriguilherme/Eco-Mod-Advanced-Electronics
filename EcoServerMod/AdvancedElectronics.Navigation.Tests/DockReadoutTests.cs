using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Covers the dock panel's text and its view-cursor arithmetic. Every case here was previously
    /// unreachable by the suite: <c>DockReadout</c> documented itself as testable without a running
    /// server while sitting in the mod assembly, which the test project does not reference.
    /// </summary>
    public class DockReadoutTests
    {
        private static SurveyFinding Finding(string ore, int count, int depthMin = 4, int depthMax = 9) =>
            SurveyFinding.Create(1, ore, count, new BlockPos(412, 63, -88), depthMin, depthMax, 0.12f);

        private static AreaSnapshot Area(
            int position = 1,
            string name = "Iron Ridge",
            int plotCount = 24,
            float coverage = 43f,
            SurveyFinding? top = null,
            bool assigned = false) =>
            new AreaSnapshot(position, name, plotCount, coverage, top ?? SurveyFinding.NotFound, assigned);

        // --- Per-material line: shipped behaviour, characterized here for the first time ---

        [Fact]
        public void OreLine_WithFinding_LeadsWithQuantityThenDigTargetThenDepthRange()
        {
            var line = DockReadout.FormatOreLine(Finding("IronOre", 180, 9, 22));

            Assert.Equal("IronOre: ~180 blocks, shallowest at (412, 63, -88), depth 9-22", line);
        }

        [Fact]
        public void OreLine_WhenMinAndMaxDepthAreEqual_ReadsAsASingleDepthNotARange()
        {
            // A range of "depth 7-7" reads like a bug. One observed depth is stated as one depth.
            var line = DockReadout.FormatOreLine(Finding("Coal", 12, 7, 7));

            Assert.Contains("7 blocks deep", line);
            Assert.DoesNotContain("depth 7-7", line);
        }

        [Fact]
        public void OreLine_ForANotFoundFinding_SaysNoDataRatherThanReportingZeroBlocksAtOrigin()
        {
            // The Found flag exists precisely so a zeroed struct is never mistaken for a real
            // finding at (0, 0, 0). The line has to honour that.
            var line = DockReadout.FormatOreLine(SurveyFinding.NotFound);

            Assert.EndsWith(": no data yet", line);
            Assert.DoesNotContain("(0, 0, 0)", line);
        }

        [Fact]
        public void OreLine_WithAnIconItem_PrefixesTheMarkupWithoutDisturbingTheLine()
        {
            var plain = DockReadout.FormatOreLine(Finding("Sandstone", 40, 3, 5));
            var withIcon = DockReadout.FormatOreLine(Finding("Sandstone", 40, 3, 5), "SandstoneItem");

            Assert.StartsWith("<icon name='SandstoneItem'> ", withIcon);
            Assert.EndsWith(plain, withIcon);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void OreLine_WithNoIconItem_EmitsNoMarkup(string iconItemName)
        {
            // A material whose item id does not resolve loses its icon rather than rendering a
            // broken glyph, so the absent case has to stay byte-identical to the plain line.
            var line = DockReadout.FormatOreLine(Finding("Sandstone", 40, 3, 5), iconItemName);

            Assert.DoesNotContain("<icon", line);
            Assert.Equal(DockReadout.FormatOreLine(Finding("Sandstone", 40, 3, 5)), line);
        }

        [Fact]
        public void AtReadableSize_WrapsTheWholeBlockOnce_AndLeavesEmptyTextAlone()
        {
            // Eco's sizes count DOWN from 7, so 2 is one step above standard, not near-minimum.
            Assert.Equal("<size=2>a\nb\n</size>", DockReadout.AtReadableSize("a\nb\n"));
            Assert.Equal(string.Empty, DockReadout.AtReadableSize(string.Empty));
            Assert.Null(DockReadout.AtReadableSize(null));
        }

        // --- Area summary: the three states ---

        [Fact]
        public void AreaSummary_WithAVisibleFinding_NamesCoverageAndTheLargestFind()
        {
            var summary = DockReadout.FormatAreaSummary(Area(coverage: 43f, top: Finding("IronOre", 180)));

            Assert.Equal("43% surveyed, most IronOre (~180 blocks)", summary);
        }

        [Fact]
        public void AreaSummary_SurveyedButNothingVisible_SaysNothingMatchingNotNotSurveyed()
        {
            // Coverage above zero means the drone looked. If the player sees nothing, that is the
            // filter or an empty area -- never "not surveyed yet", which would tell them to wait
            // for work that already happened.
            var summary = DockReadout.FormatAreaSummary(Area(coverage: 67f, top: SurveyFinding.NotFound));

            Assert.Equal("67% surveyed, nothing matching", summary);
        }

        [Fact]
        public void AreaSummary_AtZeroCoverage_SaysNotSurveyedEvenWithNoVisibleFindings()
        {
            var summary = DockReadout.FormatAreaSummary(Area(coverage: 0f, top: SurveyFinding.NotFound));

            Assert.Equal("not surveyed yet", summary);
        }

        [Fact]
        public void AreaSummary_RoundsCoverageToWholePercent()
        {
            var summary = DockReadout.FormatAreaSummary(Area(coverage: 43.6f, top: SurveyFinding.NotFound));

            Assert.StartsWith("44% surveyed", summary);
        }

        // --- Roster line ---

        [Fact]
        public void AreaLine_NamesPositionNamePlotCountAndSummary()
        {
            var line = DockReadout.FormatAreaLine(
                Area(position: 2, name: "Iron Ridge", plotCount: 24, coverage: 43f, top: Finding("IronOre", 180)));

            Assert.Equal("2. Iron Ridge -- 24 plots, 43% surveyed, most IronOre (~180 blocks)", line);
        }

        [Fact]
        public void AreaLine_ForAnUnsurveyedArea_CarriesNoFinding()
        {
            var line = DockReadout.FormatAreaLine(
                Area(position: 3, name: "Limestone Flats", plotCount: 12, coverage: 0f));

            Assert.Equal("3. Limestone Flats -- 12 plots, not surveyed yet", line);
        }

        [Fact]
        public void AreaLine_MarksTheAssignedAreaAndOnlyTheAssignedArea()
        {
            var assigned = DockReadout.FormatAreaLine(Area(position: 2, assigned: true));
            var other = DockReadout.FormatAreaLine(Area(position: 3, assigned: false));

            Assert.EndsWith(DockReadout.AssignedMarker, assigned);
            Assert.DoesNotContain("[assigned]", other);
        }

        // --- Viewing line (R13) ---

        [Fact]
        public void ViewingLine_NamesPositionTotalAndAreaName()
        {
            var line = DockReadout.FormatViewingLine(Area(position: 2, name: "Iron Ridge"), totalAreas: 5);

            Assert.Equal("Viewing: 2 of 5 -- Iron Ridge", line);
        }

        [Fact]
        public void ViewingLine_MarksTheAreaWhenItIsAlsoTheAssignedOne()
        {
            var line = DockReadout.FormatViewingLine(Area(position: 2, assigned: true), totalAreas: 5);

            Assert.EndsWith(DockReadout.AssignedMarker, line);
        }

        // --- Overflow notice (R9) ---

        [Fact]
        public void OverflowNotice_WhenAreasExceedThePool_NamesTheFallbackCommand()
        {
            var notice = DockReadout.FormatOverflowNotice(areaCount: 8, controlPoolSize: 6, fallbackCommand: "/drone assignarea <id>");

            Assert.Contains("6", notice);
            Assert.Contains("/drone assignarea <id>", notice);
        }

        [Theory]
        [InlineData(6)] // exactly at the pool -- every area still has a control
        [InlineData(3)]
        [InlineData(0)]
        public void OverflowNotice_WhenEveryAreaHasAControl_IsEmptySoCallersCanAppendUnconditionally(int areaCount)
        {
            var notice = DockReadout.FormatOverflowNotice(areaCount, controlPoolSize: 6, fallbackCommand: "/drone assignarea <id>");

            Assert.Equal(string.Empty, notice);
        }

        // --- Cursor arithmetic ---

        [Fact]
        public void CycleCursor_ForwardFromTheLastArea_WrapsToTheFirst()
        {
            Assert.Equal(0, DockReadout.CycleCursor(index: 4, direction: +1, count: 5));
        }

        [Fact]
        public void CycleCursor_BackwardFromTheFirstArea_WrapsToTheLast()
        {
            // Without wrapping, reaching the last area from the first costs count-1 clicks on a
            // panel that has no scrollbar and no jump-to control.
            Assert.Equal(4, DockReadout.CycleCursor(index: 0, direction: -1, count: 5));
        }

        [Fact]
        public void CycleCursor_WithNoAreas_StaysAtZeroInsteadOfGoingNegative()
        {
            Assert.Equal(0, DockReadout.CycleCursor(index: 0, direction: -1, count: 0));
            Assert.Equal(0, DockReadout.CycleCursor(index: 0, direction: +1, count: 0));
        }

        [Fact]
        public void ClampCursor_WhenTheListShrankBelowTheCursor_LandsOnTheNewLastArea()
        {
            // Deleting areas on the map is how this happens: the cursor was on 5, three areas
            // remain. Indexing with the stale 4 would throw.
            Assert.Equal(2, DockReadout.ClampCursor(index: 4, count: 3));
        }

        [Fact]
        public void ClampCursor_WithAnEmptyList_ReturnsZeroNotNegativeOne()
        {
            Assert.Equal(0, DockReadout.ClampCursor(index: 3, count: 0));
        }

        [Fact]
        public void ClampCursor_WithAnIndexAlreadyInRange_LeavesItAlone()
        {
            Assert.Equal(2, DockReadout.ClampCursor(index: 2, count: 5));
            Assert.Equal(0, DockReadout.ClampCursor(index: 0, count: 5));
        }

        [Fact]
        public void ClampCursor_WithANegativeIndex_ReturnsZero()
        {
            Assert.Equal(0, DockReadout.ClampCursor(index: -1, count: 5));
        }
    }
}
