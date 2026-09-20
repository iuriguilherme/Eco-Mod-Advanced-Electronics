using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class BlockClassifierContractTests
    {
        [Fact]
        public void MinableClassification_YieldsFourThroughTable()
        {
            var table = new YieldTable(minableYield: 4, excavatableYield: 1);
            Assert.Equal(4, table.YieldFor(BlockClassification.Minable));
        }

        [Fact]
        public void ExcavatableClassification_YieldsOneThroughTable()
        {
            var table = new YieldTable(minableYield: 4, excavatableYield: 1);
            Assert.Equal(1, table.YieldFor(BlockClassification.Excavatable));
        }

        [Fact]
        public void NotRemovableClassification_YieldsZero_NeverPassedToARemoval()
        {
            var table = new YieldTable(minableYield: 4, excavatableYield: 1);
            Assert.Equal(0, table.YieldFor(BlockClassification.NotRemovable));
        }

        [Theory]
        [InlineData(FakeReason.FormBearing)]
        [InlineData(FakeReason.TreeDebris)]
        [InlineData(FakeReason.Empty)]
        [InlineData(FakeReason.WorldObjectBlock)]
        [InlineData(FakeReason.Contained)]
        [InlineData(FakeReason.RootBlocked)]
        [InlineData(FakeReason.Ramp)]
        public void EachExclusion_ClassifiesNotRemovable(FakeReason reason)
        {
            var classifier = new FakeBlockClassifier();
            classifier.Set(0, 0, 0, reason);

            Assert.Equal(BlockClassification.NotRemovable, classifier.Classify(0, 0, 0));
        }

        [Fact]
        public void FormBearingMinableBlock_ClassifiesNotRemovable_FormBearingWins()
        {
            // AE5: a wall built from minable stone -- form-bearing wins over minable.
            var classifier = new FakeBlockClassifier();
            classifier.Set(0, 0, 0, FakeReason.FormBearing);

            Assert.Equal(BlockClassification.NotRemovable, classifier.Classify(0, 0, 0));
        }

        [Fact]
        public void ClassifierConsultedOncePerPosition_AnswerDrivesDecision_AllNotRemovableProducesZeroRemovals()
        {
            var classifier = new FakeBlockClassifier(); // every unset position defaults NotRemovable
            var positions = new (int X, int Y, int Z)[] { (0, 0, 0), (1, 0, 0), (2, 0, 0) };

            var toRemove = positions.Where(p => classifier.Classify(p.X, p.Y, p.Z) != BlockClassification.NotRemovable).ToList();

            Assert.Empty(toRemove);
            Assert.Equal(3, classifier.CallCount);
        }

        [Fact]
        public void YieldTable_UsesInjectedConstants_NothingHardCoded()
        {
            var table = new YieldTable(minableYield: 7, excavatableYield: 3);

            Assert.Equal(7, table.YieldFor(BlockClassification.Minable));
            Assert.Equal(3, table.YieldFor(BlockClassification.Excavatable));
        }

        // --- U4: the at-bedrock walk down a column (R7, R8, KTD4) ---
        //
        // The walk exists because the recorded surface is the top SOLID block
        // (Eco's GetTopSolidBlockY), and a player-built block is solid. On a capped
        // column the "surface" is the cap, so a single read beneath it finds only the
        // dug-out air the mining drone left -- never the impenetrable block. Every
        // scenario below is decided by the walk's termination rules alone, which is
        // why it lives in the Eco-free library (KTD12).

        [Fact]
        public void ColumnRestingDirectlyOnImpenetrable_ReadsAtBedrock()
        {
            var probe = new FakeColumnProbe();
            probe.Set(4, 0, 9, ColumnBlock.Impenetrable);

            Assert.True(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: 0, z: 9));
            Assert.Equal(1, probe.CallCount);   // no walk needed: the surface IS the floor
        }

        [Fact]
        public void ColumnWithOrdinaryRockAboveTheFloor_DoesNotReadAtBedrock()
        {
            var probe = new FakeColumnProbe();
            probe.Set(4, 1, 9, ColumnBlock.Terrain);        // one rock layer left standing
            probe.Set(4, 0, 9, ColumnBlock.Impenetrable);

            Assert.False(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: 1, z: 9));
            Assert.Equal(1, probe.CallCount);   // natural terrain stops the walk at once
        }

        [Fact]
        public void CappedAndDugOutColumn_WalksPastTheCapAndTheAirToTheFloor()
        {
            // Covers AE9. A player-built block caps the column; the mining drone skipped
            // that column's cap but dug everything beneath it out, so the shaft is air
            // from the cap down to the impenetrable floor.
            var probe = new FakeColumnProbe();
            probe.Set(4, 70, 9, ColumnBlock.Built);
            for (var y = 69; y >= 1; y--)
                probe.Set(4, y, 9, ColumnBlock.Empty);
            probe.Set(4, 0, 9, ColumnBlock.Impenetrable);

            Assert.True(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: 70, z: 9));
        }

        [Fact]
        public void ColumnUnderATallTower_StopsAtTheReadCap_RatherThanWalkingUnbounded()
        {
            // Every read is a built block, so nothing but the cap can end the walk.
            var probe = new FakeColumnProbe(unsetIs: ColumnBlock.Built);

            Assert.False(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: 100000, z: 9));
            Assert.Equal(BedrockWalk.MaxColumnReads, probe.CallCount);
        }

        [Fact]
        public void DirtHoldingAPlant_IsGroundNotAnObstruction_SoTheColumnIsNotAtBedrock()
        {
            // R7 draws the line the walk needs: the plant block itself is an obstruction
            // the walk steps past, but the dirt holding it is ground standing above the
            // floor, so the column is not at bedrock.
            var probe = new FakeColumnProbe();
            probe.Set(4, 61, 9, ColumnBlock.Built);        // the plant
            probe.Set(4, 60, 9, ColumnBlock.Terrain);      // the dirt it grows in
            probe.Set(4, 0, 9, ColumnBlock.Impenetrable);

            Assert.False(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: 61, z: 9));
        }

        [Fact]
        public void WalkThatRunsOutOfWorldBeneathTheColumn_DoesNotReadAtBedrock()
        {
            // Nothing but air all the way down past y = 0: the world floor bound ends the
            // walk, and an unproven column never claims bedrock.
            var probe = new FakeColumnProbe(unsetIs: ColumnBlock.Empty);

            Assert.False(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: 5, z: 9));
            Assert.Equal(6, probe.CallCount);              // y = 5 down to y = 0 inclusive
        }

        [Fact]
        public void WalkBelowTheWorldFloor_IsNeverAttempted()
        {
            var probe = new FakeColumnProbe(unsetIs: ColumnBlock.Empty);

            Assert.False(BedrockWalk.ColumnRestsOnBedrock(probe, 4, surfaceY: -1, z: 9));
            Assert.Equal(0, probe.CallCount);
        }

        /// <summary>Hand-rolled fake IColumnProbe: whatever the test puts at each y, defaulting to <c>unsetIs</c>.</summary>
        private sealed class FakeColumnProbe : IColumnProbe
        {
            private readonly Dictionary<(int, int, int), ColumnBlock> _blocks = new Dictionary<(int, int, int), ColumnBlock>();
            private readonly ColumnBlock _unset;

            public FakeColumnProbe(ColumnBlock unsetIs = ColumnBlock.Empty) => _unset = unsetIs;

            public int CallCount { get; private set; }

            public void Set(int x, int y, int z, ColumnBlock block) => _blocks[(x, y, z)] = block;

            public ColumnBlock ProbeColumn(int x, int y, int z)
            {
                CallCount++;
                return _blocks.TryGetValue((x, y, z), out var b) ? b : _unset;
            }
        }

        public enum FakeReason
        {
            Removable,
            FormBearing,
            TreeDebris,
            Empty,
            WorldObjectBlock,
            Contained,
            RootBlocked,
            Ramp
        }

        /// <summary>Hand-rolled fake IBlockClassifier -- every "not removable" reason maps to the same category, matching the real classifier's contract.</summary>
        private sealed class FakeBlockClassifier : IBlockClassifier
        {
            private readonly Dictionary<(int, int, int), FakeReason> _reasons = new Dictionary<(int, int, int), FakeReason>();

            public int CallCount { get; private set; }

            public void Set(int x, int y, int z, FakeReason reason) => _reasons[(x, y, z)] = reason;

            public BlockClassification Classify(int x, int y, int z)
            {
                CallCount++;
                var reason = _reasons.TryGetValue((x, y, z), out var r) ? r : FakeReason.Empty;
                return reason == FakeReason.Removable ? BlockClassification.Minable : BlockClassification.NotRemovable;
            }
        }
    }
}
