using AdvancedElectronics.Navigation;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;
using Eco.Shared.Math;
using Eco.World;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Live-Eco-world-backed implementation of the Navigation library's
    /// <see cref="IWorldSampler"/> (U3), letting <see cref="GridPathfinder"/>
    /// query real terrain/obstacle data instead of the test suite's in-memory
    /// fake (<c>GridPathfinderTests.FakeWorldSampler</c>). This class has no
    /// pathfinding logic of its own -- it is purely a translation layer from
    /// (x, z) grid columns to Eco world-query calls.
    ///
    /// APIs below were found via a reflection dump against
    /// Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024 (MetadataLoadContext
    /// over the restored NuGet package DLLs -- same technique the movement and
    /// district-read best-practice docs used). The reference assemblies ship
    /// with method BODIES stripped, so exact runtime semantics of the calls
    /// below (not just their signatures) could not be executed/confirmed
    /// offline. Every place that rests on such an unverified semantic is
    /// flagged ASSUMPTION, matching the pattern already established in
    /// DroneDockObject.cs -- these are expected to need live-server confirmation,
    /// not blockers to shipping this unit.
    /// </summary>
    public sealed class EcoWorldSampler : IWorldSampler
    {
        // Finding (confirmed by the real compiler, not just reflection): unlike
        // Eco.Simulation.EcoSim / PathManager / WaterManager (which are
        // Singleton<T>/AutoSingleton<T> instances accessed via ".Obj"), World's
        // block-query surface is exposed as STATIC members directly on the
        // World class itself -- no instance/singleton accessor is needed at
        // all. (A reflection-only dump against the stripped reference assembly
        // could not distinguish static from instance methods reliably; trying
        // to call these through a guessed "World.Obj" singleton instance
        // failed to compile with CS0176 "cannot be accessed with an instance
        // reference; qualify it with a type name instead", which is what
        // revealed this.)

        /// <summary>
        /// The cell a ground entity would stand in for this column: the block directly
        /// above the top solid block. Mirrors the engine's own convention (its walkable
        /// node is "the first empty block above a solid block").
        /// </summary>
        private static Vector3i StandCell(int x, int z) =>
            new Vector3i(x, EcoWorld.GetTopSolidBlockY(new Vector2i(x, z)) + 1, z);

        /// <summary>
        /// True when terrain geometry blocks this column. Mirrors the engine's own
        /// walkability predicate (Eco.Simulation's pathfinding: "walkable blocks are the
        /// first empty block above a solid block, where there are two empty blocks above
        /// it (or plants)"), which tests <c>Is&lt;Solid&gt;()</c>/<c>Is&lt;Occupied&gt;()</c>
        /// rather than "is not Empty".
        ///
        /// That distinction was the bug behind the drone never moving: an earlier version
        /// asked <c>!above.Is&lt;Empty&gt;()</c>, so any grass or plant block above the
        /// ground read as solid. Eco worlds are carpeted in vegetation, so effectively
        /// every column was unwalkable and pathfinding failed at any distance, leaving
        /// the drone permanently Unreachable. Plants are walkable to the engine, and now
        /// to us.
        ///
        /// Object occupancy is deliberately NOT considered here -- it belongs to
        /// <see cref="IsObstacleAt"/> so the pathfinder's endpoint exemption can let the
        /// drone leave its own column and enter the dock's.
        /// </summary>
        public bool IsSolidAt(int x, int z)
        {
            var stand = StandCell(x, z);

            var block = EcoWorld.GetBlock(stand);
            if (block != null && block.Is<Solid>())
                return true;                                   // no room to stand

            var under = EcoWorld.GetBlock(stand + Vector3i.Down);
            if (under == null || (!under.Is<Solid>() && !under.Is<UnderWater>()))
                return true;                                   // nothing to stand on

            var over = EcoWorld.GetBlock(stand + Vector3i.Up);
            if (over != null && over.Is<Solid>())
                return true;                                   // no headroom

            // Deliberate divergence from the animal predicate, which also rejects
            // Constructed blocks (animals stay off player-built floors). A drone is a
            // machine and should be able to cross roads and floors -- and a dock placed
            // on one must not strand it.
            return false;
        }

        public float GroundHeightAt(int x, int z)
        {
            // ASSUMPTION -- verify against a live server: of World's several
            // "top of column" queries (GetTopBlockY, GetTopEmptyBlock,
            // GetTopSolidBlockY, GetTopSolidBlockYRaw, GetTopPathPos, ...),
            // GetTopSolidBlockY(Vector2i) was picked as the one whose name most
            // directly matches "ground height" for a walking entity. Its exact
            // water/cliff/overhang edge-case behavior could not be confirmed
            // (method body stripped in the reference assembly).
            return EcoWorld.GetTopSolidBlockY(new Vector2i(x, z));
        }

        /// <summary>
        /// True when a placed WorldObject occupies this column (R2, "distinct from
        /// natural terrain solidity").
        ///
        /// Read from the world's own block data rather than a spatial query: placing a
        /// WorldObject writes <c>WorldObjectBlock</c>s into its occupancy footprint, and
        /// those blocks carry the <c>Occupied</c> attribute -- the same attribute the
        /// engine's pathfinding rejects. That makes this exact and cheap (two block
        /// lookups), replacing an earlier <c>GetObjectsWithin</c> probe whose radius was
        /// an admitted guess and which ran a spatial query per column visited by A*.
        /// </summary>
        public bool IsObstacleAt(int x, int z)
        {
            var stand = StandCell(x, z);

            var block = EcoWorld.GetBlock(stand);
            if (block != null && block.Is<Occupied>())
                return true;

            // A multi-block object (or one whose footprint starts a block higher) still
            // blocks passage through the column.
            var over = EcoWorld.GetBlock(stand + Vector3i.Up);
            return over != null && over.Is<Occupied>();
        }
    }
}
