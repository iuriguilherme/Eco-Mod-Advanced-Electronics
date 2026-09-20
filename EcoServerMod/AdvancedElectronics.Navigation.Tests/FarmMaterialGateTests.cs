using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Which block actions the drone may take with the materials it can reach. Found live:
    /// with no seed in storage the drone plowed one block, then stood at the plot playing
    /// its work animation, re-offered the same plot forever because nothing asked whether
    /// the sowing it was preparing for could happen.
    /// </summary>
    public class FarmMaterialGateTests
    {
        [Theory]
        [InlineData(FarmAction.Plow)]
        [InlineData(FarmAction.Relay)]
        [InlineData(FarmAction.PlaceDirt)]
        [InlineData(FarmAction.Sow)]
        public void WithoutSeed_NothingThatPreparesForSowingIsDone(FarmAction action)
        {
            // Tilled ground with nothing sown is ground a wild plant takes over, and the
            // drone would have to plow it again.
            Assert.Equal(FarmMaterial.Seed, FarmMaterialGate.Missing(action, hasSeed: false, hasDirt: true));
        }

        [Fact]
        public void WithoutSeed_ARipeCropIsStillHarvested()
        {
            Assert.Equal(FarmMaterial.None, FarmMaterialGate.Missing(FarmAction.Harvest, hasSeed: false, hasDirt: false));
        }

        [Fact]
        public void PlacingDirtNeedsDirtAsWellAsSeed()
        {
            Assert.Equal(FarmMaterial.Dirt, FarmMaterialGate.Missing(FarmAction.PlaceDirt, hasSeed: true, hasDirt: false));
        }

        [Fact]
        public void RelayingNeedsNoDirtFromStorage_TheDigSuppliesIt()
        {
            Assert.Equal(FarmMaterial.None, FarmMaterialGate.Missing(FarmAction.Relay, hasSeed: true, hasDirt: false));
        }

        [Fact]
        public void WithSeedAndDirt_EveryActionIsAllowed()
        {
            foreach (FarmAction action in System.Enum.GetValues(typeof(FarmAction)))
                Assert.Equal(FarmMaterial.None, FarmMaterialGate.Missing(action, hasSeed: true, hasDirt: true));
        }
    }
}
