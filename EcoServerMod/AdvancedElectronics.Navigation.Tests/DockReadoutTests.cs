using System;
using System.Linq;
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
            AreaLifecycleStatus status = AreaLifecycleStatus.Surveyed,
            bool assigned = false,
            bool unreachable = false,
            bool overlap = false) =>
            new AreaSnapshot(
                position, name, plotCount, coverage, top ?? SurveyFinding.NotFound, status,
                assigned, unreachable, overlap);

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
        public void AtReadableSize_UsesAPercentage_BecauseABareNumberIsAbsoluteUnitsNotAMultiplier()
        {
            // The distinction that cost a deploy: <size=2> is two absolute units (microscopic),
            // while a percentage scales. The wiki's 1-7 scale describes signs and chat.
            //
            // Asserts the FORM, not the number -- the exact percentage is a taste setting that
            // moved from 200 to 125 after seeing it in game, and a test that pins it turns tuning
            // into a two-file edit for no safety.
            var sized = DockReadout.AtReadableSize("a\nb\n");

            Assert.Matches(@"^<size=\d+%>a\nb\n</size>$", sized);
            Assert.DoesNotContain("<size=2>", sized);
        }

        [Fact]
        public void AtReadableSize_LeavesEmptyTextAlone()
        {
            Assert.Equal(string.Empty, DockReadout.AtReadableSize(string.Empty));
            Assert.Null(DockReadout.AtReadableSize(null));
        }

        [Fact]
        public void OreLine_IsNeverColoured_SoTheFindingsListStaysDefaultText()
        {
            // Colour in this readout is reserved for the area roster. The findings list is plain
            // text at every coverage level, including a fully-surveyed area.
            var line = DockReadout.FormatOreLine(Finding("IronOre", 180, 9, 22), "IronOreItem");

            Assert.DoesNotContain("<color", line);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(50f)]
        [InlineData(100f)]
        public void AreaSummary_IsNeverColoured_BecauseTheRosterOwnsThatDecision(float coverage)
        {
            // The summary is shared with compact control labels, so it stays plain and
            // FormatAreaLine adds the roster's colour on top.
            var summary = DockReadout.FormatAreaSummary(Area(coverage: coverage, top: Finding("Coal", 4)));

            Assert.DoesNotContain("<color", summary);
        }

        [Fact]
        public void AreaLine_TakesItsColourFromTheLifecycleStatus_NotFromCoverage()
        {
            // R4 moved the colour onto the ramp. Coverage is a survey-progress figure and says
            // nothing about whether the ground has been dug, so a 100%-surveyed area that has
            // since been mined must not still read green.
            var surveyed = DockReadout.FormatAreaLine(
                Area(coverage: 12f, status: AreaLifecycleStatus.Surveyed, top: Finding("Coal", 4)));
            var mined = DockReadout.FormatAreaLine(
                Area(coverage: 100f, status: AreaLifecycleStatus.Mined, top: Finding("Coal", 4)));

            Assert.StartsWith("<color=green>", surveyed);
            Assert.StartsWith("<color=yellow>", mined);
            Assert.DoesNotContain("<color=green>", mined);
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
        public void AreaLine_NamesPositionNamePlotCountSummaryThenStatus()
        {
            // R29's field order, stated once as an exact string so a reordering cannot pass.
            var line = DockReadout.FormatAreaLine(
                Area(position: 2, name: "Iron Ridge", plotCount: 24, coverage: 43f,
                     top: Finding("IronOre", 180), status: AreaLifecycleStatus.Surveyed));

            Assert.Equal(
                "<color=green>2. Iron Ridge -- 24 plots, 43% surveyed, most IronOre (~180 blocks)   [surveyed]</color>",
                line);
        }

        [Fact]
        public void AreaLine_ForAnUnsurveyedArea_CarriesNoFindingAndNoColour()
        {
            var line = DockReadout.FormatAreaLine(
                Area(position: 3, name: "Limestone Flats", plotCount: 12, coverage: 0f,
                     status: AreaLifecycleStatus.Unsurveyed));

            Assert.Equal("3. Limestone Flats -- 12 plots, not surveyed yet   [unsurveyed]", line);
            Assert.DoesNotContain("<color", line);
        }

        [Fact]
        public void AreaLine_MarksTheAssignedAreaAndOnlyTheAssignedArea()
        {
            var assigned = DockReadout.FormatAreaLine(Area(position: 2, assigned: true));
            var other = DockReadout.FormatAreaLine(Area(position: 3, assigned: false));

            Assert.Contains("[assigned]", assigned);
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

            Assert.EndsWith("[assigned]", line);
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
    
        // --- The lifecycle ramp and the uncoloured annotations (U6: R4, R9, R28, R29) ---

        [Fact]
        public void EveryLifecycleStatus_RendersItsOwnWord_SoNoStateDependsOnColourVision()
        {
            // R4: "Every state carries a word as well as a colour." Grey and no-colour differ by
            // lightness rather than hue, so the word is the only reading that always survives.
            var words = Enum.GetValues(typeof(AreaLifecycleStatus))
                .Cast<AreaLifecycleStatus>()
                .Select(DockReadout.StatusWord)
                .ToList();

            Assert.All(words, w => Assert.Matches(@"^\[[a-z]+\]$", w));
            Assert.Equal(words.Count, words.Distinct().Count());

            foreach (var status in Enum.GetValues(typeof(AreaLifecycleStatus)).Cast<AreaLifecycleStatus>())
                Assert.Contains(DockReadout.StatusWord(status), DockReadout.FormatAreaLine(Area(status: status)));
        }

        [Fact]
        public void EveryLifecycleStatus_CarriesItsOwnColour_AndOnlyUnsurveyedHasNone()
        {
            Assert.Null(DockReadout.StatusColor(AreaLifecycleStatus.Unsurveyed));
            Assert.Equal("green", DockReadout.StatusColor(AreaLifecycleStatus.Surveyed));
            Assert.Equal("magenta", DockReadout.StatusColor(AreaLifecycleStatus.Digging));
            Assert.Equal("yellow", DockReadout.StatusColor(AreaLifecycleStatus.Mined));
            Assert.Equal("red", DockReadout.StatusColor(AreaLifecycleStatus.Cleared));

            // R4 asks for a mid grey "dark enough to read as deliberately coloured rather than as
            // the panel's default text" -- so a value, not the absence of one.
            var empty = DockReadout.StatusColor(AreaLifecycleStatus.Empty);
            Assert.NotNull(empty);
            Assert.Contains("<color=", DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Empty)));

            var colours = Enum.GetValues(typeof(AreaLifecycleStatus))
                .Cast<AreaLifecycleStatus>()
                .Select(DockReadout.StatusColor)
                .ToList();
            Assert.Equal(colours.Count, colours.Distinct().Count());
        }

        [Fact]
        public void MinedIsYellowAndSurveyedIsGreen_BecauseTheRampReassignedGreen()
        {
            // The vocabulary this replaces coloured [mined] green. Green now means [surveyed],
            // and a line that still reads green for mined ground is the regression.
            Assert.StartsWith("<color=yellow>", DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Mined)));
            Assert.StartsWith("<color=green>", DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Surveyed)));
        }

        [Fact]
        public void AE6_AMinedAreaStillAssigned_IsYellowWithAnUncolouredAssignedAnnotation()
        {
            var line = DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Mined, assigned: true));

            Assert.StartsWith("<color=yellow>", line);
            Assert.EndsWith("</color>", line);
            Assert.Contains("[mined]", line);
            Assert.Contains("[assigned]", line);

            // R9: the annotation inherits the line's colour rather than carrying one. One colour
            // tag on the line is the whole assertion -- the old marker shipped its own.
            Assert.Equal(1, CountOccurrences(line, "<color="));
            Assert.DoesNotContain("<color=yellow>[assigned]", line);
        }

        [Fact]
        public void AE14_AFullySurveyedPartlyMinedAssignedArea_IsOneMagentaLineInTheFixedFieldOrder()
        {
            var line = DockReadout.FormatAreaLine(Area(
                position: 1, name: "Iron Ridge", plotCount: 16, coverage: 100f,
                top: Finding("IronOre", 180), status: AreaLifecycleStatus.Digging, assigned: true));

            Assert.Equal(
                "<color=magenta>1. Iron Ridge -- 16 plots, 100% surveyed, most IronOre (~180 blocks)"
                + "   [digging]   [assigned]</color>",
                line);

            // R28: the line says THAT the plots differ. How much is the progress row's business.
            Assert.DoesNotContain("worked", line);
        }

        [Fact]
        public void ThreeApplicableAnnotations_RenderTwo_InTheOrderR29Fixes()
        {
            var line = DockReadout.FormatAreaLine(
                Area(status: AreaLifecycleStatus.Digging, assigned: true, unreachable: true, overlap: true));

            Assert.EndsWith("   [overlap]   [unreachable]</color>", line);
            Assert.DoesNotContain("[assigned]", line);
        }

        [Fact]
        public void Annotations_AreOrderedMostBlockingFirst_AndCappedAtTwo()
        {
            Assert.Equal(
                "   [overlap]   [unreachable]",
                DockReadout.FormatAnnotations(AreaAnnotation.Assigned, AreaAnnotation.Unreachable, AreaAnnotation.Overlap));

            Assert.Equal(
                "   [overlap]   [assigned]",
                DockReadout.FormatAnnotations(AreaAnnotation.Assigned, AreaAnnotation.Overlap));
        }

        [Fact]
        public void Flat_YieldsToEveryOtherAnnotation_AndShowsAgainOnceTheRoomIsFree()
        {
            // Lowest display priority of any annotation. It yields for want of space alone: what
            // it says about the ground stays true while a drone works the area, because a citizen
            // can farm that ground by hand at the same time.
            Assert.Equal(
                "   [overlap]   [assigned]",
                DockReadout.FormatAnnotations(AreaAnnotation.Flat, AreaAnnotation.Assigned, AreaAnnotation.Overlap));

            Assert.Equal(
                "   [assigned]   [flat]",
                DockReadout.FormatAnnotations(AreaAnnotation.Flat, AreaAnnotation.Assigned));

            Assert.Equal("   [flat]", DockReadout.FormatAnnotations(AreaAnnotation.Flat));
        }

        [Fact]
        public void Annotations_AreNeverColoured_SoTheyInheritTheLifecycleColour()
        {
            var all = DockReadout.FormatAnnotations(
                AreaAnnotation.Overlap, AreaAnnotation.Unreachable, AreaAnnotation.Assigned,
                AreaAnnotation.Flat);

            Assert.DoesNotContain("<color", all);
        }

        [Fact]
        public void Farm_IsNotAnAnnotation_AndCannotBeAskedForAsOne()
        {
            // [farm] is what an area IS, so it occupies the exclusive status slot where a mining
            // area shows its lifecycle rung. It never competes for an overlay slot, which is why
            // the annotation vocabulary does not contain it at all.
            var names = Enum.GetNames(typeof(AreaAnnotation));

            Assert.DoesNotContain("Farm", names);
            Assert.All(
                Enum.GetValues(typeof(AreaAnnotation)).Cast<AreaAnnotation>(),
                a => Assert.NotEqual("[farm]", DockReadout.AnnotationWord(a)));
        }

        [Fact]
        public void AFarmingArea_ReadsFarmInTheStatusSlot_WhereAMiningAreaReadsItsRung()
        {
            var farm = DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Farm));
            var mining = DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Digging));

            Assert.Contains("[farm]", farm);
            Assert.Contains($"<color={DockReadout.StatusColor(AreaLifecycleStatus.Farm)}>", farm);

            // Same slot, same position on the line: only the word and the colour differ.
            Assert.Equal(
                mining.Replace("[digging]", "[farm]")
                      .Replace("<color=magenta>", $"<color={DockReadout.StatusColor(AreaLifecycleStatus.Farm)}>"),
                farm);
        }

        [Fact]
        public void AnAssignedOverlappingFarmArea_ShowsAllThreeFacts_BecauseFarmTakesNoOverlaySlot()
        {
            // The case that prompted the question. [farm] used to be an annotation and lost the
            // cap to [overlap] and [assigned]; now it is the status, so nothing is dropped.
            var line = DockReadout.FormatAreaLine(
                Area(status: AreaLifecycleStatus.Farm, assigned: true, overlap: true));

            Assert.Contains("[farm]", line);
            Assert.Contains("[overlap]", line);
            Assert.Contains("[assigned]", line);
            Assert.EndsWith("   [farm]   [overlap]   [assigned]</color>", line);
        }

        [Fact]
        public void AFarmLineNeverCarriesAMiningStatusWord_ForAnyKindAndAnyInputs()
        {
            // The invariant, asserted as a property rather than as a list of scenarios, and driven
            // off the status vocabulary so a rung added later is covered without anyone
            // remembering this test exists.
            //
            // It matters most for a case that cannot arise yet: once kind lives on the survey
            // area, an exhausted mining area repurposed as farmland still carries its mined stamps
            // and bedrock observations, so every input the mining ladder reads is still sitting on
            // it and still truthful about its past. The kind is what stops the ladder being asked.
            var vocabulary = Enum.GetValues(typeof(AreaLifecycleStatus))
                .Cast<AreaLifecycleStatus>()
                .ToDictionary(s => s, DockReadout.StatusWord);

            var miningWords = AreaLifecycle.MiningRamp.Select(s => vocabulary[s]).ToList();

            foreach (AreaKind kind in Enum.GetValues(typeof(AreaKind)))
            foreach (var laddersTo in AreaLifecycle.MiningRamp)
            foreach (var assigned in new[] { false, true })
            foreach (var unreachable in new[] { false, true })
            foreach (var overlap in new[] { false, true })
            {
                // The kind chooses which status is derived at all: the ladder is a delegate, and
                // for farmland it is never invoked.
                var status = AreaLifecycle.StatusFor(kind, () => laddersTo);

                var line = DockReadout.FormatAreaLine(Area(
                    status: status, assigned: assigned, unreachable: unreachable, overlap: overlap));

                var present = vocabulary.Values.Where(w => line.Contains(w)).ToList();
                Assert.Single(present);
                Assert.Equal(vocabulary[status], present[0]);

                if (line.Contains("[farm]"))
                    Assert.All(miningWords, w => Assert.DoesNotContain(w, line));
            }
        }

        [Fact]
        public void AnAreaWithNoAnnotations_RendersNoneAndNoTrailingSeparator()
        {
            Assert.Equal(string.Empty, DockReadout.FormatAnnotations());
            Assert.Equal(string.Empty, DockReadout.FormatAnnotations(null));

            var line = DockReadout.FormatAreaLine(Area(status: AreaLifecycleStatus.Surveyed));

            Assert.EndsWith("[surveyed]</color>", line);
            Assert.DoesNotMatch(@"\s+</color>$", line);
        }

        [Fact]
        public void TheReadingDocksMaterialFilter_NarrowsTheSummaryAndNothingElse()
        {
            // R29 leaves exactly one field free to differ between docks, because a filter is a
            // display preference rather than a fact about the area.
            var showing = DockReadout.FormatAreaLine(Area(
                coverage: 67f, top: Finding("IronOre", 180), status: AreaLifecycleStatus.Surveyed, assigned: true));
            var filteredOut = DockReadout.FormatAreaLine(Area(
                coverage: 67f, top: SurveyFinding.NotFound, status: AreaLifecycleStatus.Surveyed, assigned: true));

            Assert.Contains("most IronOre (~180 blocks)", showing);
            Assert.Contains("nothing matching", filteredOut);

            const string head = "<color=green>1. Iron Ridge -- 24 plots, 67% surveyed, ";
            const string tail = "   [surveyed]   [assigned]</color>";
            Assert.StartsWith(head, showing);
            Assert.StartsWith(head, filteredOut);
            Assert.EndsWith(tail, showing);
            Assert.EndsWith(tail, filteredOut);
        }

        [Fact]
        public void AE7_AnAreaAt100Percent_Shows0PercentOnceAResurveyHasClearedIt()
        {
            // The readout carries nothing between calls: it renders the coverage it is handed and
            // no blend of this pass with the last. Clearing the record is U7's; showing the
            // cleared figure honestly is this line's.
            var before = DockReadout.FormatAreaLine(Area(
                coverage: 100f, top: Finding("IronOre", 180), status: AreaLifecycleStatus.Surveyed));

            var during = DockReadout.FormatAreaLine(Area(
                coverage: 0f, top: SurveyFinding.NotFound, status: AreaLifecycleStatus.Unsurveyed));

            var partway = DockReadout.FormatAreaLine(Area(
                coverage: 12f, top: Finding("IronOre", 9), status: AreaLifecycleStatus.Unsurveyed));

            Assert.Contains("100% surveyed", before);
            Assert.DoesNotContain("100", during);
            Assert.DoesNotContain("IronOre", during);
            Assert.Contains("not surveyed yet", during);
            Assert.Contains("12% surveyed", partway);
            Assert.DoesNotContain("100", partway);
        }

        [Fact]
        public void TheSurveyTab_RendersTwoAreasSharingANameIdentically_BecauseItListsOneDocksAreas()
        {
            var first = DockReadout.FormatAreaLine(Area(position: 1, name: "North Ridge"));
            var second = DockReadout.FormatAreaLine(Area(position: 1, name: "North Ridge"));

            Assert.Equal(first, second);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
}
}
