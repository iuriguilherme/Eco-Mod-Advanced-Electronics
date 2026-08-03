using System.Linq;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Covers the projection from lifecycle status onto the seven animation booleans the
    /// HRVSTR animator controller consumes.
    ///
    /// Worth testing despite being branch-light, because the alternative way to discover a
    /// wrong mapping is to fly a drone in game and judge an animation by eye -- a slow loop
    /// with no failure message. These assertions state the intended reading of each boolean
    /// in a form that breaks loudly instead.
    /// </summary>
    public class DroneAnimationStateTests
    {
        // --- Docked: parked at the dock with nothing to do ---

        [Fact]
        public void IdleAndStationary_IsDocked()
        {
            var state = DroneAnimationState.From(DroneStatus.Idle, DroneTravelTarget.None,
                                                 isMoving: false, hasAssignment: false);

            Assert.True(state.Docked);
            Assert.False(state.Flying);
            Assert.False(state.Working);
        }

        [Fact]
        public void IdleButStillMoving_IsNotDocked()
        {
            // The final approach home: the state machine flips to Idle on arrival, but the
            // mover can still be settling. Playing the docked animation while visibly drifting
            // is the artifact this guards against.
            var state = DroneAnimationState.From(DroneStatus.Idle, DroneTravelTarget.None,
                                                 isMoving: true, hasAssignment: false);

            Assert.False(state.Docked);
            Assert.True(state.Flying);
        }

        // --- Assigned / Unassigned: always exact negations ---

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AssignedAndUnassigned_AreAlwaysOpposites(bool hasAssignment)
        {
            var state = DroneAnimationState.From(DroneStatus.Idle, DroneTravelTarget.None,
                                                 isMoving: false, hasAssignment: hasAssignment);

            Assert.Equal(hasAssignment, state.Assigned);
            Assert.NotEqual(state.Assigned, state.Unassigned);
        }

        // --- Flying tracks the mover, not the status ---

        [Fact]
        public void EnRouteToArea_IsFlyingButNotWorking()
        {
            // Working means "on station doing the job". The outbound leg is flying, even
            // though DroneLifecycle.IsWorking counts it -- that property answers a different
            // question (what to charge fuel for).
            var state = DroneAnimationState.From(DroneStatus.EnRoute, DroneTravelTarget.District,
                                                 isMoving: true, hasAssignment: true);

            Assert.True(state.Flying);
            Assert.False(state.Working);
            Assert.False(state.Mining);
        }

        [Fact]
        public void EnRouteToDock_IsFlyingButNotWorking()
        {
            var state = DroneAnimationState.From(DroneStatus.EnRoute, DroneTravelTarget.Dock,
                                                 isMoving: true, hasAssignment: false);

            Assert.True(state.Flying);
            Assert.False(state.Working);
        }

        // --- Working / Mining: the park-and-sweep pass ---

        [Fact]
        public void SurveyingAndParked_IsWorkingAndMining()
        {
            var state = DroneAnimationState.From(DroneStatus.Surveying, DroneTravelTarget.None,
                                                 isMoving: false, hasAssignment: true);

            Assert.True(state.Working);
            Assert.True(state.Mining);
            Assert.False(state.Flying);
        }

        [Fact]
        public void SurveyingButHoppingToNextPlot_StaysWorkingAndStopsMining()
        {
            // Park-and-sweep alternates: read the columns under this plot, hop to the next.
            // Working stays true across the whole pass so the drone keeps its deployed posture;
            // only the sampling animation stops during the hop.
            var state = DroneAnimationState.From(DroneStatus.Surveying, DroneTravelTarget.None,
                                                 isMoving: true, hasAssignment: true);

            Assert.True(state.Working);
            Assert.False(state.Mining);
            Assert.True(state.Flying);
        }

        // --- Unreachable is not a work state ---

        [Fact]
        public void Unreachable_IsNeitherWorkingNorMining()
        {
            var state = DroneAnimationState.From(DroneStatus.Unreachable, DroneTravelTarget.District,
                                                 isMoving: false, hasAssignment: true);

            Assert.False(state.Working);
            Assert.False(state.Mining);
            Assert.False(state.Docked); // Unreachable is stranded somewhere, not home.
        }

        // --- Harvesting is reserved until harvest behaviour exists ---

        [Theory]
        [InlineData(DroneStatus.Idle)]
        [InlineData(DroneStatus.EnRoute)]
        [InlineData(DroneStatus.Surveying)]
        [InlineData(DroneStatus.Unreachable)]
        public void Harvesting_IsAlwaysFalse_UntilHarvestBehaviourExists(DroneStatus status)
        {
            // Deliberately pinned. The drone surveys today; nothing on the server can
            // truthfully drive a harvest animation. When harvest behaviour lands, this test
            // should fail and be rewritten -- that failure is the reminder.
            var state = DroneAnimationState.From(status, DroneTravelTarget.District,
                                                 isMoving: false, hasAssignment: true);

            Assert.False(state.Harvesting);
        }

        // --- The name/value pairing the pusher iterates ---

        [Fact]
        public void AsNamedValues_CoversEveryStateNameExactlyOnce()
        {
            var state = DroneAnimationState.From(DroneStatus.Idle, DroneTravelTarget.None,
                                                 isMoving: false, hasAssignment: false);

            var names = state.AsNamedValues().Select(pair => pair.Name).ToArray();

            Assert.Equal(names.Length, names.Distinct().Count());
            Assert.Contains(DroneAnimationStateNames.Docked, names);
            Assert.Contains(DroneAnimationStateNames.Unassigned, names);
            Assert.Contains(DroneAnimationStateNames.Assigned, names);
            Assert.Contains(DroneAnimationStateNames.Flying, names);
            Assert.Contains(DroneAnimationStateNames.Working, names);
            Assert.Contains(DroneAnimationStateNames.Mining, names);
            Assert.Contains(DroneAnimationStateNames.Harvesting, names);
        }

        [Fact]
        public void AsNamedValues_ReportsTheSameValuesAsTheProperties()
        {
            var state = DroneAnimationState.From(DroneStatus.Surveying, DroneTravelTarget.None,
                                                 isMoving: false, hasAssignment: true);

            var byName = state.AsNamedValues().ToDictionary(pair => pair.Name, pair => pair.Value);

            Assert.Equal(state.Docked,     byName[DroneAnimationStateNames.Docked]);
            Assert.Equal(state.Unassigned, byName[DroneAnimationStateNames.Unassigned]);
            Assert.Equal(state.Assigned,   byName[DroneAnimationStateNames.Assigned]);
            Assert.Equal(state.Flying,     byName[DroneAnimationStateNames.Flying]);
            Assert.Equal(state.Working,    byName[DroneAnimationStateNames.Working]);
            Assert.Equal(state.Mining,     byName[DroneAnimationStateNames.Mining]);
            Assert.Equal(state.Harvesting, byName[DroneAnimationStateNames.Harvesting]);
        }
    }
}
