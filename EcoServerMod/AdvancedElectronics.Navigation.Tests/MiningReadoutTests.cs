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

        [Fact]
        public void OfferedArea_CarriesBothMarkers_WhenAMinedAreaIsStillAssigned()
        {
            // A real state, not a contradiction: the pass finished and nobody unassigned it.
            var line = MiningReadout.FormatOfferedAreaLine(1, "Survey Dock", "North Ridge", 12,
                isAssigned: true, isMined: true);

            Assert.Contains(DockReadout.AssignedMarker, line);
            Assert.Contains(DockReadout.MinedMarker, line);
            Assert.StartsWith("<color=green>", line);
        }

        [Fact]
        public void OfferedArea_WithNothingSpecial_CarriesNoMarkup()
        {
            var line = MiningReadout.FormatOfferedAreaLine(2, "Survey Dock", "Creek Bend", 8,
                isAssigned: false, isMined: false);

            Assert.Equal("2. Survey Dock -- Creek Bend (8 plots)", line);
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
    }
}
