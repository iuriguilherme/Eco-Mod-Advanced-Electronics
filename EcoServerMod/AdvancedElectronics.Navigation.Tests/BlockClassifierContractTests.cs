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
