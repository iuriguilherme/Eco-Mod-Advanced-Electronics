using System.Linq;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Covers the projection from lifecycle status onto the four animation booleans the
    /// HRVSTR animator controller consumes.
    ///
    /// Worth testing despite being branch-light, because the alternative way to discover a
    /// wrong mapping is to fly a drone in game and judge an animation by eye -- a slow loop
    /// with no failure message. These assertions state the intended reading of each boolean
    /// in a form that breaks loudly instead.
    /// </summary>
    public class DroneAnimationStateTests
    {
        private static DroneAnimationState State(DroneStatus status, bool isMoving,
                                                 bool isAtHomeDock, bool usesHarvestTool = false) =>
            DroneAnimationState.From(status, isMoving, isAtHomeDock, usesHarvestTool);

        // --- IsAtHomeDock: physically home and settled ---

        [Fact]
        public void AtTheDockAndStationary_IsAtHomeDock()
        {
            var state = State(DroneStatus.Idle, isMoving: false, isAtHomeDock: true);

            Assert.True(state.IsAtHomeDock);
            Assert.False(state.IsWorking);
        }

        [Fact]
        public void DispatchedAndAwayFromTheDock_IsNotAtHomeDock()
        {
            var state = State(DroneStatus.EnRoute, isMoving: true, isAtHomeDock: false);

            Assert.False(state.IsAtHomeDock);
        }

        [Fact]
        public void WithinDockingRangeButStillMoving_IsNotYetAtHomeDock()
        {
            // The final approach: the status machine flips to Idle on arrival while the
            // mover is still settling. Playing the fully-stopped animation over a drone
            // that is visibly drifting is the artifact this guards against.
            var state = State(DroneStatus.Idle, isMoving: true, isAtHomeDock: true);

            Assert.False(state.IsAtHomeDock);
        }

        // The reason KD3 reads position rather than assignment. An assignment outlives the
        // survey that completes it, so a drone that finished its area and flew home is still
        // assigned; so is one recalled for lack of fuel. A flag meaning "not dispatched"
        // would never go true again for either, and the drone would sit in its dock running
        // the flying loop.
        //
        // What pins that here is the SIGNATURE, not a scenario: assignment is not a
        // parameter of From, so no combination of inputs can make it matter. Separate
        // completed-survey and fuel-recall test cases would be three copies of the case
        // above wearing different names -- coverage theatre, since the dimension that
        // distinguished them is gone. The real geometric predicate lives in
        // DroneLifecycle.IsAtHomeDock(), outside this project's reach.
        [Fact]
        public void IsAtHomeDock_DependsOnlyOnPositionAndMotion_NotOnLifecycleStatus()
        {
            foreach (var status in new[] { DroneStatus.Idle, DroneStatus.EnRoute, DroneStatus.Unreachable })
            {
                Assert.True(State(status, isMoving: false, isAtHomeDock: true).IsAtHomeDock);
                Assert.False(State(status, isMoving: false, isAtHomeDock: false).IsAtHomeDock);
            }
        }

        // --- IsWorking: on station, across the whole pass ---

        [Fact]
        public void OnStationAndStationary_IsWorking()
        {
            var state = State(DroneStatus.Surveying, isMoving: false, isAtHomeDock: false);

            Assert.True(state.IsWorking);
            Assert.False(state.IsAtHomeDock);
        }

        // A hop to the next plot is travel, not work. An earlier version held IsWorking true
        // across hops to avoid a stutter, and the result was the work loop playing over a
        // drone sliding sideways with its arm swinging at nothing.
        [Fact]
        public void OnStationButRepositioningBetweenPlots_IsNotWorking()
        {
            var state = State(DroneStatus.Surveying, isMoving: true, isAtHomeDock: false);

            Assert.False(state.IsWorking);
        }

        // One case, not two. Outbound and homebound legs are indistinguishable here by
        // construction -- DroneTravelTarget stopped being an input -- so a second test naming
        // the return leg would assert nothing the first does not.
        [Fact]
        public void EnRouteInEitherDirection_IsNeitherWorkingNorHome()
        {
            var state = State(DroneStatus.EnRoute, isMoving: true, isAtHomeDock: false);

            Assert.False(state.IsWorking);
            Assert.False(state.IsAtHomeDock);
        }

        // Neither flag is true when the drone is stranded, so it plays the flying loop and
        // hovers. Nobody specified that; it falls out of the projection and is pinned here
        // so a change to it is a deliberate one.
        [Fact]
        public void Unreachable_IsNeitherWorkingNorHome()
        {
            var state = State(DroneStatus.Unreachable, isMoving: false, isAtHomeDock: false);

            Assert.False(state.IsWorking);
            Assert.False(state.IsAtHomeDock);
        }

        // --- ModeMining / ModeHarvest: decided by the tool, and nothing else ---

        [Fact]
        public void MiningArm_SelectsMiningModeOnly()
        {
            var state = State(DroneStatus.Surveying, isMoving: false, isAtHomeDock: false,
                              usesHarvestTool: false);

            Assert.True(state.ModeMining);
            Assert.False(state.ModeHarvest);
        }

        [Fact]
        public void HarvestArm_SelectsHarvestModeOnly()
        {
            var state = State(DroneStatus.Surveying, isMoving: false, isAtHomeDock: false,
                              usesHarvestTool: true);

            Assert.True(state.ModeHarvest);
            Assert.False(state.ModeMining);
        }

        // The controller's mode-select has no third branch and no neither-branch: both true
        // or both false is a state the art cannot render.
        [Theory]
        [InlineData(DroneStatus.Idle, false)]
        [InlineData(DroneStatus.Idle, true)]
        [InlineData(DroneStatus.EnRoute, false)]
        [InlineData(DroneStatus.EnRoute, true)]
        [InlineData(DroneStatus.Surveying, false)]
        [InlineData(DroneStatus.Surveying, true)]
        [InlineData(DroneStatus.Unreachable, false)]
        [InlineData(DroneStatus.Unreachable, true)]
        public void ModeBooleans_AreNeverEqual_WhateverTheStatus(DroneStatus status, bool usesHarvestTool)
        {
            var state = State(status, isMoving: false, isAtHomeDock: false, usesHarvestTool);

            Assert.NotEqual(state.ModeMining, state.ModeHarvest);
        }

        // The tool is a fact about the drone, not about what it is doing. A drone that
        // switched arms on arrival would be a behaviour nobody asked for.
        [Theory]
        [InlineData(DroneStatus.Idle)]
        [InlineData(DroneStatus.EnRoute)]
        [InlineData(DroneStatus.Surveying)]
        [InlineData(DroneStatus.Unreachable)]
        public void ModeBooleans_DoNotVaryWithLifecycleStatus(DroneStatus status)
        {
            var mining = State(status, isMoving: false, isAtHomeDock: false, usesHarvestTool: false);
            var harvest = State(status, isMoving: true, isAtHomeDock: false, usesHarvestTool: true);

            Assert.True(mining.ModeMining);
            Assert.True(harvest.ModeHarvest);
        }

        // --- IsAtHomeDock and IsWorking are mutually exclusive ---

        // The caller's "at the dock" test is a radius sized to clear the pad's diagonal, so
        // it also contains a survey plot next to the dock. Without this rule such a drone
        // reports home and working at once: blades stop, take-off replays, once per plot hop.
        [Fact]
        public void SurveyingInsideTheDocksOwnRadius_IsWorkingNotHome()
        {
            var state = State(DroneStatus.Surveying, isMoving: false, isAtHomeDock: true);

            Assert.True(state.IsWorking);
            Assert.False(state.IsAtHomeDock);
        }

        [Theory]
        [InlineData(DroneStatus.Idle, false, true)]
        [InlineData(DroneStatus.Idle, true, true)]
        [InlineData(DroneStatus.EnRoute, true, false)]
        [InlineData(DroneStatus.Surveying, false, true)]
        [InlineData(DroneStatus.Surveying, true, true)]
        [InlineData(DroneStatus.Surveying, false, false)]
        [InlineData(DroneStatus.Unreachable, false, true)]
        public void IsAtHomeDockAndIsWorking_AreNeverBothTrue(
            DroneStatus status, bool isMoving, bool isAtHomeDock)
        {
            var state = State(status, isMoving, isAtHomeDock);

            Assert.False(state.IsAtHomeDock && state.IsWorking);
        }

        // --- The name/value pairing the pusher iterates ---

        [Fact]
        public void AsNamedValues_CarriesExactlyTheFourControllerParameters()
        {
            var state = State(DroneStatus.Idle, isMoving: false, isAtHomeDock: true);

            var names = state.AsNamedValues().Select(pair => pair.Name).ToArray();

            Assert.Equal(4, names.Length);
            Assert.Equal(names.Length, names.Distinct().Count());
            Assert.Contains(DroneAnimationStateNames.IsAtHomeDock, names);
            Assert.Contains(DroneAnimationStateNames.IsWorking, names);
            Assert.Contains(DroneAnimationStateNames.ModeMining, names);
            Assert.Contains(DroneAnimationStateNames.ModeHarvest, names);
        }

        // The names are the contract, not the C# member names -- the controller reads
        // strings. A constant renamed without its value changing would pass every other
        // test here and still animate nothing.
        [Fact]
        public void StateNames_AreTheExactStringsTheControllerDeclares()
        {
            Assert.Equal("IsAtHomeDock", DroneAnimationStateNames.IsAtHomeDock);
            Assert.Equal("IsWorking", DroneAnimationStateNames.IsWorking);
            Assert.Equal("ModeMining", DroneAnimationStateNames.ModeMining);
            Assert.Equal("ModeHarvest", DroneAnimationStateNames.ModeHarvest);
        }

        // The regression that cost a night. WorldObject registers Enabled, Operating and
        // Using itself, and RegisterCustomEvents adds our States to the same dictionary --
        // so repeating one throws while the client builds the archetype, the object is never
        // instanced, and it simply does not appear. No model, no placement ghost, and
        // nothing in the server log, because the throw is entirely client-side.
        //
        // "Operating" was the animator's own parameter name, which is exactly why it got
        // picked. This test is the only thing standing between the next obvious name and
        // another silent night.
        [Fact]
        public void NoStateName_CollidesWithAWorldObjectBuiltIn()
        {
            var state = State(DroneStatus.Idle, isMoving: false, isAtHomeDock: true);

            foreach (var (name, _) in state.AsNamedValues())
                Assert.DoesNotContain(name, DroneAnimationStateNames.Reserved);
        }

        [Fact]
        public void AsNamedValues_ReportsTheSameValuesAsTheProperties()
        {
            var state = State(DroneStatus.Surveying, isMoving: false, isAtHomeDock: false,
                              usesHarvestTool: true);

            var byName = state.AsNamedValues().ToDictionary(pair => pair.Name, pair => pair.Value);

            Assert.Equal(state.IsAtHomeDock, byName[DroneAnimationStateNames.IsAtHomeDock]);
            Assert.Equal(state.IsWorking,    byName[DroneAnimationStateNames.IsWorking]);
            Assert.Equal(state.ModeMining,   byName[DroneAnimationStateNames.ModeMining]);
            Assert.Equal(state.ModeHarvest,  byName[DroneAnimationStateNames.ModeHarvest]);
        }
    }
}
