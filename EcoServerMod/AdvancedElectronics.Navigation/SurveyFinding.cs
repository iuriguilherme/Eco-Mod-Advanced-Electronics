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
    /// The standardized, machine-readable finding for one ore type in one survey
    /// area (R5/R6): what was found, the precise block to dig, how deep, and how
    /// concentrated. The dock's Survey Results tab renders these for the player
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

        private SurveyFinding(bool found, int areaId, string oreType, int count, BlockPos position, int depthBelowSurface, int depthMax, float concentration)
        {
            Found = found;
            AreaId = areaId;
            OreType = oreType;
            Count = count;
            Position = position;
            DepthBelowSurface = depthBelowSurface;
            DepthMax = depthMax;
            Concentration = concentration;
        }

        public static SurveyFinding NotFound { get; } = new SurveyFinding(false, 0, null, 0, default, 0, 0, 0f);

        public static SurveyFinding Create(int areaId, string oreType, int count, BlockPos position, int depthBelowSurface, int depthMax, float concentration) =>
            new SurveyFinding(true, areaId, oreType, count, position, depthBelowSurface, depthMax, concentration);

        public bool Equals(SurveyFinding other) =>
            Found == other.Found &&
            AreaId == other.AreaId &&
            OreType == other.OreType &&
            Count == other.Count &&
            Position.Equals(other.Position) &&
            DepthBelowSurface == other.DepthBelowSurface &&
            DepthMax == other.DepthMax &&
            Concentration.Equals(other.Concentration);

        public override bool Equals(object obj) => obj is SurveyFinding other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Found, AreaId, OreType, Count, Position, DepthBelowSurface, DepthMax, Concentration);
    }
}
