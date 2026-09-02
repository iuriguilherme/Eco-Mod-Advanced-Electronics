using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// A precise world block position (x, y, z). The dig target a finding points
    /// at — finer than a plot (KTD2), unlike the old model which reported only a
    /// cell coordinate. Eco-free so it can live in the Navigation project.
    /// </summary>
    public readonly struct BlockPos : IEquatable<BlockPos>
    {
        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public BlockPos(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(BlockPos other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is BlockPos other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>
    /// The shape version of an area's persisted findings (KTD1). Before U1 findings were stored
    /// as one row per ore for the whole area; they are now one row per (plot, ore), and an
    /// existing save's rows are DISCARDED rather than migrated — there is nothing to attribute
    /// an area total to. Detection is this explicit marker, never an absent plot: the plot is
    /// two plain ints on a flat serialized class, so a pre-U1 row loads as plot (0,0), and plot
    /// coordinates are absolute — (0,0) is a real plot any area near the world origin contains.
    /// </summary>
    public static class FindingsVersion
    {
        /// <summary>The version rows are written at today. Bump when the row shape changes again.</summary>
        public const int Current = 1;

        /// <summary>
        /// True when findings stamped <paramref name="storedVersion"/> predate the current row
        /// shape and must be discarded. An unstamped save reads 0 and is stale; a version at or
        /// past <see cref="Current"/> is kept, so a downgrade never destroys newer data.
        /// </summary>
        public static bool IsStale(int storedVersion) => storedVersion < Current;
    }

    /// <summary>
    /// The standardized, machine-readable finding for one ore type — either in one
    /// plot of a survey area (a per-plot row, KTD1) or across the area as a whole
    /// (an area total re-derived from those rows), told apart by
    /// <see cref="HasPlot"/>. Carries what was found, the precise block to dig, how
    /// deep, and how concentrated (R5/R6). The dock's Survey Results tab renders these
    /// (R7) and a future mining drone consumes the same shape (R6). Uses the
    /// <see cref="Found"/>-flag convention (mirrors the old
    /// <c>DensestCellResult</c> and <c>GridPathfinder.PathResult</c>): "no data
    /// for this ore in this area yet" is a normal outcome, not a zeroed struct a
    /// caller might mistake for a real (0,0,0) finding.
    /// </summary>
    public readonly struct SurveyFinding : IEquatable<SurveyFinding>
    {
        public bool Found { get; }

        /// <summary>Which survey area this finding belongs to (R3a attribution).</summary>
        public int AreaId { get; }

        /// <summary>
        /// True when this finding describes ONE plot (a per-plot row, KTD1); false when it is an
        /// area total re-derived from those rows, which belongs to no single plot. Carried as an
        /// explicit flag for the same reason <see cref="Found"/> is: plot (0,0) is a real plot
        /// any area near the world origin contains, so a zeroed <see cref="Plot"/> can never
        /// stand in for "this finding has no plot".
        /// </summary>
        public bool HasPlot { get; }

        /// <summary>
        /// The plot this row's counts were accumulated in (R16/R20 need findings attributable to
        /// a single plot so one plot's can be invalidated without touching the rest). Meaningful
        /// only when <see cref="HasPlot"/> is true.
        /// </summary>
        public PlotCoord Plot { get; }

        /// <summary>The material type name (an ore, a rock, sulfur, sand, ...). Field name is historical.</summary>
        public string OreType { get; }

        /// <summary>Total blocks of this material found in the area — the quantity headline (KTD2).</summary>
        public int Count { get; }

        /// <summary>The precise block to dig: the shallowest observed occurrence of this material in the area.</summary>
        public BlockPos Position { get; }

        /// <summary>Blocks below the surface of <see cref="Position"/> — the shallowest depth (== depth-range minimum).</summary>
        public int DepthBelowSurface { get; }

        /// <summary>Deepest observed occurrence, in blocks below surface — the depth-range maximum.</summary>
        public int DepthMax { get; }

        /// <summary>
        /// Material-blocks / sampled-blocks in the area (secondary, ore-oriented signal). Demoted from
        /// the readout headline in favour of <see cref="Count"/> (KTD2/R3); kept for callers that want it.
        /// </summary>
        public float Concentration { get; }

        private SurveyFinding(bool found, int areaId, bool hasPlot, PlotCoord plot, string oreType, int count, BlockPos position, int depthBelowSurface, int depthMax, float concentration)
        {
            Found = found;
            AreaId = areaId;
            HasPlot = hasPlot;
            Plot = plot;
            OreType = oreType;
            Count = count;
            Position = position;
            DepthBelowSurface = depthBelowSurface;
            DepthMax = depthMax;
            Concentration = concentration;
        }

        public static SurveyFinding NotFound { get; } = new SurveyFinding(false, 0, false, default, null, 0, default, 0, 0, 0f);

        /// <summary>An area-total finding: this ore across the whole area, belonging to no single plot.</summary>
        public static SurveyFinding Create(int areaId, string oreType, int count, BlockPos position, int depthBelowSurface, int depthMax, float concentration) =>
            new SurveyFinding(true, areaId, false, default, oreType, count, position, depthBelowSurface, depthMax, concentration);

        /// <summary>One per-plot row (KTD1): this ore's counts within <paramref name="plot"/> alone.</summary>
        public static SurveyFinding CreateInPlot(int areaId, PlotCoord plot, string oreType, int count, BlockPos position, int depthBelowSurface, int depthMax, float concentration) =>
            new SurveyFinding(true, areaId, true, plot, oreType, count, position, depthBelowSurface, depthMax, concentration);

        public bool Equals(SurveyFinding other) =>
            Found == other.Found &&
            AreaId == other.AreaId &&
            HasPlot == other.HasPlot &&
            Plot.Equals(other.Plot) &&
            OreType == other.OreType &&
            Count == other.Count &&
            Position.Equals(other.Position) &&
            DepthBelowSurface == other.DepthBelowSurface &&
            DepthMax == other.DepthMax &&
            Concentration.Equals(other.Concentration);

        public override bool Equals(object obj) => obj is SurveyFinding other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                HashCode.Combine(Found, AreaId, HasPlot, Plot),
                OreType, Count, Position, DepthBelowSurface, DepthMax, Concentration);
    }
}
