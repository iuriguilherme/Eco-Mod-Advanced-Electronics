using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// One column of the area and the height of its topmost block, sampled by the level
    /// pass itself (R19). The per-area median height a survey writes is no use here: it
    /// is written only by a survey pass and zeroed when findings are cleared, and a farm
    /// area is never surveyed.
    /// </summary>
    public readonly struct SurfaceColumn : IEquatable<SurfaceColumn>
    {
        public int X { get; }

        public int Z { get; }

        /// <summary>Y of this column's topmost block.</summary>
        public int SurfaceY { get; }

        public SurfaceColumn(int x, int z, int surfaceY)
        {
            X = x;
            Z = z;
            SurfaceY = surfaceY;
        }

        public bool Equals(SurfaceColumn other) => X == other.X && Z == other.Z && SurfaceY == other.SurfaceY;

        public override bool Equals(object obj) => obj is SurfaceColumn other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z, SurfaceY);

        public override string ToString() => $"({X}, {Z}) top {SurfaceY}";
    }

    /// <summary>One column's worth of blocks standing above the target, top block first.</summary>
    public readonly struct LevelRemoval
    {
        public int X { get; }

        public int Z { get; }

        /// <summary>Every block to take out, highest first.</summary>
        public IReadOnlyList<BlockPos> Positions { get; }

        public int BlockCount => Positions.Count;

        internal LevelRemoval(int x, int z, IReadOnlyList<BlockPos> positions)
        {
            X = x;
            Z = z;
            Positions = positions;
        }
    }

    /// <summary>One column's worth of blocks to place to reach the target, lowest first.</summary>
    public readonly struct LevelFill
    {
        public int X { get; }

        public int Z { get; }

        /// <summary>Every block to place, lowest first.</summary>
        public IReadOnlyList<BlockPos> Positions { get; }

        public int BlockCount => Positions.Count;

        internal LevelFill(int x, int z, IReadOnlyList<BlockPos> positions)
        {
            X = x;
            Z = z;
            Positions = positions;
        }
    }

    /// <summary>
    /// The fill half of a level pass, reachable only through
    /// <see cref="LevelPlan.BeginFill"/> once every removal is accounted for. Splitting
    /// the material two ways is the whole point: spoil the drone banked in linked storage
    /// when its hold filled is still its own material, and new dirt is drawn only for the
    /// shortfall (R20).
    /// </summary>
    public sealed class LevelFillPhase
    {
        public IReadOnlyList<LevelFill> Fills { get; }

        /// <summary>Blocks the fill takes from spoil the pass already removed.</summary>
        public int FromBankedSpoil { get; }

        /// <summary>Blocks the fill must draw fresh from linked storage.</summary>
        public int NewDirtRequired { get; }

        internal LevelFillPhase(IReadOnlyList<LevelFill> fills, int demand, int bankedSpoil)
        {
            Fills = fills;
            FromBankedSpoil = Math.Min(demand, bankedSpoil);
            NewDirtRequired = demand - FromBankedSpoil;
        }
    }

    /// <summary>
    /// A level pass over one area: the median target of R19, the blocks above it, and the
    /// columns below it (R20). Eco-free (KTD6) -- the caller samples each column's surface
    /// through its own seam and this type does the coordinate arithmetic, so no driver
    /// works out block positions for itself.
    /// </summary>
    /// <remarks>
    /// R20's ordering is structural rather than a discipline the driver is trusted to
    /// keep: <see cref="Removals"/> is a plain property, and there is no way to reach a
    /// fill list except <see cref="BeginFill"/>, which refuses until the whole removal
    /// volume is reported done. A driver cannot fill early by forgetting to check.
    /// </remarks>
    public sealed class LevelPlan
    {
        /// <summary>The height every column is brought to: the area's own median surface height (R19).</summary>
        public int TargetHeight { get; }

        /// <summary>Every column standing above the target, in the order its samples arrived.</summary>
        public IReadOnlyList<LevelRemoval> Removals { get; }

        private readonly IReadOnlyList<LevelFill> fills;

        /// <summary>Total blocks to take out before any filling may start.</summary>
        public int RemovalVolume { get; }

        /// <summary>Total blocks to place once removal is done.</summary>
        public int FillDemand { get; }

        private LevelPlan(
            int targetHeight,
            IReadOnlyList<LevelRemoval> removals,
            IReadOnlyList<LevelFill> fills)
        {
            TargetHeight = targetHeight;
            Removals = removals;
            this.fills = fills;
            RemovalVolume = removals.Sum(r => r.BlockCount);
            FillDemand = fills.Sum(f => f.BlockCount);
        }

        /// <summary>
        /// Builds the pass from the area's sampled columns.
        ///
        /// For an even column count the target is the LOWER of the two middle heights.
        /// The choice is deliberate: the lower target turns the difference between them
        /// into spoil the drone removes and already holds, rather than into dirt it has to
        /// draw from linked storage.
        /// </summary>
        public static LevelPlan Build(IEnumerable<SurfaceColumn> columns)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var sampled = columns.ToList();
            if (sampled.Count == 0)
                throw new ArgumentException("An area with no sampled columns has no median to level to.", nameof(columns));

            var seen = new HashSet<(int, int)>();
            foreach (var column in sampled)
            {
                if (!seen.Add((column.X, column.Z)))
                    throw new ArgumentException(
                        $"Column ({column.X}, {column.Z}) was sampled more than once; a repeat skews the median and plans its blocks twice.",
                        nameof(columns));
            }

            var heights = sampled.Select(c => c.SurfaceY).OrderBy(y => y).ToList();
            var target = heights[(heights.Count - 1) / 2];

            var removals = new List<LevelRemoval>();
            var fills = new List<LevelFill>();

            foreach (var column in sampled)
            {
                if (column.SurfaceY > target)
                {
                    var positions = new List<BlockPos>();
                    for (var y = column.SurfaceY; y > target; y--)
                        positions.Add(new BlockPos(column.X, y, column.Z));
                    removals.Add(new LevelRemoval(column.X, column.Z, positions));
                }
                else if (column.SurfaceY < target)
                {
                    var positions = new List<BlockPos>();
                    for (var y = column.SurfaceY + 1; y <= target; y++)
                        positions.Add(new BlockPos(column.X, y, column.Z));
                    fills.Add(new LevelFill(column.X, column.Z, positions));
                }
            }

            return new LevelPlan(target, removals, fills);
        }

        /// <summary>
        /// Opens the fill half, once <paramref name="removalBlocksCompleted"/> accounts for
        /// the whole <see cref="RemovalVolume"/> (R20).
        /// </summary>
        /// <param name="bankedSpoil">
        /// Blocks of the pass's own spoil available to spend -- what the drone still holds
        /// plus what it unloaded to linked storage mid-pass. Spent before new dirt is
        /// requested (AE6).
        /// </param>
        /// <exception cref="InvalidOperationException">Removal is not finished.</exception>
        public LevelFillPhase BeginFill(int removalBlocksCompleted, int bankedSpoil)
        {
            if (bankedSpoil < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(bankedSpoil), bankedSpoil, "Banked spoil is never negative.");

            if (removalBlocksCompleted < RemovalVolume)
                throw new InvalidOperationException(
                    $"The level pass fills only after it has removed: {removalBlocksCompleted} of {RemovalVolume} blocks are out (R20).");

            return new LevelFillPhase(this.fills, FillDemand, bankedSpoil);
        }
    }
}
