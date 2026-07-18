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
    /// DroneDock.cs -- these are expected to need live-server confirmation,
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

        public bool IsSolidAt(int x, int z)
        {
            var groundY = EcoWorld.GetTopSolidBlockY(new Vector2i(x, z));
            var above = EcoWorld.GetBlock(new Vector3i(x, groundY + 1, z));

            // ASSUMPTION -- verify against a live server: IsSolidAt (R2, "the
            // terrain/block at this column is solid and blocks passage") is
            // interpreted here as "the space immediately above the ground
            // surface is not open air". The ground-surface block itself is
            // expected to be solid by definition (that's what a ground drone
            // stands on); it is the space *above* the surface that must be
            // clear for the drone to occupy that column. Block.Is<T>() checks
            // for a BlockAttribute-derived marker (Eco.World.Blocks.Empty here)
            // on the block's declared type -- confirmed to exist by signature
            // (Block.Is<T>(), the Empty/Solid/Impenetrable BlockAttribute
            // hierarchy), but its body is stripped in the reference assembly so
            // the exact pass/fail boundary could not be executed offline.
            return above == null || !above.Is<Empty>();
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

        public bool IsObstacleAt(int x, int z)
        {
            // ASSUMPTION -- verify against a live server: player-placed
            // obstacles (R2, "distinct from natural terrain solidity") are
            // detected via WorldObjectManager.GetObjectsWithin, the same
            // ServiceHolder<IWorldObjectManager> access pattern already proven
            // live in SpikeMoveCommand.cs (ForceAdd/DestroyPermanently). The
            // query radius (roughly "one grid column") is a guess needing live
            // tuning -- too small and a WorldObject straddling the column
            // center is missed, too large and neighboring columns falsely
            // report obstructed.
            const float ColumnObstacleRadius = 0.75f;
            var center = new Vector2(x, z);
            var manager = ServiceHolder<IWorldObjectManager>.Obj;

            foreach (var _ in manager.GetObjectsWithin(center, ColumnObstacleRadius))
                return true;
            return false;
        }
    }
}
