using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class MiningReadoutTests
    {
        private static Dictionary<SkipCategory, int> AllZero() => new Dictionary<SkipCategory, int>
        {
            [SkipCategory.Unreachable] = 0,
            [SkipCategory.Property] = 0,
            [SkipCategory.SettlementLaw] = 0,
            [SkipCategory.Obstructed] = 0,
            [SkipCategory.Other] = 0,
        };

        [Theory]
        [InlineData(MiningJobStatus.Idle)]
        [InlineData(MiningJobStatus.Working)]
        [InlineData(MiningJobStatus.WaitingToUnload)]
        [InlineData(MiningJobStatus.Ended)]
        public void EveryJobStatus_RendersDistinctWording(MiningJobStatus status)
        {
            var text = MiningReadout.FormatJobStatus(status, workedCount: 3);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }

        [Fact]
        public void CompleteWithZeroWorked_ReadsFinished_NotUnderWay()
        {
            var text = MiningReadout.FormatJobStatus(MiningJobStatus.Complete, workedCount: 0);
            Assert.Contains("complete", text);
            Assert.DoesNotContain("working", text);
        }

        [Fact]
        public void CompleteWithWorkedPlots_DiffersFromZeroWorked()
        {
            var zero = MiningReadout.FormatJobStatus(MiningJobStatus.Complete, workedCount: 0);
            var some = MiningReadout.FormatJobStatus(MiningJobStatus.Complete, workedCount: 5);
            Assert.NotEqual(zero, some);
        }

        [Fact]
        public void SkipLine_AllZero_RendersDistinctly()
        {
            Assert.Equal("none skipped", MiningReadout.FormatSkipLine(AllZero(), skippedTotal: 0));
        }

        [Fact]
        public void SkipLine_SingleCategory_RendersDistinctly()
        {
            var counts = AllZero();
            counts[SkipCategory.Property] = 3;

            var line = MiningReadout.FormatSkipLine(counts, skippedTotal: 3);

            Assert.Contains("3", line);
            Assert.Contains("property", line);
            Assert.DoesNotContain(",", line);
        }

        [Fact]
        public void SkipLine_MultiCategory_RendersDistinctly_CountsSum()
        {
            var counts = AllZero();
            counts[SkipCategory.Property] = 2;
            counts[SkipCategory.Unreachable] = 1;

            var line = MiningReadout.FormatSkipLine(counts, skippedTotal: 3);

            Assert.Contains(",", line);
            Assert.Contains("2", line);
            Assert.Contains("1", line);
        }

        [Fact]
        public void Headroom_EmptyPartialFull_RenderDistinctly()
        {
            var empty = MiningReadout.FormatHeadroom(headroom: 0, sampleQuantity: 50);
            var partial = MiningReadout.FormatHeadroom(headroom: 20, sampleQuantity: 50);
            var full = MiningReadout.FormatHeadroom(headroom: 60, sampleQuantity: 50);

            Assert.NotEqual(empty, partial);
            Assert.NotEqual(partial, full);
            Assert.NotEqual(empty, full);
        }

        // Driven off the enum rather than a hand-listed set, so adding a reason without
        // wording it fails here instead of silently rendering its bare enum name at a player.
        public static IEnumerable<object[]> AllEndReasons() =>
            Enum.GetValues(typeof(MiningEndReason)).Cast<MiningEndReason>().Select(r => new object[] { r });

        [Theory]
        [MemberData(nameof(AllEndReasons))]
        public void EveryEndReason_RendersWording(MiningEndReason reason)
        {
            var wording = MiningReadout.FormatStopReason(reason);

            Assert.False(string.IsNullOrWhiteSpace(wording));
            Assert.NotEqual(reason.ToString(), wording); // the default branch's fallback
        }

        [Fact]
        public void EveryEndReason_RendersDistinctWording()
        {
            var wordings = Enum.GetValues(typeof(MiningEndReason)).Cast<MiningEndReason>()
                .Select(r => MiningReadout.FormatStopReason(r))
                .ToList();

            Assert.Equal(wordings.Count, wordings.Distinct().Count());
        }

        private static readonly PlotCoord[] TwoPlots = { new PlotCoord(0, 0), new PlotCoord(1, 0) };

        private static AreaSnapshot Area(
            int position = 1,
            string name = "North Ridge",
            int plotCount = 12,
            float coverage = 100f,
            SurveyFinding? top = null,
            AreaLifecycleStatus status = AreaLifecycleStatus.Surveyed,
            bool assigned = false,
            bool unreachable = false,
            bool overlap = false) =>
            new AreaSnapshot(
                position, name, plotCount, coverage, top ?? SurveyFinding.NotFound, status,
                assigned, unreachable, overlap);

        [Fact]
        public void OfferedArea_CarriesTheStatusAndTheAnnotation_WhenAMinedAreaIsStillAssigned()
        {
            // A real state, not a contradiction: the pass finished and nobody unassigned it.
            // R4 moved [mined] from green to yellow, and R9 stripped the annotation's own colour.
            var line = MiningReadout.FormatOfferedAreaLine(
                Area(status: AreaLifecycleStatus.Mined, assigned: true), "Survey Dock");

            Assert.Contains("[mined]", line);
            Assert.Contains("[assigned]", line);
            Assert.StartsWith("<color=yellow>", line);
            Assert.DoesNotContain("<color=yellow>[assigned]", line);
        }

        [Fact]
        public void OfferedArea_WithNothingSpecial_CarriesOnlyTheStatusItsRampGivesIt()
        {
            var line = MiningReadout.FormatOfferedAreaLine(
                Area(position: 2, name: "Creek Bend", plotCount: 8, coverage: 0f,
                     status: AreaLifecycleStatus.Unsurveyed), "Survey Dock");

            Assert.Equal("2. Survey Dock -- Creek Bend -- 8 plots, not surveyed yet   [unsurveyed]", line);
        }

        [Fact]
        public void AE14_TheMiningCopy_IsTheSurveyLineWithTheOwningDockPrefixed()
        {
            // R29: identical fields in an identical order on both tabs. The prefix is the only
            // structural difference, and it exists because the selector commits by position and
            // area names are not unique.
            var area = Area(
                position: 1, name: "Iron Ridge", plotCount: 16, coverage: 100f,
                top: SurveyFinding.Create(1, "IronOre", 180, new BlockPos(412, 63, -88), 9, 22, 0.12f),
                status: AreaLifecycleStatus.Digging, assigned: true);

            var survey = DockReadout.FormatAreaLine(area);
            var mining = MiningReadout.FormatOfferedAreaLine(area, "North Dock");

            Assert.Equal(
                "<color=magenta>1. Iron Ridge -- 16 plots, 100% surveyed, most IronOre (~180 blocks)"
                + "   [digging]   [assigned]</color>",
                survey);
            Assert.Equal(
                "<color=magenta>1. North Dock -- Iron Ridge -- 16 plots, 100% surveyed, most IronOre (~180 blocks)"
                + "   [digging]   [assigned]</color>",
                mining);

            // Same line but for the prefix: nothing else is allowed to drift.
            Assert.Equal(survey, mining.Replace("North Dock -- ", string.Empty));
        }

        [Fact]
        public void TwoAreasSharingAName_AreDistinguishableOnTheMiningTab_ButNotOnTheSurveyTab()
        {
            var first = Area(position: 1, name: "North Ridge");
            var second = Area(position: 1, name: "North Ridge");

            Assert.NotEqual(
                MiningReadout.FormatOfferedAreaLine(first, "North Dock"),
                MiningReadout.FormatOfferedAreaLine(second, "South Dock"));

            // The survey tab lists one dock's own areas, so it has nothing to disambiguate with
            // and deliberately adds nothing.
            Assert.Equal(DockReadout.FormatAreaLine(first), DockReadout.FormatAreaLine(second));
        }

        [Fact]
        public void OfferedArea_CapsItsAnnotationsAtTwo_InTheSameOrderTheSurveyTabUses()
        {
            var line = MiningReadout.FormatOfferedAreaLine(
                Area(status: AreaLifecycleStatus.Digging, assigned: true, unreachable: true, overlap: true),
                "Survey Dock");

            Assert.EndsWith("   [overlap]   [unreachable]</color>", line);
            Assert.DoesNotContain("[assigned]", line);
        }

        [Fact]
        public void MinedOut_NeedsEveryPlotMined_AndNoneReSurveyedSince()
        {
            long Surveyed(PlotCoord p) => 100;

            // Both plots mined after their survey: nothing to do here.
            Assert.True(PlotFreshness.IsMinedOut(TwoPlots, Surveyed, _ => 200));

            // One plot never mined: the area still has work.
            Assert.False(PlotFreshness.IsMinedOut(TwoPlots, Surveyed, p => p.X == 0 ? 200 : 0));

            // Mined, then re-surveyed to open the next tier: a whole pass is waiting.
            Assert.False(PlotFreshness.IsMinedOut(TwoPlots, p => p.X == 0 ? 300 : 100, _ => 200));
        }

        [Fact]
        public void MinedOut_IsFalseForAnAreaNobodyHasTouched()
        {
            // Both stamps 0 means no plot is mineable, which is "nothing to do" -- and reading
            // that as "nothing left" would paint an untouched area green.
            Assert.False(PlotFreshness.IsMinedOut(TwoPlots, _ => 0, _ => 0));
            Assert.False(PlotFreshness.IsMinedOut(System.Array.Empty<PlotCoord>(), _ => 0, _ => 0));
        }

        [Fact]
        public void JobStatus_WithNoAssignment_ReportsWhereTheDroneIs()
        {
            // The reported bug: unassigning left "working" on screen while the drone flew home.
            // Between jobs the job word says nothing; where it is IS the status.
            var flying = MiningReadout.FormatJobStatus(
                MiningJobStatus.Ended, workedCount: 3, hasAssignment: false, travel: "returning to dock");

            Assert.Equal("returning to dock", flying);
            Assert.Equal(
                "docked",
                MiningReadout.FormatJobStatus(MiningJobStatus.Ended, 3, hasAssignment: false, travel: "docked"));
        }

        [Fact]
        public void JobStatus_WithAnAssignment_ComposesBothHalves()
        {
            Assert.Equal(
                "complete -- returning to dock",
                MiningReadout.FormatJobStatus(MiningJobStatus.Complete, 4, hasAssignment: true, travel: "returning to dock"));
        }

        [Fact]
        public void JobStatus_DropsTravelThatRepeatsTheJobWord()
        {
            // "working -- at the area" is noise: being at the area is what working means.
            Assert.Equal(
                "working",
                MiningReadout.FormatJobStatus(MiningJobStatus.Working, 1, hasAssignment: true, travel: "at the area"));

            Assert.Equal(
                "idle -- waiting to set out",
                MiningReadout.FormatJobStatus(MiningJobStatus.Idle, 0, hasAssignment: true, travel: "docked"));
        }

        [Theory]
        [InlineData(DroneStatus.Idle, DroneTravelTarget.None, "docked")]
        [InlineData(DroneStatus.OnStation, DroneTravelTarget.None, "at the area")]
        [InlineData(DroneStatus.Unreachable, DroneTravelTarget.Dock, "cannot reach the area")]
        [InlineData(DroneStatus.EnRoute, DroneTravelTarget.Dock, "returning to dock")]
        [InlineData(DroneStatus.EnRoute, DroneTravelTarget.District, "flying to the area")]
        public void Travel_NamesEachPlaceTheDroneCanBe(DroneStatus status, DroneTravelTarget target, string expected)
        {
            Assert.Equal(expected, DockReadout.FormatTravel(status, target));
        }

        [Fact]
        public void Travel_LetsTheCallerNameWhatBeingAtTheAreaMeans()
        {
            // On a survey dock, arriving and working are the same thing, so "at the area" is a
            // worse word than "surveying". Only that one state differs between the two docks.
            Assert.Equal(
                "surveying",
                DockReadout.FormatTravel(DroneStatus.OnStation, DroneTravelTarget.None, atAreaLabel: "surveying"));

            Assert.Equal(
                "returning to dock",
                DockReadout.FormatTravel(DroneStatus.EnRoute, DroneTravelTarget.Dock, atAreaLabel: "surveying"));
        }

        [Fact]
        public void Travel_NeverRendersARawStateMachineName()
        {
            // "EnRoute" and "OnStation" name states in a state machine. A player watching a drone
            // learns nothing from either.
            foreach (DroneStatus status in Enum.GetValues(typeof(DroneStatus)))
            foreach (DroneTravelTarget target in Enum.GetValues(typeof(DroneTravelTarget)))
            {
                var text = DockReadout.FormatTravel(status, target);
                if (string.IsNullOrEmpty(text)) continue;

                Assert.NotEqual(status.ToString(), text);
                Assert.DoesNotContain("EnRoute", text);
                Assert.DoesNotContain("OnStation", text);
            }
        }

        [Fact]
        public void Progress_ReportsTheAreaTotalAlongsideWhatIsDone()
        {
            // "worked 2, skipped 1" leaves the player computing the denominator from the area list,
            // and a bare "6/15" leaves them guessing what is being counted.
            Assert.Equal(
                "total: 12 plots, worked: 2, skipped: 1, current: 6/15 layers",
                MiningReadout.FormatProgress(totalPlots: 12, worked: 2, skipped: 1, shaftLayersDone: 6, shaftLayersTotal: 15));
        }

        [Fact]
        public void Progress_OmitsTheShaftWhenNoneIsOpen_RatherThanShowingZeroOfZero()
        {
            // Between plots there is no current shaft. "current: 0/0" reads as a stalled one.
            var line = MiningReadout.FormatProgress(totalPlots: 12, worked: 12, skipped: 0, shaftLayersDone: 0, shaftLayersTotal: 0);

            Assert.Equal("total: 12 plots, worked: 12, skipped: 0", line);
            Assert.DoesNotContain("current", line);
        }

        [Fact]
        public void NoEndReason_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatStopReason(null));
        }

        // The case live pass #1 lost three rounds to: halted, no job yet, so nothing to read an
        // end reason from. The old code rendered empty here and the dock sat silent.
        [Fact]
        public void Halted_WithNoJob_SaysSo()
        {
            var blocked = MiningReadout.FormatBlockedReason(haltedServerWide: true, jobEndReason: null);

            Assert.False(string.IsNullOrWhiteSpace(blocked));
            Assert.Contains("halted", blocked);
        }

        [Fact]
        public void Halted_OutranksAFinishedJobsEndReason()
        {
            // A dock halted after a job ended for some other reason must report the halt: the end
            // reason is history, the halt is why nothing will start again.
            var blocked = MiningReadout.FormatBlockedReason(true, MiningEndReason.AreaGone);

            Assert.NotEqual(MiningReadout.FormatStopReason(MiningEndReason.AreaGone), blocked);
            Assert.Contains("halted", blocked);
        }

        [Fact]
        public void NotHalted_FallsBackToTheJobsEndReason()
        {
            Assert.Equal(
                MiningReadout.FormatStopReason(MiningEndReason.AreaGone),
                MiningReadout.FormatBlockedReason(false, MiningEndReason.AreaGone));
        }

        [Fact]
        public void NotHalted_WithNoJob_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatBlockedReason(false, null));
        }

        [Fact]
        public void NoRefusal_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatRefusalDetail(null));
            Assert.Equal(string.Empty, MiningReadout.FormatRefusalDetail("   "));
        }

        [Fact]
        public void Idle_DistinguishesUnassignedFromNotYetSetOut()
        {
            var unassigned = MiningReadout.FormatJobStatus(MiningJobStatus.Idle, 0, hasAssignment: false);
            var assigned = MiningReadout.FormatJobStatus(MiningJobStatus.Idle, 0, hasAssignment: true);

            Assert.NotEqual(unassigned, assigned);
            Assert.Contains("no area assigned", unassigned);
            Assert.DoesNotContain("no area assigned", assigned);
        }

        [Fact]
        public void ShaftProgress_NothingRecordedYet_RendersEmpty()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatShaftProgress(0, 0, 0, 0));
        }

        [Fact]
        public void ShaftProgress_ShowsDepthAndBothStamps()
        {
            var rendered = MiningReadout.FormatShaftProgress(5, 15, 900, 100);

            Assert.Contains("5/15", rendered);
            Assert.Contains("900", rendered);
            Assert.Contains("100", rendered);
        }

        [Theory]
        [InlineData(900, 100, true)]   // surveyed after mined -> work to do
        [InlineData(100, 900, false)]  // mined after surveyed -> already done
        [InlineData(100, 100, false)]  // equal is NOT mineable; IsMineable wants strictly newer
        public void ShaftProgress_CallsTheMineableVerdictTheSameWayIsMineableDoes(long surveyed, long mined, bool mineable)
        {
            // The row exists to expose a disagreement between this verdict and the ground, so the
            // verdict shown must be the same comparison the strategy actually gates on.
            var rendered = MiningReadout.FormatShaftProgress(1, 15, surveyed, mined);

            Assert.Equal(mineable, PlotFreshness.IsMineable(surveyed, mined));
            Assert.Equal(mineable, !rendered.Contains("NOT mineable"));
        }

        [Fact]
        public void ARefusal_KeepsTheEnginesOwnWording()
        {
            // The engine's text is the payload -- it must survive verbatim, since it is the only
            // thing distinguishing one Obstructed skip from another.
            var rendered = MiningReadout.FormatRefusalDetail("Not enough room in inventory.");

            Assert.Contains("Not enough room in inventory.", rendered);
        }

        // ---- U10: the dock-network radius (R14, R23, R24) ----------------------------------
        //
        // What is proven here is the decidable half: which side of the radius a distance falls
        // on, and what the panel says about each side. The world walk itself -- enumerating
        // DroneDockObjects through IWorldObjectManager, and the owner test applied over it -- is
        // Eco-side and has no test double in this suite, so these model an OWNER-MATCHED
        // candidate list as (dock, distance) pairs and apply the real predicate to it. The walk
        // is covered by U10's live verification: two docks placed beyond and within 60 m.

        /// <summary>The radius the mod ships (KTD9). Mirrors DroneDockObject.DockNetworkRadius, which is Eco-side.</summary>
        private const float ShippedRadius = 60f;

        private static readonly (string Dock, float Distance)[] OneNearOneFar =
        {
            ("near dock", 25f),
            ("far dock", 140f),
        };

        private static IReadOnlyList<string> InRangeLines((string Dock, float Distance)[] candidates) =>
            candidates
                .Where(c => MiningReadout.IsWithinDockNetwork(c.Distance, ShippedRadius))
                .Select(c => $"{c.Dock} -- Survey Area 1")
                .ToList();

        private static int OutOfRangeDocks((string Dock, float Distance)[] candidates) =>
            candidates.Count(c => !MiningReadout.IsWithinDockNetwork(c.Distance, ShippedRadius));

        /// <summary>
        /// Covers AE8, R14. Both docks share an owner -- every candidate here has already passed
        /// the owner test -- and the distant one still contributes nothing.
        /// </summary>
        [Fact]
        public void ASurveyDockBeyondTheRadius_ContributesNoOfferableAreas()
        {
            var offered = InRangeLines(OneNearOneFar);

            Assert.DoesNotContain(offered, line => line.StartsWith("far dock"));
            Assert.Single(offered);
        }

        /// <summary>Covers R14. Inside the radius nothing changes -- the area is offered as before.</summary>
        [Fact]
        public void ASurveyDockInsideTheRadius_ContributesItsAreasAsBefore()
        {
            var offered = InRangeLines(OneNearOneFar);

            Assert.Contains(offered, line => line.StartsWith("near dock"));
        }

        /// <summary>
        /// Covers R14. A pair exactly at the radius resolves ONE way -- in range -- and asking
        /// again never changes the answer. The boundary is the case a tolerance band would make
        /// depend on which side the pair last came from, which is what "does not flicker" forbids.
        /// </summary>
        [Fact]
        public void APairExactlyAtTheRadius_ResolvesOneWayAndStaysThere()
        {
            Assert.True(MiningReadout.IsWithinDockNetwork(ShippedRadius, ShippedRadius));

            for (var i = 0; i < 5; i++)
                Assert.True(MiningReadout.IsWithinDockNetwork(ShippedRadius, ShippedRadius));

            // And the two sides of the boundary disagree, so the test above is not vacuous.
            Assert.True(MiningReadout.IsWithinDockNetwork(ShippedRadius - 0.001f, ShippedRadius));
            Assert.False(MiningReadout.IsWithinDockNetwork(ShippedRadius + 0.001f, ShippedRadius));
        }

        /// <summary>
        /// The radius is a parameter, not a baked constant: R14 expects it to become an
        /// upgrade-module effect, so the same distance must answer differently under a wider one.
        /// </summary>
        [Fact]
        public void TheRadiusIsAParameter_SoAWiderOneAdmitsAFartherDock()
        {
            Assert.False(MiningReadout.IsWithinDockNetwork(90f, ShippedRadius));
            Assert.True(MiningReadout.IsWithinDockNetwork(90f, radius: 120f));
        }

        /// <summary>
        /// Covers R23. The whole point of the unit: an out-of-range pair renders the notice, not
        /// an empty roster. "No survey docks with an area were found" would be a false statement
        /// about a dock the player can see.
        /// </summary>
        [Fact]
        public void AnOutOfRangePair_RendersTheOutOfRangeLine_NotAnEmptyRoster()
        {
            var onlyFar = new[] { ("far dock", 140f) };

            var body = MiningReadout.FormatAvailableAreas(
                InRangeLines(onlyFar), OutOfRangeDocks(onlyFar), ShippedRadius);

            Assert.Contains("out of range", body);
            Assert.DoesNotContain("No survey docks with an area were found.", body);
        }

        /// <summary>Covers R23. With one of each, the roster and the notice both survive.</summary>
        [Fact]
        public void AMixedNeighbourhood_KeepsBothTheRosterAndTheNotice()
        {
            var body = MiningReadout.FormatAvailableAreas(
                InRangeLines(OneNearOneFar), OutOfRangeDocks(OneNearOneFar), ShippedRadius);

            Assert.Contains("near dock", body);
            Assert.Contains("out of range", body);
            Assert.DoesNotContain("far dock", body);
        }

        /// <summary>
        /// With nothing out of range, the notice is absent entirely -- a permanent "0 docks out
        /// of range" row would be a fixed sentence, which is what the tab's four rows removed.
        /// </summary>
        [Fact]
        public void NothingOutOfRange_AddsNoNotice()
        {
            var near = new[] { ("near dock", 25f) };

            var body = MiningReadout.FormatAvailableAreas(
                InRangeLines(near), OutOfRangeDocks(near), ShippedRadius);

            Assert.DoesNotContain("out of range", body);
        }

        /// <summary>No docks at all is still the old sentence: nothing is far away, there is nothing.</summary>
        [Fact]
        public void NoDocksAtAll_StillReadsAsNoneFound()
        {
            var body = MiningReadout.FormatAvailableAreas(new string[0], 0, ShippedRadius);

            Assert.Equal("No survey docks with an area were found.", body);
        }

        /// <summary>The notice counts docks, not areas -- one distant dock is one thing to move.</summary>
        [Fact]
        public void TheNotice_CountsDocksAndSaysTheRadius()
        {
            Assert.Contains("1 survey dock is", MiningReadout.FormatOutOfRangeDocks(1, ShippedRadius));
            Assert.Contains("3 survey docks are", MiningReadout.FormatOutOfRangeDocks(3, ShippedRadius));
            Assert.Contains("60 m", MiningReadout.FormatOutOfRangeDocks(1, ShippedRadius));
            Assert.Equal(string.Empty, MiningReadout.FormatOutOfRangeDocks(0, ShippedRadius));
        }

        /// <summary>
        /// Covers R24. An assignment that goes out of range is REPORTED, not cleared: the dock
        /// and area names are still there, so the player can see what they still hold and move a
        /// dock to get it back.
        /// </summary>
        [Fact]
        public void AnAssignmentGoneOutOfRange_IsReported_NotCleared()
        {
            var inRange = MiningReadout.FormatAssignedArea("Drone Dock", "Survey Area 2", withinDockNetwork: true);
            var outOfRange = MiningReadout.FormatAssignedArea("Drone Dock", "Survey Area 2", withinDockNetwork: false);

            Assert.Contains("Drone Dock", outOfRange);
            Assert.Contains("Survey Area 2", outOfRange);
            Assert.Contains("out of range", outOfRange);

            // Not "none" and not "gone" -- both of those are what clearing would look like.
            Assert.NotEqual("none", outOfRange);
            Assert.NotEqual("gone", outOfRange);
            Assert.NotEqual(inRange, outOfRange);
        }

        /// <summary>
        /// Covers R23, R24. The job ends on the vanished-area path, so its end reason says the
        /// area is gone. The blocked row must not repeat that about an area still on the map.
        /// </summary>
        [Fact]
        public void OutOfRange_OutranksTheVanishedAreaWording()
        {
            var blocked = MiningReadout.FormatBlockedReason(
                haltedServerWide: false, jobEndReason: MiningEndReason.AreaGone, assignmentOutOfRange: true);

            Assert.Contains("out of range", blocked);
            Assert.DoesNotContain("is gone", blocked);
        }

        /// <summary>A server-wide halt still outranks everything -- nothing about range is actionable under it.</summary>
        [Fact]
        public void AHalt_StillOutranksOutOfRange()
        {
            var blocked = MiningReadout.FormatBlockedReason(
                haltedServerWide: true, jobEndReason: MiningEndReason.AreaGone, assignmentOutOfRange: true);

            Assert.Contains("halted", blocked);
        }

        /// <summary>The existing two-argument callers are untouched by the new parameter.</summary>
        [Fact]
        public void WithNothingOutOfRange_TheBlockedRowIsUnchanged()
        {
            Assert.Equal(
                MiningReadout.FormatStopReason(MiningEndReason.AreaGone),
                MiningReadout.FormatBlockedReason(haltedServerWide: false, jobEndReason: MiningEndReason.AreaGone));
        }
    }
}
