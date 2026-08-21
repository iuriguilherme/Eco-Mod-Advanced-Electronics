using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class ShaftPlanTests
    {
        private const int PlotSize = 5;
        private const int TierDepth = 15; // 9 + 14*25 = 359, the Problem Frame's figure.

        [Fact]
        public void FlatPlot_EmitsNineSurfacePositionsAndFullFiveByFiveBelow()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            Assert.Equal(9, plan.Layers[0].Positions.Count);
            for (int depth = 1; depth < TierDepth; depth++)
                Assert.Equal(25, plan.Layers[depth].Positions.Count);
        }

        // --- One rule: clear the 5x5 volume from each column's top down to a shared floor ---

        [Fact]
        public void ResidualBlocksLeftByAnEarlierPass_AreRemovedRatherThanSteppedOver()
        {
            // The reported state: earlier passes cut only the centre 3x3, so the rim columns still
            // stand at the original surface while the centre is fifteen blocks down. A plan that
            // starts at the pit floor never touches them and the shaft stays 3x3 forever.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            for (int dx = 1; dx <= 3; dx++)
            for (int dz = 1; dz <= 3; dz++)
                sampler.SetHeight(dx, dz, 49f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var rim = plan.AllPositions().Where(p => p.X == 0 && p.Z == 0).Select(p => p.Y).ToList();

            Assert.Contains(63, rim);
            Assert.Contains(50, rim);
            Assert.Equal(plan.FloorY, rim.Min());
        }

        [Fact]
        public void BackfillIsRemovedPerBlock_NotPerLayer()
        {
            // Minable rock never returns, but a player can drop dirt or crushed ore back into a
            // pit -- one column, part of a layer, any shape. Whatever stands in the volume goes.
            var sampler = new FakeWorldSampler(defaultHeight: 49f);
            sampler.SetHeight(0, 0, 55f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var refilled = plan.AllPositions().Where(p => p.X == 0 && p.Z == 0).Select(p => p.Y).ToList();

            // Everything the fill added is cut back down to the entrance level...
            Assert.Contains(55, refilled);
            Assert.Contains(50, refilled);

            // ...but not the lip itself, which is where the entrance is and stays.
            Assert.DoesNotContain(49, refilled);
            Assert.Equal(plan.FloorY, refilled.Min());
        }

        [Fact]
        public void TheEntranceIsLevel_EvenOnASlope()
        {
            // A per-column lip is jagged on a slope, and because every layer below was measured
            // from those same tops, the unevenness was copied all the way down. The entrance is
            // pinned to the lowest rim column instead: the ground above it is mined away first.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(0, 0, 70f);
            sampler.SetHeight(4, 4, 67f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            // 64 is the lowest rim column, so that is the entrance. Every rim column is cut down
            // THROUGH its own surface to reach it, and keeps only the block at 64 itself.
            for (var x = 0; x < PlotSize; x++)
            for (var z = 0; z < PlotSize; z++)
            {
                if (x != 0 && x != 4 && z != 0 && z != 4) continue;

                var cut = plan.AllPositions().Where(p => p.X == x && p.Z == z).Select(p => p.Y).ToHashSet();

                Assert.DoesNotContain(64, cut); // the lip, at one height for all sixteen
                Assert.Contains(63, cut);       // the shaft continues below it
            }

            // The two raised columns lose everything above the entrance.
            var raised = plan.AllPositions().Where(p => p.X == 0 && p.Z == 0).Select(p => p.Y).ToList();
            Assert.Contains(70, raised);
            Assert.Contains(65, raised);
        }

        [Fact]
        public void TheEntranceHoldsItsLevelAcrossPasses()
        {
            // Measured from the whole plot, the entrance would drop by a tier every pass and eat
            // the plot from the top down. The rim is never cut below its lip, so reading the level
            // off the rim makes it stable: after one pass the rim tops ARE the entrance.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            for (int dx = 1; dx <= 3; dx++)
            for (int dz = 1; dz <= 3; dz++)
                sampler.SetHeight(dx, dz, 49f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var rimCut = plan.AllPositions().Where(p => p.X == 0 && p.Z == 0).Select(p => p.Y).ToList();

            Assert.DoesNotContain(64, rimCut); // the lip stays where the first pass put it
            Assert.Contains(63, rimCut);
        }

        [Fact]
        public void EveryColumnEndsOnTheSameFloor()
        {
            // The flat bottom is the "5x5 top to bottom" guarantee. Ragged tops, one floor.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(2, 2, 49f);
            sampler.SetHeight(4, 4, 58f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            var floors = plan.AllPositions()
                .GroupBy(p => (p.X, p.Z))
                .Select(g => g.Min(p => p.Y))
                .Distinct()
                .ToList();

            Assert.Single(floors);
            Assert.Equal(plan.FloorY, floors[0]);
        }

        [Fact]
        public void EveryLayerIsOneDescendingHorizontalSlice()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(2, 2, 49f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            Assert.All(plan.Layers, layer => Assert.Single(layer.Positions.Select(p => p.Y).Distinct()));

            var heights = plan.Layers.Select(l => l.Positions[0].Y).ToList();
            Assert.Equal(heights.OrderByDescending(y => y).ToList(), heights);
        }

        [Fact]
        public void ReplanningMidPass_StopsAtTheRecordedFloor_InsteadOfSpendingASecondTierDepth()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            var original = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var floor = original.FloorY.Value;

            for (int x = 0; x < PlotSize; x++)
            for (int z = 0; z < PlotSize; z++)
                sampler.SetHeight(x, z, 54f);

            var unclamped = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var resumed = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize, floorY: floor);

            Assert.True(unclamped.FloorY < floor);
            Assert.Equal(floor, resumed.FloorY);
        }

        [Fact]
        public void APassWhoseFloorIsAlreadyReached_EmitsNothing()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 50f);

            var resumed = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize, floorY: 51);

            Assert.Empty(resumed.Layers);
            Assert.Null(resumed.FloorY);
        }

        [Fact]
        public void SteppedVirginTerrain_KeepsEachColumnsOwnTop()
        {
            // Tops follow the terrain; only the floor is shared. Cutting the high side down to a
            // shared plane is deliberate -- it is what "5x5 top to bottom" costs on a slope.
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            sampler.SetHeight(1, 1, 10f);
            sampler.SetHeight(2, 2, 20f);
            sampler.SetHeight(3, 3, 30f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var tops = plan.AllPositions().GroupBy(p => (p.X, p.Z)).ToDictionary(g => g.Key, g => g.Max(p => p.Y));

            Assert.Equal(10, tops[(1, 1)]);
            Assert.Equal(20, tops[(2, 2)]);
            Assert.Equal(30, tops[(3, 3)]);
        }

        [Fact]
        public void RimColumns_ContributeNoSurfacePosition()
        {
            // The rim keeping its topmost block IS the entrance (KD13), and it holds on every
            // pass -- otherwise mining a refilled plot again widens the mouth.
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var surface = plan.Layers[0].Positions;

            Assert.DoesNotContain(surface, p => p.X == 0 || p.X == 4 || p.Z == 0 || p.Z == 4);
            Assert.Equal(9, surface.Count);
        }

        [Fact]
        public void LayerGrouping_NoLayerSpansTwoDepths_EveryPositionInExactlyOneLayer()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            sampler.SetHeight(1, 1, 12f); // uneven terrain so "depth" isn't trivially one world Y
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            var seen = new HashSet<BlockPos>();
            foreach (var layer in plan.Layers)
            {
                foreach (var pos in layer.Positions)
                    Assert.True(seen.Add(pos), $"{pos} appeared in more than one layer.");
            }
            Assert.Equal(plan.TotalPositionCount, seen.Count);
        }

        [Fact]
        public void ResumeMidShaft_YieldsExactlyRemainingPositions_SameOrder_NoRepeatsNoneSkipped()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var all = plan.AllPositions();

            const int resumeAt = 20; // lands inside layer 0/1 boundary region
            var remaining = plan.LayersFrom(resumeAt).SelectMany(l => l.Positions).ToList();

            Assert.Equal(all.Skip(resumeAt), remaining);
        }

        [Fact]
        public void ResumeAtLastPosition_YieldsEmptyRemainder()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            var remaining = plan.LayersFrom(plan.TotalPositionCount);

            Assert.Empty(remaining);
        }

        [Fact]
        public void FlatPlot_TotalPositionCount_MatchesPlanFigure()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            Assert.Equal(359, plan.TotalPositionCount);
        }

        [Fact]
        public void WorldWrapSeamPlot_QuantizesToSamePlotTheEngineWouldChoose()
        {
            // Q3: the shared quantization function (PlotCoord.FromWorldColumn) is what
            // ShaftPlan's own plot->world math must agree with; this is a regression
            // guard on that agreement, not a claim about the engine's own wrap seam.
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plot = PlotCoord.FromWorldColumn(-1, -1, PlotSize);
            var plan = ShaftPlan.Create(plot, TierDepth, sampler, PlotSize);

            Assert.All(plan.Layers[0].Positions, p => Assert.Equal(plot, PlotCoord.FromWorldColumn(p.X, p.Z, PlotSize)));
        }

        private sealed class FakeWorldSampler : IWorldSampler
        {
            private readonly Dictionary<(int X, int Z), float> _heights = new Dictionary<(int, int), float>();
            private readonly float _defaultHeight;

            public FakeWorldSampler(float defaultHeight) => _defaultHeight = defaultHeight;

            public void SetHeight(int x, int z, float height) => _heights[(x, z)] = height;

            public bool IsSolidAt(int x, int z) => false;

            public bool IsObstacleAt(int x, int z) => false;

            public float GroundHeightAt(int x, int z) =>
                _heights.TryGetValue((x, z), out float height) ? height : _defaultHeight;
        }
    }
}
