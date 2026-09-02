using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// R35's phrasing, R36's law/property distinction, R37's contents-free collision
    /// warning, and R38's named engine condition.
    /// </summary>
    public class FarmReadoutTests
    {
        private const string North = "north field";
        private const string Corn = "corn";

        private static IEnumerable<FarmAreaState> OneOfEachStall() => new[]
        {
            FarmAreaState.AwaitingCrop(North),
            FarmAreaState.ShortOfMaterial(North, Corn, "corn seed"),
            FarmAreaState.CeilingReached(North, Corn),
            FarmAreaState.WaitingOnGrowth(North, Corn, nextDueHours: 4),
            FarmAreaState.UnfitGround(North, Corn, "ground pollution"),
            FarmAreaState.RefusedByLaw(North, Corn),
            FarmAreaState.RefusedByProperty(North, Corn),
            FarmAreaState.HeldByOverlap(North, Corn, heldPlotCount: 3),
            FarmAreaState.LevelPassBlocked(North, Corn, "plants are still standing here; clear them first"),
            FarmAreaState.Skipped(North, Corn)
        };

        [Fact]
        public void EveryReasonRendersDistinctly()
        {
            // U4's verification gate: R35's list is only useful if a player can tell one
            // reason from another. Rendered text, not enum names.
            var rendered = OneOfEachStall().Select(FarmReadout.FormatStall).ToList();

            Assert.All(rendered, text => Assert.False(string.IsNullOrWhiteSpace(text)));
            Assert.Equal(rendered.Count, rendered.Distinct().Count());
        }

        [Fact]
        public void AMissingMaterialStallAndALawRefusal_ReadDifferently()
        {
            // AE8's half of R36: a law refusal must not be reported as "short of something".
            var material = FarmReadout.FormatStall(FarmAreaState.ShortOfMaterial(North, Corn, "corn seed"));
            var law = FarmReadout.FormatStall(FarmAreaState.RefusedByLaw(North, Corn));

            Assert.NotEqual(material, law);
            Assert.Contains("corn seed", material);
            Assert.Contains("law", law);
        }

        [Fact]
        public void ALawRefusalAndAPropertyRefusal_ReadDifferently()
        {
            var law = FarmReadout.FormatStall(FarmAreaState.RefusedByLaw(North, Corn));
            var property = FarmReadout.FormatStall(FarmAreaState.RefusedByProperty(North, Corn));

            Assert.NotEqual(law, property);
            Assert.Contains("property", property);
        }

        [Fact]
        public void AnAreaWithNoCrop_ReportsAwaitingACrop_NotAMissingMaterial()
        {
            // AE9. "No crop selected" is a setting the citizen has not made; "short of
            // seed" is a supply problem. Confusing them sends a player to the wrong screen.
            var text = FarmReadout.FormatStall(FarmAreaState.AwaitingCrop(North));

            Assert.Contains("crop", text);
            Assert.DoesNotContain("seed", text);
        }

        [Fact]
        public void UnfitGroundNamesTheEnginesOwnCondition()
        {
            // R38. Not every refusal is contamination -- temperature and moisture refuse
            // plots too, and calling those pollution sends a player to fix the wrong thing.
            var pollution = FarmReadout.FormatStall(FarmAreaState.UnfitGround(North, Corn, "ground pollution"));
            var temperature = FarmReadout.FormatStall(FarmAreaState.UnfitGround(North, Corn, "temperature"));

            Assert.Contains("ground pollution", pollution);
            Assert.Contains("temperature", temperature);
            Assert.NotEqual(pollution, temperature);
        }

        [Fact]
        public void AnOverlapHeldAreaNamesThePlotsAndTheConsequence_NotTheOtherArea()
        {
            // R37. The channel docks exchange geometry over is internal and never a
            // player-facing view, so the warning may say how much ground is held and what
            // that means, and nothing about whose area holds it.
            var text = FarmReadout.FormatStall(FarmAreaState.HeldByOverlap(North, Corn, heldPlotCount: 3));

            Assert.Contains("3", text);
            Assert.Contains("overlap", text);
            Assert.DoesNotContain("owner", text);
            Assert.DoesNotContain("dock ", text);
        }

        [Fact]
        public void AWorkableAreaReportsWhatItWillDoNext_NotAStall()
        {
            var harvest = FarmReadout.FormatNextAction(FarmAreaState.Workable(North, Corn, FarmAction.Harvest));
            var sow = FarmReadout.FormatNextAction(FarmAreaState.Workable(North, Corn, FarmAction.Sow));

            Assert.NotEqual(harvest, sow);
            Assert.Equal(string.Empty, FarmReadout.FormatStall(FarmAreaState.Workable(North, Corn, FarmAction.Sow)));
        }

        [Fact]
        public void EveryFarmActionRendersDistinctly()
        {
            var rendered = new[]
                {
                    FarmAction.PlaceDirt, FarmAction.Plow, FarmAction.Sow,
                    FarmAction.Harvest, FarmAction.LeaveAlone
                }
                .Select(a => FarmReadout.FormatNextAction(FarmAreaState.Workable(North, Corn, a)))
                .ToList();

            Assert.Equal(rendered.Count, rendered.Distinct().Count());
        }

        [Fact]
        public void AnAreaLineCarriesItsNameItsCropAndItsMarkers()
        {
            var line = FarmReadout.FormatAreaLine(
                position: 2,
                area: FarmAreaState.Workable(North, Corn, FarmAction.Harvest),
                isFlat: true);

            Assert.Contains("2.", line);
            Assert.Contains(North, line);
            Assert.Contains(Corn, line);
            Assert.Contains("[farm]", line);
            Assert.Contains("[flat]", line);
        }

        [Fact]
        public void AnAreaThatIsNotFlat_ShowsNoFlatMarker()
        {
            var line = FarmReadout.FormatAreaLine(
                position: 1,
                area: FarmAreaState.Workable(North, Corn, FarmAction.Sow),
                isFlat: false);

            Assert.Contains("[farm]", line);
            Assert.DoesNotContain("[flat]", line);
        }

        [Fact]
        public void AnAreaWithNoCropSaysSoWhereTheCropWouldGo()
        {
            var line = FarmReadout.FormatAreaLine(
                position: 1,
                area: FarmAreaState.AwaitingCrop(North),
                isFlat: false);

            Assert.Contains(North, line);
            Assert.Contains("no crop", line);
        }

        [Fact]
        public void ARefusedLevelPassSpeaksInItsOwnWords_NotAsASupplyProblem()
        {
            // The pass's reason used to be reported as a missing material, which rendered
            // as "blocked -- linked storage has no plants are still standing here".
            var text = FarmReadout.FormatStall(
                FarmAreaState.LevelPassBlocked(North, Corn, "plants are still standing here"));

            Assert.Contains("plants are still standing here", text);
            Assert.DoesNotContain("linked storage", text);
        }

        [Fact]
        public void ASkippedBlockReadsAsWorking_NotAsAStoppedArea()
        {
            // R15: one block passed over is not a reason a citizen acts on, and must not
            // borrow the wording of one that stops a field.
            var text = FarmReadout.FormatStall(FarmAreaState.Skipped(North, Corn));

            Assert.Contains("working", text);
            Assert.DoesNotContain("blocked", text);
            Assert.DoesNotContain("refused", text);
        }

        // --- The job line ---

        [Fact]
        public void EveryJobStatusRendersDistinctly()
        {
            var rendered = new[]
                {
                    FarmReadout.FormatJobStatus(FarmJobStatus.NoAreas, null),
                    FarmReadout.FormatJobStatus(FarmJobStatus.Working, null),
                    FarmReadout.FormatJobStatus(FarmJobStatus.Idle, null),
                    FarmReadout.FormatJobStatus(FarmJobStatus.Blocked, null)
                }
                .ToList();

            Assert.Equal(rendered.Count, rendered.Distinct().Count());
        }

        [Fact]
        public void AnIdleJobWithAWakeSaysWhenItComesBack()
        {
            var withWake = FarmReadout.FormatJobStatus(FarmJobStatus.Idle, wakeAtHours: 6.5);
            var without = FarmReadout.FormatJobStatus(FarmJobStatus.Idle, wakeAtHours: null);

            Assert.NotEqual(withWake, without);
            Assert.Contains("6.5", withWake);
        }
    }
}
