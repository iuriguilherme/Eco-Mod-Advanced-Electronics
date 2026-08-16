using AdvancedElectronics.Navigation;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Objects;
using Eco.Shared.Math;
using Eco.Simulation.Agents;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Live-Eco-world-backed implementation of <see cref="IBlockClassifier"/> (U3),
    /// evaluating R14's predicate in the engine's own precedence order -- the same
    /// order <c>AtomicActions.DeleteBlock</c> uses, confirmed by reading it during
    /// planning (Engine Reference). Every exclusion is checked before the minable/
    /// excavatable test, so a form-bearing block built from minable stone (AE5) never
    /// reaches it.
    ///
    /// Never unit-tested (every collaborator is an Eco type) -- exercised only by the
    /// batched live pass, per U3's own Verification line.
    /// </summary>
    public sealed class EcoBlockClassifier : IBlockClassifier
    {
        public BlockClassification Classify(int x, int y, int z)
        {
            if (!WrappedWorldPosition3i.TryCreate(new Vector3i(x, y, z), out var position))
                return BlockClassification.NotRemovable;

            var block = EcoWorld.GetBlock(position);
            if (block == null)
                return BlockClassification.NotRemovable;

            // Ramp: special-cased multiblock pickup by the engine's own helper: not a
            // plain dig-or-mine target.
            if (block.Get<Ramp>() != null)
                return BlockClassification.NotRemovable;

            // Empty space and any part of a placed WorldObject's footprint: nothing to
            // remove, or not this mod's to remove.
            if (block.Is<Empty>() || block is WorldObjectBlock || block is WorldObjectManyBlock)
                return BlockClassification.NotRemovable;

            // Contained inside another world object (e.g. a block inside a foundation).
            if (BlockContainerManager.Obj.IsBlockContained(position))
                return BlockClassification.NotRemovable;

            // Blocked by tree roots.
            if (Tree.TreeRootsBlockDigging(position))
                return BlockClassification.NotRemovable;

            // Form-bearing: this is a constructed/deconstructable block (e.g. a wall
            // built from minable stone). Form-bearing wins over minable, which is what
            // keeps AE5's wall standing (R14).
            if (BlockFormManager.HasForms(block.GetType()))
                return BlockClassification.NotRemovable;

            // Tree debris: cleaned up by the axe's own action, not dig-or-mine.
            if (block.Is<TreeDebris>())
                return BlockClassification.NotRemovable;

            if (block.Is<Minable>())
                return BlockClassification.Minable;
            if (block.Is<Diggable>())
                return BlockClassification.Excavatable;

            return BlockClassification.NotRemovable;
        }
    }
}
