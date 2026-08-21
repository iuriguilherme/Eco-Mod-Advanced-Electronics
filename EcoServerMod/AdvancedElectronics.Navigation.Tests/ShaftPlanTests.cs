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

        // The restart case. A shaft's depths are per-column and RELATIVE, and the shaft
        // destroys the surface it was measured from -- so re-planning mid-pass against ground
        // the drone has itself excavated spends another full TierDepth below the pit floor and
        // re-cuts the 3x3 mouth into an opening that already exists. Live pass: "3x3 in layers
        // 11-18" after restarting around layer 10.
        //
        // Re-planning is the DESIRED behaviour -- a second pass on an already-cut plot starts
        // from the pit floor by design, gated by a fresh survey. The pass floor is the one
        // thing a re-plan cannot re-derive, which is why it is the one thing recorded.
        [Fact]
        public void ReplanningMidPass_StopsAtTheRecordedFloor_InsteadOfSpendingASecondTierDepth()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            var original = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var floor = original.FloorY.Value;

            // The drone cuts ten layers; the world's idea of "surface" drops with it.
            for (int x = 0; x < PlotSize; x++)
            for (int z = 0; z < PlotSize; z++)
                sampler.SetHeight(x, z, 54f);

            var unclamped = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var resumed = ShaftPlan.CreateContinuation(
                new PlotCoord(0, 0), TierDepth, sampler, PlotSize, floorY: floor);

            Assert.True(unclamped.FloorY < floor);   // the defect: straight past the tier limit
            Assert.Equal(floor, resumed.FloorY);     // the fix: stops exactly where it would have
        }

        [Fact]
        public void ReplanningMidPass_CutsNoFreshSurfaceOpening()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 54f);

            var resumed = ShaftPlan.CreateContinuation(
                new PlotCoord(0, 0), TierDepth, sampler, PlotSize, floorY: 50);

            // Every layer is the full plot width -- no 9-position 3x3 mouth at the pit floor.
            Assert.All(resumed.Layers, layer => Assert.Equal(PlotSize * PlotSize, layer.Positions.Count));
        }

        [Fact]
        public void APassWhoseFloorIsAlreadyReached_EmitsNothing()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 50f);

            var resumed = ShaftPlan.CreateContinuation(
                new PlotCoord(0, 0), TierDepth, sampler, PlotSize, floorY: 51);

            Assert.Empty(resumed.Layers);
            Assert.Null(resumed.FloorY);
        }

        // The second-pass case. KD13 leaves the rim standing, so after one pass the sixteen rim
        // columns still report their ORIGINAL surface while the centre nine report the pit floor
        // fourteen blocks down. Planned per column, a "layer" of that plot stops being a
        // horizontal slice: the rim positions land in mid-air above the pit and drop out as
        // empty, and the shaft narrows to 3x3 for every pass after the first.
        [Fact]
        public void AContinuationLevelsTheColumns_SoARepeatPassStaysFullWidth()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 64f);

            // The state one pass leaves behind: centre 3x3 cut to the floor, rim still at surface.
            for (int dx = 1; dx <= 3; dx++)
            for (int dz = 1; dz <= 3; dz++)
                sampler.SetHeight(dx, dz, 49f);

            var continuation = ShaftPlan.CreateContinuation(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            // Every layer is a full horizontal slice, and every slice sits at ONE height.
            Assert.All(continuation.Layers, layer =>
            {
                Assert.Equal(PlotSize * PlotSize, layer.Positions.Count);
                Assert.Single(layer.Positions.Select(p => p.Y).Distinct());
            });

            // Measured from the pit floor, not from the rim fifteen blocks above it.
            Assert.Equal(49, continuation.Layers[0].Positions[0].Y);
        }

        [Fact]
        public void AFirstPassStillUsesPerColumnSurfaces()
        {
            // The levelling is for holes, not for hills: virgin sloped ground must keep following
            // each column's own surface, which is what KD13 asks for.
            var sampler = new FakeWorldSampler(defaultHeight: 64f);
            sampler.SetHeight(0, 0, 60f);

            var virgin = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);

            Assert.True(virgin.Layers[1].Positions.Select(p => p.Y).Distinct().Count() > 1);
        }

        [Fact]
        public void SteppedTerrain_CentreColumnsEachUseOwnSurfaceHeight()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            // Three different heights across the centre 3x3 (world columns 1..3, plot size 5).
            sampler.SetHeight(1, 1, 10f);
            sampler.SetHeight(2, 2, 20f);
            sampler.SetHeight(3, 3, 30f);

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var surface = plan.Layers[0].Positions;

            Assert.Contains(surface, p => p.X == 1 && p.Z == 1 && p.Y == 10);
            Assert.Contains(surface, p => p.X == 2 && p.Z == 2 && p.Y == 20);
            Assert.Contains(surface, p => p.X == 3 && p.Z == 3 && p.Y == 30);
        }

        [Fact]
        public void BelowSurface_EachColumnDescendsFromItsOwnSurface_NotASharedPlane()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            sampler.SetHeight(0, 0, 10f); // low column
            sampler.SetHeight(1, 0, 20f); // high column

            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var secondLayer = plan.Layers[1].Positions; // one below each column's own surface

            var low = secondLayer.Single(p => p.X == 0 && p.Z == 0);
            var high = secondLayer.Single(p => p.X == 1 && p.Z == 0);

            Assert.Equal(9, low.Y);
            Assert.Equal(19, high.Y);
        }

        [Fact]
        public void RimColumns_ContributeNoSurfacePosition()
        {
            var sampler = new FakeWorldSampler(defaultHeight: 10f);
            var plan = ShaftPlan.Create(new PlotCoord(0, 0), TierDepth, sampler, PlotSize);
            var surface = plan.Layers[0].Positions;

            // Rim = the 16 columns outside the centre 3x3 (world columns 0 and 4 on either axis).
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
