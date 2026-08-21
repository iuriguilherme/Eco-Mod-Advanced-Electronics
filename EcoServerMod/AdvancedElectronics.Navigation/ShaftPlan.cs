using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// One shaft layer: every block position at a single depth below the plot's
    /// own per-column surface (KTD14 — mining submits one game-action pack per
    /// layer, never per block, so a layer is the unit both U5 and U13 work in).
    /// Layer 0 is the surface layer (the 3x3 opening); every layer after it is
    /// the full plot width, descending.
    /// </summary>
    public readonly struct ShaftLayer
    {
        /// <summary>Depth below each column's own surface — 0 is the surface layer.</summary>
        public int Depth { get; }

        public IReadOnlyList<BlockPos> Positions { get; }

        public ShaftLayer(int depth, IReadOnlyList<BlockPos> positions)
        {
            Depth = depth;
            Positions = positions ?? throw new ArgumentNullException(nameof(positions));
        }
    }

    /// <summary>
    /// Turns one property plot into the ordered, layered list of block positions
    /// a mining shaft removes (R11, R12, R16), so nothing above this does
    /// coordinate arithmetic itself. Eco-free (KTD6) — the caller supplies
    /// ground height through <see cref="IWorldSampler"/>, the same seam
    /// <see cref="GridPathfinder"/> uses.
    /// </summary>
    /// <remarks>
    /// Layer 0 is the centre 3x3 columns' own topmost block, one per column —
    /// the 3x3 opening (KD13). The rim (the sixteen columns outside the centre
    /// 3x3) contributes nothing at the surface layer, so it is left standing.
    /// Every layer after that is the full plot width, one position per column,
    /// descending one block per layer, down to <c>tierDepth</c> layers total —
    /// each column's own second-layer block sits one below that column's own
    /// surface, not a shared world height, because terrain is not flat (KD13).
    ///
    /// A flat plot with a five-wide plot and a fifteen-layer tier depth emits
    /// 9 (layer 0) + 14 * 25 (layers 1..14) = 359 positions — the figure the
    /// Problem Frame quotes.
    /// </remarks>
    public sealed class ShaftPlan
    {
        /// <summary>Width, in columns, of the surface-layer opening (KD13's 3x3).</summary>
        public const int OpeningWidth = 3;

        public PlotCoord Plot { get; }

        /// <summary>Layers in submission order, layer 0 (surface) first.</summary>
        public IReadOnlyList<ShaftLayer> Layers { get; }

        /// <summary>Total position count across every layer.</summary>
        public int TotalPositionCount { get; }

        /// <summary>
        /// The deepest Y this plan reaches, or null for an empty plan. This is the pass's floor:
        /// the caller records it when the shaft starts, so a shaft interrupted and re-planned
        /// against the excavated ground still stops where the original pass would have.
        /// </summary>
        public int? FloorY { get; }

        private ShaftPlan(PlotCoord plot, IReadOnlyList<ShaftLayer> layers)
        {
            Plot = plot;
            Layers = layers;
            TotalPositionCount = layers.Sum(l => l.Positions.Count);
            FloorY = layers.SelectMany(l => l.Positions).Select(p => (int?)p.Y).Min();
        }

        /// <summary>
        /// Builds the shaft plan for <paramref name="plot"/>. <paramref name="tierDepth"/> is the
        /// drone tier's total layer count (KD4 — a tier property, not a caller-chosen value).
        /// <paramref name="plotSize"/> is the mod's plot width in world blocks — the same value
        /// <see cref="PlotCoord.FromWorldColumn"/> quantizes against (KTD7), so plot-to-world
        /// conversion here and everywhere else in the mod agree.
        /// </summary>
        public static ShaftPlan Create(PlotCoord plot, int tierDepth, IWorldSampler sampler, int plotSize) =>
            Create(plot, tierDepth, sampler, plotSize, floorY: null, includeSurfaceOpening: true);

        /// <summary>
        /// A pass into a plot that already contains a shaft: a repeat pass after a fresh survey,
        /// or a pass re-planned mid-cut after a restart.
        ///
        /// Differs from <see cref="Create(PlotCoord,int,IWorldSampler,int)"/> in two ways, both
        /// because the ground is no longer terrain but a hole. Every column is levelled to the
        /// LOWEST of them, so a layer is a horizontal slice of the pit rather than a per-column
        /// depth measured from surfaces fourteen blocks apart. And there is no 3x3 opening: the
        /// mouth was cut by the first pass, and cutting another into an open floor only removes
        /// nine of the twenty-five blocks that layer should take.
        /// </summary>
        public static ShaftPlan CreateContinuation(
            PlotCoord plot, int tierDepth, IWorldSampler sampler, int plotSize, int? floorY = null) =>
            Create(plot, tierDepth, sampler, plotSize,
                   floorY, includeSurfaceOpening: false, levelToLowestColumn: true);

        /// <summary>
        /// Builds the shaft plan for <paramref name="plot"/> against the CURRENT surface, which
        /// is what the caller wants every time: a plot is re-planned from whatever ground is
        /// there now, and a second pass over an already-cut plot legitimately starts at the pit
        /// floor (gated by a fresh survey).
        ///
        /// The two extra parameters are what keep a re-plan from turning into a second full
        /// shaft. Depths here are per-column and RELATIVE, so re-planning mid-pass against
        /// ground the drone has itself excavated would spend another <paramref name="tierDepth"/>
        /// below the pit floor and re-cut the 3x3 mouth into an opening that already exists --
        /// observed live as "3x3 in layers 11-18" after a restart around layer 10.
        ///
        /// <paramref name="floorY"/> clamps the plan to the floor the pass was going to reach
        /// anyway (see <see cref="FloorY"/>); positions at or below it are dropped, and layers
        /// left empty are dropped with them. <paramref name="includeSurfaceOpening"/> is false
        /// when re-entering a shaft already cut, because layer 0 exists to open undisturbed
        /// ground.
        /// </summary>
        public static ShaftPlan Create(
            PlotCoord plot, int tierDepth, IWorldSampler sampler, int plotSize,
            int? floorY, bool includeSurfaceOpening, bool levelToLowestColumn = false)
        {
            if (sampler == null)
                throw new ArgumentNullException(nameof(sampler));

            ValidateShape(tierDepth, plotSize);

            int baseX = plot.X * plotSize;
            int baseZ = plot.Z * plotSize;
            int rimMargin = (plotSize - OpeningWidth) / 2;

            // Sampled ONCE. Every layer used to re-sample the same column: 350 lookups for a
            // 5x5x15 shaft where 25 suffice.
            var surfaceHeights = new int[plotSize * plotSize];
            for (int dz = 0; dz < plotSize; dz++)
            for (int dx = 0; dx < plotSize; dx++)
                surfaceHeights[(dz * plotSize) + dx] =
                    (int)MathF.Round(sampler.GroundHeightAt(baseX + dx, baseZ + dz));

            // A continuation levels every column to the LOWEST of them, and that is the whole
            // point of the flag. Per-column depth is right for virgin ground, where the surface
            // is terrain; it is nonsense once the plot contains a hole, because the rim columns
            // keep their original surface (KD13 leaves the rim standing, so the previous pass
            // never removed their topmost block) while the centre columns report the pit floor
            // fourteen blocks lower.
            //
            // Measured per column, a "layer" of that plot is not a horizontal slice at all: the
            // centre nine descend into fresh rock while the sixteen rim positions land in the air
            // above the pit, classify as empty, and are dropped. The visible result is a shaft
            // that narrows to 3x3 the moment a plot is mined a second time -- exactly what a
            // repeat pass produced.
            if (levelToLowestColumn)
            {
                var lowest = surfaceHeights[0];
                foreach (var height in surfaceHeights)
                    if (height < lowest) lowest = height;

                for (var i = 0; i < surfaceHeights.Length; i++) surfaceHeights[i] = lowest;
            }

            int SurfaceY(int dx, int dz) => surfaceHeights[(dz * plotSize) + dx];
            // Inclusive: FloorY is a position the pass was going to remove, not a boundary
            // below it. On stepped terrain it is the deepest column's floor, so a resumed pass
            // can take a shallower column one or two blocks past where its own layer would have
            // ended -- bounded by the terrain step, and preferable to stopping short.
            bool WithinPass(int y) => floorY == null || y >= floorY.Value;

            var layers = new List<ShaftLayer>(tierDepth);

            // Layer 0: the 3x3 surface opening, one position per centre column at that
            // column's own topmost block. Rim columns (outside the centre square)
            // contribute nothing here — they are left standing (KD13).
            if (includeSurfaceOpening)
            {
                var surface = new List<BlockPos>(OpeningWidth * OpeningWidth);
                for (int dz = rimMargin; dz < rimMargin + OpeningWidth; dz++)
                for (int dx = rimMargin; dx < rimMargin + OpeningWidth; dx++)
                {
                    int y = SurfaceY(dx, dz);
                    if (WithinPass(y)) surface.Add(new BlockPos(baseX + dx, y, baseZ + dz));
                }

                if (surface.Count > 0) layers.Add(new ShaftLayer(0, surface));
            }

            // Layers 1..tierDepth-1: the full plot width, one position per column,
            // each column's own surface minus this layer's depth.
            //
            // A re-entered shaft starts at depth 0 instead, because its "surface" IS the floor
            // the previous pass stopped on: the block under the drone's feet is the next one to
            // remove, not one already gone.
            int firstDepth = includeSurfaceOpening ? 1 : 0;
            for (int depth = firstDepth; depth < tierDepth; depth++)
            {
                var positions = new List<BlockPos>(plotSize * plotSize);
                for (int dz = 0; dz < plotSize; dz++)
                for (int dx = 0; dx < plotSize; dx++)
                {
                    int y = SurfaceY(dx, dz) - depth;
                    if (WithinPass(y)) positions.Add(new BlockPos(baseX + dx, y, baseZ + dz));
                }

                if (positions.Count > 0) layers.Add(new ShaftLayer(depth, positions));
            }

            return new ShaftPlan(plot, layers);
        }

        private static void ValidateShape(int tierDepth, int plotSize)
        {
            if (tierDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(tierDepth), "tierDepth must be positive.");
            if (plotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(plotSize), "plotSize must be positive.");
            if (plotSize < OpeningWidth)
                throw new ArgumentOutOfRangeException(nameof(plotSize), $"plotSize must be at least {OpeningWidth} to fit the surface opening.");
        }

        /// <summary>Every position across every layer, in submission order. Recomputed per call.</summary>
        public IReadOnlyList<BlockPos> AllPositions() => Layers.SelectMany(l => l.Positions).ToList();

        /// <summary>
        /// The layers still to submit after <paramref name="completedPositionCount"/> positions
        /// (counted in the same flattened, layer-major order <see cref="AllPositions"/> uses) have
        /// already been worked — the resume point U13 stores across an unload interruption. A count
        /// that lands mid-layer truncates that layer to its own remaining positions rather than
        /// dropping or repeating any of them; a count equal to <see cref="TotalPositionCount"/>
        /// yields no layers.
        /// </summary>
        public IReadOnlyList<ShaftLayer> LayersFrom(int completedPositionCount)
        {
            if (completedPositionCount < 0)
                throw new ArgumentOutOfRangeException(nameof(completedPositionCount));

            var remaining = new List<ShaftLayer>();
            int skipped = 0;
            foreach (var layer in Layers)
            {
                int count = layer.Positions.Count;
                if (skipped + count <= completedPositionCount)
                {
                    skipped += count;
                    continue;
                }

                int skipWithinLayer = Math.Max(0, completedPositionCount - skipped);
                remaining.Add(skipWithinLayer == 0
                    ? layer
                    : new ShaftLayer(layer.Depth, layer.Positions.Skip(skipWithinLayer).ToList()));
                skipped += count;
            }

            return remaining;
        }
    }
}
