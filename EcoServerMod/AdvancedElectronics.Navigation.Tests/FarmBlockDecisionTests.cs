using System;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Every clause of R14, named by its input and its returned action (U1's verification
    /// gate). The block decision is pure, so the whole rule set is provable here without a
    /// server, and nothing in this file references an Eco type.
    /// </summary>
    public class FarmBlockDecisionTests
    {
        private const string Corn = "corn";
        private const string Wheat = "wheat";

        /// <summary>Ordinary farmland: plow-accepted, tilled, nothing standing on it.</summary>
        private static FarmBlockFacts BareTilled() => FarmBlockFacts.Empty(
            surfaceAcceptsPlow: true,
            isTilled: true);

        private static FarmBlockFacts Planted(
            string species,
            bool fullyGrown,
            bool dead = false,
            bool cropUnderCeiling = true) => FarmBlockFacts.Planted(
                surfaceAcceptsPlow: true,
                isTilled: true,
                plantSpecies: species,
                plantIsDead: dead,
                plantIsFullyGrown: fullyGrown,
                cropIsUnderCeiling: cropUnderCeiling);

        // --- Desert sand: plowable only once dug up and laid back as dirt ---

        [Fact]
        public void DesertSandWithNothingOnIt_IsRelaidNotPlowed()
        {
            // Plowing desert sand fails without a message; dug up and put back, it is
            // ordinary dirt until the biome turns it to sand again.
            var facts = FarmBlockFacts.Empty(surfaceAcceptsPlow: true, isTilled: false, surfaceMustBeRelaid: true);

            Assert.Equal(FarmAction.Relay, FarmBlockDecision.Decide(facts, Corn));
        }

        [Fact]
        public void DesertSandUnderAWildPlant_IsRelaid()
        {
            var facts = FarmBlockFacts.Planted(
                surfaceAcceptsPlow: true, isTilled: false, plantSpecies: Wheat,
                plantIsDead: false, plantIsFullyGrown: false, cropIsUnderCeiling: true,
                surfaceMustBeRelaid: true);

            Assert.Equal(FarmAction.Relay, FarmBlockDecision.Decide(facts, Corn));
        }

        [Fact]
        public void OrdinaryUntilledGround_IsStillPlowed()
        {
            var facts = FarmBlockFacts.Empty(surfaceAcceptsPlow: true, isTilled: false);

            Assert.Equal(FarmAction.Plow, FarmBlockDecision.Decide(facts, Corn));
        }

        // --- AE1: the area's own crop, still growing, is left alone ---

        [Fact]
        public void AreaCropThreeQuartersGrown_IsLeftAlone()
        {
            // AE1. Growth is a two-state fact here -- "three-quarters grown" is any
            // maturity the engine does not call fully grown.
            var action = FarmBlockDecision.Decide(Planted(Corn, fullyGrown: false), areaCrop: Corn);

            Assert.Equal(FarmAction.LeaveAlone, action);
        }

        // --- AE2: a foreign species is plowed under at every maturity ---

        [Fact]
        public void FullyGrownForeignSpecies_IsPlowedUnder_NotHarvested()
        {
            // AE2. The wheat is ripe and worth harvesting, and the rule still plows it:
            // the area grows corn, so the wheat is in the way rather than a crop.
            var action = FarmBlockDecision.Decide(Planted(Wheat, fullyGrown: true), areaCrop: Corn);

            Assert.Equal(FarmAction.Plow, action);
        }

        [Fact]
        public void ImmatureForeignSpecies_IsPlowedUnder()
        {
            var action = FarmBlockDecision.Decide(Planted(Wheat, fullyGrown: false), areaCrop: Corn);

            Assert.Equal(FarmAction.Plow, action);
        }

        // --- AE3: the ceiling decides whether a ripe crop is taken ---

        [Fact]
        public void RipeAreaCrop_UnderCeiling_IsHarvested()
        {
            var action = FarmBlockDecision.Decide(
                Planted(Corn, fullyGrown: true, cropUnderCeiling: true),
                areaCrop: Corn);

            Assert.Equal(FarmAction.Harvest, action);
        }

        [Fact]
        public void RipeAreaCrop_AtCeiling_IsLeftStanding()
        {
            // AE3. Left standing rather than harvested-and-discarded: the plant keeps
            // indefinitely in the ground, so this is the safe place to store it.
            var action = FarmBlockDecision.Decide(
                Planted(Corn, fullyGrown: true, cropUnderCeiling: false),
                areaCrop: Corn);

            Assert.Equal(FarmAction.LeaveAlone, action);
        }

        // --- Precedence: the surface test beats every plant rule ---

        [Fact]
        public void SurfaceThePlowRefuses_GetsDirt_EvenWithAPlantOnIt()
        {
            // The first R14 clause outranks the plant rules. A ripe crop of the area's own
            // species standing on refused ground still yields place-dirt, not harvest.
            var facts = FarmBlockFacts.Planted(
                surfaceAcceptsPlow: false,
                isTilled: false,
                plantSpecies: Corn,
                plantIsDead: false,
                plantIsFullyGrown: true,
                cropIsUnderCeiling: true);

            var action = FarmBlockDecision.Decide(facts, areaCrop: Corn);

            Assert.Equal(FarmAction.PlaceDirt, action);
        }

        [Fact]
        public void SurfaceThePlowRefuses_GetsDirt_WhenBare()
        {
            var facts = FarmBlockFacts.Empty(surfaceAcceptsPlow: false, isTilled: false);

            Assert.Equal(FarmAction.PlaceDirt, FarmBlockDecision.Decide(facts, areaCrop: Corn));
        }

        // --- The bare-ground clauses ---

        [Fact]
        public void PlowableUntilledSurface_IsPlowed()
        {
            var facts = FarmBlockFacts.Empty(surfaceAcceptsPlow: true, isTilled: false);

            Assert.Equal(FarmAction.Plow, FarmBlockDecision.Decide(facts, areaCrop: Corn));
        }

        [Fact]
        public void AlreadyTilledGround_IsNotPlowedAgain()
        {
            // The regression this guards: a bare Is<Tillable>() test still answers true on
            // tilled dirt, so a single-predicate rule would re-plow a field forever (KTD3).
            var action = FarmBlockDecision.Decide(BareTilled(), areaCrop: Corn);

            Assert.NotEqual(FarmAction.Plow, action);
        }

        [Fact]
        public void TilledGroundWithNothingGrowing_IsSown()
        {
            Assert.Equal(FarmAction.Sow, FarmBlockDecision.Decide(BareTilled(), areaCrop: Corn));
        }

        // --- The dead-plant clause ---

        [Fact]
        public void DeadAreaCrop_BelowFullGrowth_IsPlowed_NotLeftAlone()
        {
            // Without the dead clause this reads as "still growing" and the plot is held
            // forever: a dead plant never ripens and no other rule would ever clear it.
            var action = FarmBlockDecision.Decide(
                Planted(Corn, fullyGrown: false, dead: true),
                areaCrop: Corn);

            Assert.Equal(FarmAction.Plow, action);
        }

        [Fact]
        public void DeadAreaCrop_FullyGrown_IsPlowed_NotHarvested()
        {
            var action = FarmBlockDecision.Decide(
                Planted(Corn, fullyGrown: true, dead: true, cropUnderCeiling: true),
                areaCrop: Corn);

            Assert.Equal(FarmAction.Plow, action);
        }

        [Fact]
        public void DeadForeignSpecies_IsPlowed_ByEitherRule()
        {
            var action = FarmBlockDecision.Decide(
                Planted(Wheat, fullyGrown: false, dead: true),
                areaCrop: Corn);

            Assert.Equal(FarmAction.Plow, action);
        }

        // --- Species identity is an opaque key compared for equality ---

        [Fact]
        public void SpeciesKeyComparison_IsOrdinal_NotCaseFolded()
        {
            // The adapter owns what the key is; the core only compares it. Case folding
            // here would silently merge two engine species that differ only in case.
            var action = FarmBlockDecision.Decide(Planted("Corn", fullyGrown: true), areaCrop: "corn");

            Assert.Equal(FarmAction.Plow, action);
        }

        // --- R26 is enforced above this seam, and the guard says so ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NoAreaCrop_IsRejected_RatherThanDecided(string areaCrop)
        {
            // R26 leaves a crop-less area entirely untouched, so the decision is never
            // reached for one. An explicit throw keeps that invariant visible instead of
            // letting a null key quietly compare unequal to every species and plow the area.
            Assert.Throws<ArgumentException>(() => FarmBlockDecision.Decide(BareTilled(), areaCrop));
        }
    }
}
