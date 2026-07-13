using System;
using System.Collections.Generic;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// A single cell's coordinates in a <see cref="SurveyGrid"/>. Distinct
    /// from a world block position - a cell aggregates many blocks.
    /// </summary>
    public readonly struct SurveyCell : IEquatable<SurveyCell>
    {
        public int X { get; }

        public int Z { get; }

        public SurveyCell(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(SurveyCell other) => X == other.X && Z == other.Z;

        public override bool Equals(object obj) => obj is SurveyCell other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);

        public override string ToString() => $"({X}, {Z})";
    }

    /// <summary>
    /// Outcome of a <see cref="SurveyGrid.DensestCell"/> query. Mirrors
    /// GridPathfinder.PathResult's Found-flag pattern: "no data yet for
    /// this ore" is an expected, normal outcome (e.g. querying before any
    /// sample of that ore type has been recorded), not an exceptional one -
    /// callers check <see cref="Found"/> rather than trusting a default
    /// struct's zeroed-out cell/counts, which could otherwise be mistaken
    /// for a real (0, 0) cell with a real (if degenerate) ratio.
    /// </summary>
    public readonly struct DensestCellResult
    {
        public bool Found { get; }

        public SurveyCell Cell { get; }

        public int OreCount { get; }

        public int SampledCount { get; }

        /// <summary>Ore-count / sampled-count for <see cref="Cell"/>. 0 when <see cref="Found"/> is false.</summary>
        public float Ratio { get; }

        private DensestCellResult(bool found, SurveyCell cell, int oreCount, int sampledCount, float ratio)
        {
            Found = found;
            Cell = cell;
            OreCount = oreCount;
            SampledCount = sampledCount;
            Ratio = ratio;
        }

        public static DensestCellResult NotFound { get; } = new DensestCellResult(false, default, 0, 0, 0f);

        public static DensestCellResult Success(SurveyCell cell, int oreCount, int sampledCount) =>
            new DensestCellResult(true, cell, oreCount, sampledCount, sampledCount == 0 ? 0f : (float)oreCount / sampledCount);
    }

    /// <summary>
    /// Fixed grid of cells accumulating per-ore sample density as the drone
    /// roams, queried through <see cref="IOreReader"/>-shaped callers. This
    /// type has no dependency on any Eco.* namespace - it is the pure data
    /// model behind R7/R8/KTD5's survey-density behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cell mapping convention.</b> A world block position (x, z) maps to
    /// a cell via floor division by <c>cellSize</c>:
    /// <c>cellX = floor(x / cellSize)</c>. Floor (not round, not truncation)
    /// is used deliberately so the mapping is gapless and non-overlapping
    /// across the whole plane, including negative coordinates - plain
    /// integer division (C#'s default truncate-toward-zero) would otherwise
    /// map both x=-1 and x=1 to cell 0 for cellSize=10, opening a gap at the
    /// origin. A position exactly on a cell boundary (e.g. x=20 with
    /// cellSize=10) always resolves to the cell whose range *starts* at
    /// that boundary (cell 2, covering [20, 30)), not the cell that ends
    /// there (cell 1, covering [10, 20)) - this is deterministic regardless
    /// of how the position was reached, since it depends only on the input
    /// value, not on prior calls.
    /// </para>
    /// <para>
    /// <b>Sampling idempotency rule.</b> <see cref="RecordSample"/> is
    /// idempotent per exact integer block position: the same (x, y, z)
    /// reported more than once (whether twice in the same tick or across
    /// many ticks as the drone revisits ground it has already covered)
    /// affects the accumulated per-cell counts only once. This was chosen
    /// over "every call counts, double-sampling is a caller concern"
    /// because a roaming drone naturally revisits the same ground blocks
    /// repeatedly as it wanders a district - per-call counting would let
    /// dwell time silently inflate both a cell's coverage and its ore
    /// ratio's apparent confidence without saying anything new about the
    /// world. Enforced via an internal set of every distinct block position
    /// already recorded.
    /// </para>
    /// </remarks>
    public sealed class SurveyGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<SurveyCell, CellData> _cells = new Dictionary<SurveyCell, CellData>();
        private readonly HashSet<(int X, int Y, int Z)> _sampledBlocks = new HashSet<(int, int, int)>();

        public SurveyGrid(float cellSize)
        {
            if (cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize must be positive.");

            _cellSize = cellSize;
        }

        /// <summary>Maps a world block (x, z) column to its <see cref="SurveyCell"/>. See class remarks for the boundary convention.</summary>
        public SurveyCell CellAt(int x, int z) => new SurveyCell(FloorDiv(x, _cellSize), FloorDiv(z, _cellSize));

        /// <summary>
        /// Records one sampled block at (x, y, z), attributing it to the
        /// cell its (x, z) column maps to. <paramref name="oreType"/> null
        /// or empty means the block was sampled but is not an ore block -
        /// it still counts toward the cell's sampled-total (coverage) but
        /// not toward any ore's count. See class remarks for the
        /// idempotency rule governing repeated calls with the same (x, y,
        /// z).
        /// </summary>
        public void RecordSample(int x, int y, int z, string oreType)
        {
            if (!_sampledBlocks.Add((x, y, z)))
                return;

            var cell = CellAt(x, z);
            if (!_cells.TryGetValue(cell, out var data))
            {
                data = new CellData();
                _cells[cell] = data;
            }

            data.SampledCount++;
            if (!string.IsNullOrEmpty(oreType))
            {
                data.OreCounts.TryGetValue(oreType, out var existing);
                data.OreCounts[oreType] = existing + 1;
            }
        }

        /// <summary>
        /// Finds the cell with the highest observed density
        /// (ore-count / sampled-count ratio, per KTD5 - NOT the highest raw
        /// ore count) for <paramref name="oreType"/>, considering only
        /// cells that have recorded at least one sample of that ore.
        /// Returns a not-found result (see <see cref="DensestCellResult.Found"/>)
        /// when no sample of this ore type has been recorded anywhere yet -
        /// never a default/zeroed cell that could be mistaken for a real
        /// answer.
        /// </summary>
        public DensestCellResult DensestCell(string oreType)
        {
            bool found = false;
            SurveyCell bestCell = default;
            int bestOreCount = 0;
            int bestSampledCount = 0;
            float bestRatio = 0f;

            foreach (var pair in _cells)
            {
                var data = pair.Value;
                if (!data.OreCounts.TryGetValue(oreType, out var oreCount) || oreCount == 0)
                    continue;

                float ratio = (float)oreCount / data.SampledCount;
                if (!found || ratio > bestRatio)
                {
                    found = true;
                    bestCell = pair.Key;
                    bestOreCount = oreCount;
                    bestSampledCount = data.SampledCount;
                    bestRatio = ratio;
                }
            }

            return found ? DensestCellResult.Success(bestCell, bestOreCount, bestSampledCount) : DensestCellResult.NotFound;
        }

        private static int FloorDiv(int value, float size) => (int)Math.Floor(value / size);

        private sealed class CellData
        {
            public int SampledCount;
            public readonly Dictionary<string, int> OreCounts = new Dictionary<string, int>();
        }
    }
}
