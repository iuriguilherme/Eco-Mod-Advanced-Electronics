using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// The Farming tab tells a player, for every area, whether the drone is working, is
    /// waiting on something that resolves by itself, or needs them -- and when it needs
    /// them, what to do. Asked for from live play: every reason used to read alike, so a
    /// player could not tell a crop growing from a chest that needed seed.
    /// </summary>
    public class FarmAttentionTests
    {
        private const string North = "North";
        private const string Corn = "Corn";

        public static IEnumerable<object[]> NeedsYou() => new[]
        {
            new object[] { FarmAreaState.AwaitingCrop(North) },
            new object[] { FarmAreaState.ShortOfMaterial(North, Corn, "Corn seed") },
            new object[] { FarmAreaState.UnfitGround(North, Corn, "ground pollution") },
            new object[] { FarmAreaState.RefusedByLaw(North, Corn) },
            new object[] { FarmAreaState.RefusedByProperty(North, Corn) },
            new object[] { FarmAreaState.LevelPassBlocked(North, Corn, "a tree stands on ground that must be levelled; fell it first") },
            new object[] { FarmAreaState.BlocksRefused(North, Corn, "The roots are too strong!") },
            new object[] { FarmAreaState.PackRejected(North, Corn) },
            new object[] { FarmAreaState.HeldByOverlap(North, Corn, heldPlotCount: 3) },
        };

        public static IEnumerable<object[]> Waiting() => new[]
        {
            new object[] { FarmAreaState.WaitingOnGrowth(North, Corn, nextDueHours: 3.5) },
            new object[] { FarmAreaState.CeilingReached(North, Corn) },
            new object[] { FarmAreaState.Workable(North, Corn, FarmAction.LeaveAlone) },
        };

        public static IEnumerable<object[]> Working() => new[]
        {
            new object[] { FarmAreaState.Workable(North, Corn, FarmAction.Plow) },
            new object[] { FarmAreaState.Workable(North, Corn, FarmAction.Relay) },
            new object[] { FarmAreaState.Workable(North, Corn, FarmAction.PlaceDirt) },
            new object[] { FarmAreaState.Workable(North, Corn, FarmAction.Sow) },
            new object[] { FarmAreaState.Workable(North, Corn, FarmAction.Harvest) },
            new object[] { FarmAreaState.Skipped(North, Corn) },
        };

        [Theory, MemberData(nameof(NeedsYou))]
        public void AReasonOnlyAPlayerCanClear_NeedsThem_InTheAttentionColour(FarmAreaState state)
        {
            Assert.Equal(FarmAttention.NeedsYou, FarmReadout.AttentionFor(state));

            var detail = FarmReadout.FormatDetail(state);
            Assert.StartsWith($"<color={FarmReadout.NeedsYouColor}>", detail);
            Assert.Contains("needs you", detail);
        }

        [Theory, MemberData(nameof(Waiting))]
        public void AReasonThatClearsByItself_SaysSo_WithoutTheAttentionColour(FarmAreaState state)
        {
            Assert.Equal(FarmAttention.Waiting, FarmReadout.AttentionFor(state));

            var detail = FarmReadout.FormatDetail(state);
            Assert.DoesNotContain(FarmReadout.NeedsYouColor, detail);
            Assert.Contains("waiting", detail);
            Assert.Contains("by itself", detail);
        }

        [Theory, MemberData(nameof(Working))]
        public void WorkInHand_ReadsAsWorking(FarmAreaState state)
        {
            Assert.Equal(FarmAttention.Working, FarmReadout.AttentionFor(state));

            var detail = FarmReadout.FormatDetail(state);
            Assert.DoesNotContain(FarmReadout.NeedsYouColor, detail);
            Assert.Contains("working", detail);
        }

        [Fact]
        public void MissingSeed_TellsThePlayerWhereToPutIt()
        {
            var detail = FarmReadout.FormatDetail(FarmAreaState.ShortOfMaterial(North, Corn, "Corn seed"));

            Assert.Contains("Corn seed", detail);
            Assert.Contains("Take From", detail);
        }

        [Fact]
        public void ATreeInTheWay_TellsThePlayerToFellIt()
        {
            var detail = FarmReadout.FormatDetail(
                FarmAreaState.LevelPassBlocked(North, Corn, "a tree stands on ground that must be levelled; fell it first"));

            Assert.Contains("fell it", detail);
        }

        [Fact]
        public void RootsRefusingEveryBlock_PointsAtTheTreesNearby()
        {
            var detail = FarmReadout.FormatDetail(FarmAreaState.BlocksRefused(North, Corn, "The roots are too strong!"));

            Assert.Contains("The roots are too strong!", detail);
            Assert.Contains("tree", detail);
        }

        [Fact]
        public void Plowing_NamesTheStepThatFollows()
        {
            var detail = FarmReadout.FormatDetail(FarmAreaState.Workable(North, Corn, FarmAction.Plow));

            Assert.Contains("plowing", detail);
            Assert.Contains("next", detail);
            Assert.Contains("sowing Corn", detail);
        }

        [Fact]
        public void GrowingCrops_SayWhenTheyAreDue()
        {
            var detail = FarmReadout.FormatDetail(FarmAreaState.WaitingOnGrowth(North, Corn, nextDueHours: 3.5));

            Assert.Contains("3.5h", detail);
        }
    }
}
