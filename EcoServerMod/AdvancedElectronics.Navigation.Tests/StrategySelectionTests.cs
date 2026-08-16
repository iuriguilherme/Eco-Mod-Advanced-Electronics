using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class StrategySelectionTests
    {
        [Fact]
        public void MiningTool_SelectsMiningJobKind()
        {
            Assert.Equal(DroneJobKind.Mining, DroneJobSelection.SelectFor(DroneTool.Mining));
        }

        [Fact]
        public void HarvestTool_SelectsSurveyJobKind()
        {
            // The survey drone's own Tool value is Harvest (an existing, accepted quirk this
            // unit does not change), and the harvest drone shares that value -- both keep
            // running the survey strategy, which is what preserves today's behaviour,
            // accidental as it is for the harvest drone.
            Assert.Equal(DroneJobKind.Survey, DroneJobSelection.SelectFor(DroneTool.Harvest));
        }

        [Fact]
        public void UnrecognisedTool_ReturnsNoStrategy_NeverDefaultsToSurvey()
        {
            var unrecognised = (DroneTool)99;
            Assert.Null(DroneJobSelection.SelectFor(unrecognised));
        }

        [Fact]
        public void SelectionKeysOnDeclaredTool_NotOnAnyComponentConcept()
        {
            // Selection is a pure function of the enum value alone -- no component, no
            // WorldObject, nothing Eco-side is reachable from this call at all.
            Assert.Equal(DroneJobKind.Mining, DroneJobSelection.SelectFor(DroneTool.Mining));
            Assert.Equal(DroneJobKind.Survey, DroneJobSelection.SelectFor(DroneTool.Harvest));
        }
    }
}
