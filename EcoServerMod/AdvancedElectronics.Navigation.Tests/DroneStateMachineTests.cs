using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Covers U8's pure status state machine (Idle/EnRoute/Surveying/Unreachable) per
    /// the state diagram in the unit spec. Written before <see cref="DroneStateMachine"/>'s
    /// implementation was finalized (test-first, per the unit's Execution note) -- every
    /// [Fact] here started red against a stub/empty implementation.
    /// </summary>
    public class DroneStateMachineTests
    {
        // --- Test scenario 1 (R6): no district assigned -> Idle, no sampling ---

        [Fact]
        public void NoDistrictAssigned_StaysIdle_AndDoesNotSample()
        {
            var sm = new DroneStateMachine();

            Assert.Equal(DroneStatus.Idle, sm.Status);
            // The Eco side skips its per-tick survey work when this is false (don't
            // sample on Idle).
            Assert.False(sm.ShouldSample);
        }

        // --- Idle -> EnRoute on first assignment (baseline for scenario 2) ---

        [Fact]
        public void DistrictAssignedFromIdle_TransitionsToEnRoute_WithTargetDistrict()
        {
            var sm = new DroneStateMachine();

            sm.OnDistrictAssigned("Farmland");

            Assert.Equal(DroneStatus.EnRoute, sm.Status);
            Assert.Equal(DroneTravelTarget.District, sm.TravelTarget);
            Assert.Equal("Farmland", sm.TargetDistrictName);
            Assert.False(sm.ShouldSample);
        }

        // --- EnRoute -> Surveying on arrival, and sampling turns on ---

        [Fact]
        public void ArrivedAtDistrict_TransitionsToSurveying_AndEnablesSampling()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");

            sm.OnArrived();

            Assert.Equal(DroneStatus.OnStation, sm.Status);
            Assert.True(sm.ShouldSample);
        }

        // --- Test scenario 2 (R13): reassignment while Surveying re-paths to the NEW
        //     district, not the old one or the dock ---

        [Fact]
        public void ReassignedWhileSurveying_TransitionsToEnRoute_TargetingNewDistrict()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");
            sm.OnArrived();
            Assert.Equal(DroneStatus.OnStation, sm.Status);

            sm.OnDistrictAssigned("Mining Zone");

            Assert.Equal(DroneStatus.EnRoute, sm.Status);
            Assert.Equal(DroneTravelTarget.District, sm.TravelTarget);
            Assert.Equal("Mining Zone", sm.TargetDistrictName);
            Assert.NotEqual("Farmland", sm.TargetDistrictName);
        }

        // --- Test scenario 3 (AE9/R15): no path to the district -> Unreachable, and a
        //     return-to-dock attempt is signaled ---

        [Fact]
        public void NoPathToDistrict_TransitionsToUnreachable_AndSignalsReturnAttempt()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");

            var attemptReturn = sm.OnNoPathFound();

            Assert.Equal(DroneStatus.Unreachable, sm.Status);
            Assert.True(attemptReturn);
            Assert.Equal(DroneTravelTarget.Dock, sm.TravelTarget);
        }

        // --- Test scenario 4 (R15): no path on the RETURN leg from Surveying lands in
        //     Unreachable directly, not Idle, not stuck ---

        [Fact]
        public void NoPathOnReturnLeg_FromSurveying_GoesToUnreachable_NotIdleOrStuck()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");
            sm.OnArrived();
            Assert.Equal(DroneStatus.OnStation, sm.Status);

            var attemptReturn = sm.OnNoPathFound();

            Assert.Equal(DroneStatus.Unreachable, sm.Status);
            Assert.True(attemptReturn);
        }

        // --- Test scenario 4 (R15), continued: a failed retry while already
        //     Unreachable stays Unreachable rather than transitioning anywhere ---

        [Fact]
        public void NoPathOnReturnLeg_WhileAlreadyUnreachable_StaysUnreachable_AndDoesNotResignalReturn()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");
            sm.OnNoPathFound(); // -> Unreachable, first return attempt signaled

            var attemptReturnAgain = sm.OnNoPathFound(); // retry also fails

            Assert.Equal(DroneStatus.Unreachable, sm.Status);
            Assert.False(attemptReturnAgain);
        }

        // --- Test scenario 5 (R6): district cleared while Surveying -> EnRoute (back
        //     to dock) -> Idle on arrival -- the FULL two-step transition ---

        [Fact]
        public void DistrictClearedWhileSurveying_GoesEnRouteToDock_ThenIdleOnArrival()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");
            sm.OnArrived();
            Assert.Equal(DroneStatus.OnStation, sm.Status);

            sm.OnDistrictCleared();

            Assert.Equal(DroneStatus.EnRoute, sm.Status);
            Assert.Equal(DroneTravelTarget.Dock, sm.TravelTarget);

            sm.OnReturnedToDock();

            Assert.Equal(DroneStatus.Idle, sm.Status);
        }

        // --- Unreachable -> EnRoute: new reachable district assigned (explicit
        //     diagram arrow) ---

        [Fact]
        public void UnreachableThenNewDistrictAssigned_TransitionsToEnRoute()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");
            sm.OnNoPathFound();
            Assert.Equal(DroneStatus.Unreachable, sm.Status);

            sm.OnDistrictAssigned("Mining Zone");

            Assert.Equal(DroneStatus.EnRoute, sm.Status);
            Assert.Equal(DroneTravelTarget.District, sm.TravelTarget);
            Assert.Equal("Mining Zone", sm.TargetDistrictName);
        }

        // --- Unreachable -> Idle: "returned to dock" (explicit diagram arrow) ---

        [Fact]
        public void UnreachableThenReturnedToDock_TransitionsToIdle()
        {
            var sm = new DroneStateMachine();
            sm.OnDistrictAssigned("Farmland");
            sm.OnNoPathFound();
            Assert.Equal(DroneStatus.Unreachable, sm.Status);

            sm.OnReturnedToDock();

            Assert.Equal(DroneStatus.Idle, sm.Status);
        }
    }
}
