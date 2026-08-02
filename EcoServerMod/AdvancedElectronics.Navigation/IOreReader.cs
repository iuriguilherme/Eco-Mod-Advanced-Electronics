namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Read-only view of ore-bearing blocks that <see cref="SurveyRecord"/> (via
    /// the Eco-side ore-sensing component) queries. Positions are the same
    /// plain integer block coordinates <see cref="IWorldSampler"/> uses -
    /// this project has zero dependency on any Eco.* namespace, so a real
    /// mod implements this against live Eco block data (see
    /// EcoServerMod/AdvancedElectronics/EcoOreReader.cs) while tests use a
    /// simple in-memory fake.
    ///
    /// Kept as a separate interface from <see cref="IWorldSampler"/> rather
    /// than folded into it: terrain solidity/height (pathfinding's concern)
    /// and ore-block identity (survey's concern) are unrelated queries that
    /// happen to both read "the block at this position" - splitting them
    /// keeps each interface narrow and lets a caller depend on only the one
    /// it needs.
    /// </summary>
    public interface IOreReader
    {
        /// <summary>
        /// True if the block at (x, y, z) is a raw, in-ground ore-bearing
        /// block, with <paramref name="oreType"/> set to a stable
        /// identifier for that ore (e.g. "IronOre"). False (and
        /// <paramref name="oreType"/> null) for any non-ore block - this is
        /// the expected, common case for most sampled blocks, not an error
        /// path.
        /// </summary>
        bool TryGetOreType(int x, int y, int z, out string oreType);
    }
}
