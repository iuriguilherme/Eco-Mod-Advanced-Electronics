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
        /// Builds a pass over <paramref name="plot"/>: every block standing in the plot's 5x5
        /// column volume, from each column's CURRENT top down to one shared floor.
        ///
        /// One rule, replacing the three special cases that used to live here (virgin ground,
        /// repeat pass, resumed pass). Each of those was a different answer to "what counts as the
        /// surface", and each got a different case wrong.
        ///
        /// What the rule guarantees is the shape: a clean 5x5 shaft with a flat bottom, and the
        /// 3x3 entrance at the top as its only exception. It follows that anything standing in the
        /// volume is removed rather than stepped over -- residue left by an earlier pass that cut
        /// the wrong width, and backfill, which is real: minable rock never comes back, but a
        /// player can drop dirt, sand or crushed ore into a pit, per block and in any shape.
        ///
        /// The floor is <c>lowest column - (tierDepth - 1)</c>, so a pass always advances the
        /// shaft by its tier while levelling whatever is above. On a flat virgin plot this is
        /// exactly the old plan -- 9 + 14*25 = 359 positions, the figure the Problem Frame quotes.
        /// On a sloped virgin plot it removes MORE than the old per-column version did, because
        /// the high side is cut down to the low side's floor rather than each column keeping its
        /// own depth. That is the "5x5 top to bottom" guarantee costing what it costs.
        ///
        /// Rim columns keep their topmost block and centre columns do not: that difference IS the
        /// entrance (KD13), and it survives every pass, so the mouth stays 3x3 no matter how many
        /// times the plot is deepened or refilled.
        ///
        /// <paramref name="floorY"/> overrides the computed floor, for a pass re-planned mid-cut
        /// after a restart: recomputing it against ground the drone has already excavated would
        /// spend another full tier below the pit floor.
        /// </summary>
        public static ShaftPlan Create(
            PlotCoord plot, int tierDepth, IWorldSampler sampler, int plotSize, int? floorY = null)
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
            var lowest = int.MaxValue;
            var lowestRim = int.MaxValue;

            for (int dz = 0; dz < plotSize; dz++)
            for (int dx = 0; dx < plotSize; dx++)
            {
                var height = (int)MathF.Round(sampler.GroundHeightAt(baseX + dx, baseZ + dz));
                surfaceHeights[(dz * plotSize) + dx] = height;

                if (height < lowest) lowest = height;

                if (IsRim(dx, dz, rimMargin) && height < lowestRim) lowestRim = height;
            }

            // Two different references, and using one for both is a trap.
            //
            // The ENTRANCE is the lowest rim column, which is stable across passes: the rim is
            // never cut below its lip, so on virgin ground its tops are the original surface, and
            // afterwards they ARE the entrance this returns. Measuring it from the whole plot
            // instead would drop it by a tier on every pass and eat the plot from the top down.
            //
            // The FLOOR is a tier below the lowest column of all, which is the pit floor once one
            // exists -- so a pass advances the shaft rather than re-cutting where it already is.
            var entranceLevel = lowestRim;
            var floor = floorY ?? (lowest - (tierDepth - 1));

            // Grouped by height, so a layer is a horizontal slice and stays the unit one game-action
            // pack covers (KTD14). Slices are ragged by construction now -- a column whose top is
            // already below a given height simply contributes nothing to it.
            var byHeight = new Dictionary<int, List<BlockPos>>();
            var highest = int.MinValue;

            for (int dz = 0; dz < plotSize; dz++)
            for (int dx = 0; dx < plotSize; dx++)
            {
                var isRim = IsRim(dx, dz, rimMargin);
                var top = surfaceHeights[(dz * plotSize) + dx];

                for (var y = top; y >= floor; y--)
                {
                    // The rim keeps exactly ONE block, and it is at the entrance level -- the same
                    // height for all sixteen of them, not each column's own surface.
                    //
                    // Per-column was the obvious reading of "leave the rim standing" and it makes
                    // a jagged lip on any slope: the mouth sits at a different height on each side,
                    // and because every layer below is measured from the same tops, that unevenness
                    // is copied all the way down. Pinning it to the lowest original surface means
                    // the ground above is mined away first and the entrance comes out level, which
                    // is what a player building a pit would do by hand.
                    if (isRim && y == entranceLevel) continue;

                    if (!byHeight.TryGetValue(y, out var slice))
                        byHeight[y] = slice = new List<BlockPos>(plotSize * plotSize);

                    slice.Add(new BlockPos(baseX + dx, y, baseZ + dz));
                    if (y > highest) highest = y;
                }
            }

            var layers = new List<ShaftLayer>(byHeight.Count);
            var depth = 0;

            for (var y = highest; y >= floor; y--)
                if (byHeight.TryGetValue(y, out var slice))
                    layers.Add(new ShaftLayer(depth++, slice));

            return new ShaftPlan(plot, layers);
        }

        /// <summary>True for the columns outside the centre opening -- the ring that forms the lip.</summary>
        private static bool IsRim(int dx, int dz, int rimMargin) =>
            dx < rimMargin || dx >= rimMargin + OpeningWidth ||
            dz < rimMargin || dz >= rimMargin + OpeningWidth;

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
