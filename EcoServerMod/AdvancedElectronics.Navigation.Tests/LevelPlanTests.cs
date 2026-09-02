using System;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// R19's median target and R20's remove-before-fill ordering, provable from the plan
    /// type alone without running a driver.
    /// </summary>
    public class LevelPlanTests
    {
        private static SurfaceColumn Col(int x, int z, int surfaceY) => new SurfaceColumn(x, z, surfaceY);

        // --- R19: the target is the area's own median surface height ---

        [Fact]
        public void OddColumnCount_TargetsTheMiddleHeight()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 10), Col(1, 0, 14), Col(2, 0, 12) });

            Assert.Equal(12, plan.TargetHeight);
        }

        [Fact]
        public void EvenColumnCount_TargetsTheLowerOfTheTwoMiddleHeights()
        {
            // The median rule for an even count is the LOWER middle height. Named rather
            // than incidental: the lower choice turns the difference into spoil the drone
            // already holds instead of dirt it has to draw from storage.
            var plan = LevelPlan.Build(new[] { Col(0, 0, 10), Col(1, 0, 10), Col(2, 0, 12), Col(3, 0, 12) });

            Assert.Equal(10, plan.TargetHeight);
            Assert.Equal(4, plan.RemovalVolume);
            Assert.Equal(0, plan.FillDemand);
        }

        [Fact]
        public void FlatArea_PlansNoRemovalsAndNoFills()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 8), Col(1, 0, 8), Col(2, 0, 8) });

            Assert.Empty(plan.Removals);
            Assert.Equal(0, plan.RemovalVolume);
            Assert.Equal(0, plan.FillDemand);
        }

        // --- Geometry: which blocks come out, which columns go up ---

        [Fact]
        public void ARemovalCoversEveryBlockAboveTheTarget_TopDown()
        {
            var plan = LevelPlan.Build(new[] { Col(4, 7, 13), Col(0, 0, 10), Col(1, 0, 10) });

            var removal = Assert.Single(plan.Removals);
            Assert.Equal(3, removal.BlockCount);
            Assert.Equal(new[] { 13, 12, 11 }, removal.Positions.Select(p => p.Y));
            Assert.All(removal.Positions, p => Assert.Equal(4, p.X));
            Assert.All(removal.Positions, p => Assert.Equal(7, p.Z));
        }

        [Fact]
        public void AFillCoversEveryBlockUpToTheTarget_BottomUp()
        {
            var plan = LevelPlan.Build(new[] { Col(4, 7, 6), Col(0, 0, 9), Col(1, 0, 9) });

            var fill = Assert.Single(plan.BeginFill(removalBlocksCompleted: 0, bankedSpoil: 3).Fills);
            Assert.Equal(3, fill.BlockCount);
            Assert.Equal(new[] { 7, 8, 9 }, fill.Positions.Select(p => p.Y));
        }

        // --- R20: the ordering is a property of the plan ---

        [Fact]
        public void FillWork_IsUnreachableUntilTheRemovalVolumeIsAccountedFor()
        {
            // The driver cannot fill early even by mistake: there is no path to a fill
            // list that does not pass the completed removal volume.
            var plan = LevelPlan.Build(new[] { Col(0, 0, 4), Col(1, 0, 10), Col(2, 0, 10) });

            Assert.Equal(0, plan.RemovalVolume);
            Assert.Equal(6, plan.FillDemand);

            var partial = LevelPlan.Build(new[] { Col(0, 0, 4), Col(1, 0, 12), Col(2, 0, 10) });
            Assert.Equal(2, partial.RemovalVolume);
            Assert.Throws<InvalidOperationException>(
                () => partial.BeginFill(removalBlocksCompleted: 1, bankedSpoil: 0));
        }

        [Fact]
        public void FillWork_OpensOnceEveryRemovalIsDone()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 4), Col(1, 0, 12), Col(2, 0, 10) });

            var phase = plan.BeginFill(removalBlocksCompleted: plan.RemovalVolume, bankedSpoil: 0);

            Assert.Equal(6, phase.Fills.Sum(f => f.BlockCount));
        }

        // --- AE6: banked spoil is spent before new dirt is drawn ---

        [Fact]
        public void RemovalVolumeBeyondTheHold_StillPlansAFillThatDrawsBankedSpoilFirst()
        {
            // AE6. The hold's capacity is the driver's problem, not the plan's: spoil the
            // drone unloads mid-pass is still its own material, and the fill phase spends
            // it before asking storage for dirt.
            var plan = LevelPlan.Build(new[]
            {
                Col(0, 0, 40), Col(1, 0, 40), Col(2, 0, 10), Col(3, 0, 10), Col(4, 0, 4)
            });

            Assert.Equal(10, plan.TargetHeight);
            Assert.Equal(60, plan.RemovalVolume);
            Assert.Equal(6, plan.FillDemand);

            var phase = plan.BeginFill(removalBlocksCompleted: 60, bankedSpoil: 60);

            Assert.Equal(6, phase.FromBankedSpoil);
            Assert.Equal(0, phase.NewDirtRequired);
        }

        [Fact]
        public void FillDemandEqualToBankedSpoil_RequestsNoNewDirt()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 7), Col(1, 0, 10), Col(2, 0, 10) });

            var phase = plan.BeginFill(removalBlocksCompleted: 0, bankedSpoil: 3);

            Assert.Equal(3, phase.FromBankedSpoil);
            Assert.Equal(0, phase.NewDirtRequired);
        }

        [Fact]
        public void FillDemandBeyondBankedSpoil_RequestsExactlyTheShortfall()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 2), Col(1, 0, 10), Col(2, 0, 10) });

            var phase = plan.BeginFill(removalBlocksCompleted: 0, bankedSpoil: 3);

            Assert.Equal(8, plan.FillDemand);
            Assert.Equal(3, phase.FromBankedSpoil);
            Assert.Equal(5, phase.NewDirtRequired);
        }

        [Fact]
        public void BankedSpoilBeyondTheFillDemand_IsNotSpentJustBecauseItIsHeld()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 9), Col(1, 0, 10), Col(2, 0, 10) });

            var phase = plan.BeginFill(removalBlocksCompleted: 0, bankedSpoil: 500);

            Assert.Equal(1, phase.FromBankedSpoil);
            Assert.Equal(0, phase.NewDirtRequired);
        }

        // --- Argument guards ---

        [Fact]
        public void AnEmptyArea_HasNoMedianAndIsRejected()
        {
            Assert.Throws<ArgumentException>(() => LevelPlan.Build(Array.Empty<SurfaceColumn>()));
        }

        [Fact]
        public void ARepeatedColumn_IsRejected()
        {
            // Two samples of one column would skew the median and plan the same blocks
            // twice, which the driver would then dig twice.
            Assert.Throws<ArgumentException>(
                () => LevelPlan.Build(new[] { Col(0, 0, 10), Col(0, 0, 12) }));
        }

        [Fact]
        public void NegativeBankedSpoil_IsRejected()
        {
            var plan = LevelPlan.Build(new[] { Col(0, 0, 9), Col(1, 0, 10), Col(2, 0, 10) });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => plan.BeginFill(removalBlocksCompleted: 0, bankedSpoil: -1));
        }
    }
}
