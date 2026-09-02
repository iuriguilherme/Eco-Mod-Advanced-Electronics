using System;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// R25 and R27 from the ledger alone. The model is two-state on purpose: a crop
    /// either has a ceiling or it does not, and "no ceiling" is the default every crop
    /// starts at rather than an unconfigured state to report (R26).
    /// </summary>
    public class CropCeilingTests
    {
        private const string Corn = "corn";
        private const string Wheat = "wheat";

        [Fact]
        public void CropWithNoRow_IsHarvestedAtAnyStoredQuantity()
        {
            // AE10's wheat half: no ceiling set means harvest without limit, so a crop
            // the ledger has never heard of is not a stall and not a question.
            var ledger = new CropCeilingLedger();

            Assert.True(ledger.MayHarvest(Wheat, storedQuantity: 0));
            Assert.True(ledger.MayHarvest(Wheat, storedQuantity: 300));
            Assert.True(ledger.MayHarvest(Wheat, storedQuantity: 1_000_000));
        }

        [Fact]
        public void CropAtExactlyItsCeiling_IsLeftStanding()
        {
            // AE10's corn half: 300 stored against a ceiling of 300. "At or above" (R25),
            // so equality stops the harvest rather than allowing one last one.
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);

            Assert.False(ledger.MayHarvest(Corn, storedQuantity: 300));
        }

        [Fact]
        public void CropAboveItsCeiling_IsLeftStanding()
        {
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);

            Assert.False(ledger.MayHarvest(Corn, storedQuantity: 301));
        }

        [Fact]
        public void CropOneBelowItsCeiling_IsHarvested()
        {
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);

            Assert.True(ledger.MayHarvest(Corn, storedQuantity: 299));
        }

        [Fact]
        public void CeilingOfZero_RemovesTheCeiling_RatherThanForbiddingEveryHarvest()
        {
            // R25: setting a ceiling to zero is how a citizen removes it. Reading zero as
            // "harvest nothing" would make the grid's own default value silently stop
            // every crop in the game.
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 0);

            Assert.True(ledger.MayHarvest(Corn, storedQuantity: 0));
            Assert.True(ledger.MayHarvest(Corn, storedQuantity: 5_000));
        }

        [Fact]
        public void SettingZero_ClearsAPreviouslyConfiguredCeiling()
        {
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);
            Assert.False(ledger.MayHarvest(Corn, storedQuantity: 300));

            ledger.Set(Corn, 0);

            Assert.True(ledger.MayHarvest(Corn, storedQuantity: 300));
            Assert.False(ledger.HasCeiling(Corn));
        }

        [Fact]
        public void SettingACeilingTwice_ReplacesRatherThanAccumulates()
        {
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);
            ledger.Set(Corn, 50);

            Assert.Equal(50, ledger.CeilingFor(Corn));
            Assert.False(ledger.MayHarvest(Corn, storedQuantity: 50));
        }

        [Fact]
        public void OneCropsCeiling_DoesNotReachAnother()
        {
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);

            Assert.False(ledger.MayHarvest(Corn, storedQuantity: 300));
            Assert.True(ledger.MayHarvest(Wheat, storedQuantity: 300));
        }

        [Fact]
        public void CeilingFor_ReportsZeroForACropWithNoRow()
        {
            // Zero is the readable form of "no ceiling", which is what the Crop Ceilings
            // grid renders in every row a citizen has not touched (U16).
            var ledger = new CropCeilingLedger();

            Assert.Equal(0, ledger.CeilingFor(Wheat));
            Assert.False(ledger.HasCeiling(Wheat));
        }

        [Fact]
        public void ConfiguredCrops_AreEnumerableForPersistenceAndTheGrid()
        {
            var ledger = new CropCeilingLedger();
            ledger.Set(Corn, 300);
            ledger.Set(Wheat, 0);

            Assert.Equal(new[] { Corn }, ledger.ConfiguredCrops);
        }

        [Fact]
        public void SpeciesKeyComparison_IsOrdinal_NotCaseFolded()
        {
            // The same opaque-key rule the block decision follows: the adapter owns what
            // the key is, and case folding here would merge two engine crops.
            var ledger = new CropCeilingLedger();
            ledger.Set("Corn", 300);

            Assert.True(ledger.MayHarvest("corn", storedQuantity: 300));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void MissingCropKey_IsRejected(string crop)
        {
            var ledger = new CropCeilingLedger();

            Assert.Throws<ArgumentException>(() => ledger.MayHarvest(crop, storedQuantity: 0));
            Assert.Throws<ArgumentException>(() => ledger.Set(crop, 10));
        }

        [Fact]
        public void NegativeCeiling_IsRejected()
        {
            var ledger = new CropCeilingLedger();

            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Set(Corn, -1));
        }

        [Fact]
        public void NegativeStoredQuantity_IsRejected()
        {
            // A count of what linked storage holds is never negative; taking one would
            // read as "far below the ceiling" and harvest into a broken tally.
            var ledger = new CropCeilingLedger();

            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.MayHarvest(Corn, storedQuantity: -1));
        }
    }
}
