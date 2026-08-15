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

        private ShaftPlan(PlotCoord plot, IReadOnlyList<ShaftLayer> layers)
        {
            Plot = plot;
            Layers = layers;
            TotalPositionCount = layers.Sum(l => l.Positions.Count);
        }

        /// <summary>
        /// Builds the shaft plan for <paramref name="plot"/>. <paramref name="tierDepth"/> is the
        /// drone tier's total layer count (KD4 — a tier property, not a caller-chosen value).
        /// <paramref name="plotSize"/> is the mod's plot width in world blocks — the same value
        /// <see cref="PlotCoord.FromWorldColumn"/> quantizes against (KTD7), so plot-to-world
        /// conversion here and everywhere else in the mod agree.
        /// </summary>
        public static ShaftPlan Create(PlotCoord plot, int tierDepth, IWorldSampler sampler, int plotSize)
        {
            if (sampler == null)
                throw new ArgumentNullException(nameof(sampler));
            if (tierDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(tierDepth), "tierDepth must be positive.");
            if (plotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(plotSize), "plotSize must be positive.");
            if (plotSize < OpeningWidth)
                throw new ArgumentOutOfRangeException(nameof(plotSize), $"plotSize must be at least {OpeningWidth} to fit the surface opening.");

            int baseX = plot.X * plotSize;
            int baseZ = plot.Z * plotSize;
            int rimMargin = (plotSize - OpeningWidth) / 2;

            var layers = new List<ShaftLayer>(tierDepth);

            // Layer 0: the 3x3 surface opening, one position per centre column at that
            // column's own topmost block. Rim columns (outside the centre square)
            // contribute nothing here — they are left standing (KD13).
            var surface = new List<BlockPos>(OpeningWidth * OpeningWidth);
            for (int dz = rimMargin; dz < rimMargin + OpeningWidth; dz++)
            for (int dx = rimMargin; dx < rimMargin + OpeningWidth; dx++)
                surface.Add(SurfacePosition(sampler, baseX + dx, baseZ + dz));
            layers.Add(new ShaftLayer(0, surface));

            // Layers 1..tierDepth-1: the full plot width, one position per column,
            // each column's own surface minus this layer's depth.
            for (int depth = 1; depth < tierDepth; depth++)
            {
                var positions = new List<BlockPos>(plotSize * plotSize);
                for (int dz = 0; dz < plotSize; dz++)
                for (int dx = 0; dx < plotSize; dx++)
                {
                    int wx = baseX + dx, wz = baseZ + dz;
                    int surfaceY = SurfaceY(sampler, wx, wz);
                    positions.Add(new BlockPos(wx, surfaceY - depth, wz));
                }
                layers.Add(new ShaftLayer(depth, positions));
            }

            return new ShaftPlan(plot, layers);
        }

        private static BlockPos SurfacePosition(IWorldSampler sampler, int x, int z) =>
            new BlockPos(x, SurfaceY(sampler, x, z), z);

        private static int SurfaceY(IWorldSampler sampler, int x, int z) =>
            (int)MathF.Round(sampler.GroundHeightAt(x, z));

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
