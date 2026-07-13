namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Read-only view of world terrain/obstacles that <see cref="GridPathfinder"/>
    /// queries. Positions are plain grid columns identified by integer
    /// (x, z) coordinates - this project has zero dependency on any Eco.*
    /// namespace, so a real mod implements this against live Eco world data
    /// (blocks, WorldObjects) while tests use a simple in-memory fake.
    /// </summary>
    public interface IWorldSampler
    {
        /// <summary>
        /// True if the terrain/block at this (x, z) column is solid and
        /// blocks passage. Independent of player-placed obstacles - see
        /// <see cref="IsObstacleAt"/>.
        /// </summary>
        bool IsSolidAt(int x, int z);

        /// <summary>
        /// Ground height (world Y) at this (x, z) column. Used both to place
        /// waypoints at the correct elevation and to evaluate step height
        /// between adjacent columns.
        /// </summary>
        float GroundHeightAt(int x, int z);

        /// <summary>
        /// True if a player-placed obstacle occupies this (x, z) column.
        /// Distinct from natural terrain solidity (<see cref="IsSolidAt"/>)
        /// - the pathfinder must route around both identically.
        /// </summary>
        bool IsObstacleAt(int x, int z);
    }
}
