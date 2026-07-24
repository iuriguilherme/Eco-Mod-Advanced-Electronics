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

        public string OreType { get; }

        /// <summary>The precise block to dig: the shallowest observed occurrence of this ore in the densest plot.</summary>
        public BlockPos Position { get; }

        /// <summary>Blocks below the surface of <see cref="Position"/> — the dig-effort signal (R5).</summary>
        public int DepthBelowSurface { get; }

        /// <summary>
        /// Ore-blocks / sampled-blocks in the plot this finding was drawn from
        /// (R5 concentration). Aggregated per plot rather than per block because a
        /// single block is either ore or not — concentration needs a window. The
        /// window is one plot; the <see cref="Position"/> stays block-precise.
        /// </summary>
        public float Concentration { get; }

        private SurveyFinding(bool found, int areaId, string oreType, BlockPos position, int depthBelowSurface, float concentration)
        {
            Found = found;
            AreaId = areaId;
            OreType = oreType;
            Position = position;
            DepthBelowSurface = depthBelowSurface;
            Concentration = concentration;
        }

        public static SurveyFinding NotFound { get; } = new SurveyFinding(false, 0, null, default, 0, 0f);

        public static SurveyFinding Create(int areaId, string oreType, BlockPos position, int depthBelowSurface, float concentration) =>
            new SurveyFinding(true, areaId, oreType, position, depthBelowSurface, concentration);

        public bool Equals(SurveyFinding other) =>
            Found == other.Found &&
            AreaId == other.AreaId &&
            OreType == other.OreType &&
            Position.Equals(other.Position) &&
            DepthBelowSurface == other.DepthBelowSurface &&
            Concentration.Equals(other.Concentration);

        public override bool Equals(object obj) => obj is SurveyFinding other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Found, AreaId, OreType, Position, DepthBelowSurface, Concentration);
    }
}
