using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// R32's per-crop fitness gate at the decision level, with the engine's own
    /// minimum-habitability boundary pinned on both sides.
    /// </summary>
    public class GroundFitnessTests
    {
        private const string Corn = "corn";
        private const string Rice = "rice";

        /// <summary>A stub fitness source, the shape <see cref="IGroundFitness"/> exists for.</summary>
        private sealed class StubFitness : IGroundFitness
        {
            private readonly Dictionary<(string, int, int, int), GroundFitness> ratings =
                new Dictionary<(string, int, int, int), GroundFitness>();

            private readonly GroundFitness fallback;

            public StubFitness(GroundFitness fallback) => this.fallback = fallback;

            public StubFitness At(string species, int x, int y, int z, GroundFitness fitness)
            {
                this.ratings[(species, x, y, z)] = fitness;
                return this;
            }

            public GroundFitness Rate(string speciesKey, int x, int y, int z) =>
                this.ratings.TryGetValue((speciesKey, x, y, z), out var found) ? found : this.fallback;
        }

        private static FarmBlockFacts TilledAndBare() =>
            FarmBlockFacts.Empty(surfaceAcceptsPlow: true, isTilled: true);

        // --- The boundary KTD5 exists to pin ---

        [Fact]
        public void ARatingBelowTheEnginesMinimum_IsUnfit_EvenThoughItIsNonzero()
        {
            // The failure this test exists to prevent: a plot rated 0.1 is nonzero and
            // reads as "some habitability", but the engine's own growth pass sets anything
            // under 0.4 to zero and kills what grows there. Testing for zero alone would
            // sow a field that dies.
            var fitness = GroundFitness.Rate(0.1f, "ground pollution");

            Assert.False(fitness.IsFit);
        }

        [Fact]
        public void ARatingExactlyAtTheEnginesMinimum_IsFit()
        {
            // The engine's test is `habitability < MinHabitability`, so the constant itself
            // is on the living side. Pinned here so a later refactor cannot quietly flip it.
            Assert.True(GroundFitness.Rate(GroundFitness.MinHabitability, null).IsFit);
            Assert.True(GroundFitness.Rate(1f, null).IsFit);
        }

        [Fact]
        public void ARatingOfZero_IsUnfit()
        {
            Assert.False(GroundFitness.Rate(0f, "temperature").IsFit);
        }

        [Fact]
        public void TheMinimumMatchesTheEnginesOwnConstant()
        {
            // PlantSpecies.MinHabitability = 0.4f. Mirrored rather than referenced,
            // because this project carries no Eco dependency.
            Assert.Equal(0.4f, GroundFitness.MinHabitability);
        }

        // --- The gate over the block decision ---

        [Fact]
        public void UnfitGround_DoesNotSow_AndCarriesTheReason()
        {
            var outcome = FarmPlotDecision.Decide(
                TilledAndBare(), Corn, GroundFitness.Rate(0.1f, "ground pollution"));

            Assert.NotEqual(FarmAction.Sow, outcome.Action);
            Assert.Equal(FarmAction.LeaveAlone, outcome.Action);
            Assert.True(outcome.WasRefusedForFitness);
            Assert.Equal("ground pollution", outcome.UnfitCondition);
        }

        [Fact]
        public void FitGround_Sows()
        {
            var outcome = FarmPlotDecision.Decide(TilledAndBare(), Corn, GroundFitness.Rate(0.9f, null));

            Assert.Equal(FarmAction.Sow, outcome.Action);
            Assert.False(outcome.WasRefusedForFitness);
            Assert.Null(outcome.UnfitCondition);
        }

        [Fact]
        public void FitnessGatesSowingOnly_NotTheOtherActions()
        {
            // R33: the drone never remediates unfit ground, and it also does not stop
            // harvesting or plowing over it. Only the act of putting a seed in is refused.
            var unfit = GroundFitness.Rate(0f, "ground pollution");

            var harvest = FarmPlotDecision.Decide(
                FarmBlockFacts.Planted(true, true, Corn, false, true, true), Corn, unfit);
            var plow = FarmPlotDecision.Decide(
                FarmBlockFacts.Empty(surfaceAcceptsPlow: true, isTilled: false), Corn, unfit);
            var dirt = FarmPlotDecision.Decide(
                FarmBlockFacts.Empty(surfaceAcceptsPlow: false, isTilled: false), Corn, unfit);

            Assert.Equal(FarmAction.Harvest, harvest.Action);
            Assert.Equal(FarmAction.Plow, plow.Action);
            Assert.Equal(FarmAction.PlaceDirt, dirt.Action);
            Assert.All(
                new[] { harvest, plow, dirt },
                o => Assert.False(o.WasRefusedForFitness));
        }

        // --- R34: one bad plot does not spread ---

        [Fact]
        public void AnUnfitPlotInTheMiddle_LeavesItsNeighboursDecisionsUnchanged()
        {
            var probe = new StubFitness(GroundFitness.Rate(0.9f, null))
                .At(Corn, 1, 10, 0, GroundFitness.Rate(0.05f, "ground pollution"));

            var outcomes = Enumerable.Range(0, 3)
                .Select(x => FarmPlotDecision.Decide(TilledAndBare(), Corn, probe.Rate(Corn, x, 10, 0)))
                .ToList();

            Assert.Equal(FarmAction.Sow, outcomes[0].Action);
            Assert.Equal(FarmAction.LeaveAlone, outcomes[1].Action);
            Assert.Equal(FarmAction.Sow, outcomes[2].Action);
        }

        // --- Fitness is a property of the plot AND the crop together ---

        [Fact]
        public void TwoCropsCanDisagreeAboutTheSamePlot()
        {
            // R32. A waterlogged plot suits rice and refuses corn, so a single
            // contamination flag on the plot could not answer the question at all.
            var probe = new StubFitness(GroundFitness.Rate(0f, "moisture"))
                .At(Rice, 5, 10, 5, GroundFitness.Rate(0.95f, null));

            var corn = FarmPlotDecision.Decide(TilledAndBare(), Corn, probe.Rate(Corn, 5, 10, 5));
            var rice = FarmPlotDecision.Decide(TilledAndBare(), Rice, probe.Rate(Rice, 5, 10, 5));

            Assert.Equal(FarmAction.LeaveAlone, corn.Action);
            Assert.Equal(FarmAction.Sow, rice.Action);
        }

        // --- The reason reaches the readout ---

        [Fact]
        public void TheRefusalsConditionSurvivesIntoTheAreaReadout()
        {
            var outcome = FarmPlotDecision.Decide(
                TilledAndBare(), Corn, GroundFitness.Rate(0.2f, "temperature"));

            var text = FarmReadout.FormatStall(
                FarmAreaState.UnfitGround("north field", Corn, outcome.UnfitCondition));

            Assert.Contains("temperature", text);
        }

        [Fact]
        public void AnUnfitRatingWithNoNamedCondition_StillReadsAsUnfit()
        {
            // The probe should always name a condition, but a rating that arrives without
            // one must still refuse the sow rather than fall through to planting.
            var outcome = FarmPlotDecision.Decide(TilledAndBare(), Corn, GroundFitness.Rate(0.1f, null));

            Assert.Equal(FarmAction.LeaveAlone, outcome.Action);
            Assert.True(outcome.WasRefusedForFitness);
        }
    }
}
