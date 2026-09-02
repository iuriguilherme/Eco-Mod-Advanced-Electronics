using AdvancedElectronics.Navigation;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Plants;
using Eco.Shared.Math;
using Eco.Simulation.Agents;
using Eco.World;
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
    public sealed class EcoBlockClassifier : IBlockClassifier, IColumnProbe
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

        /// <summary>
        /// The <see cref="IColumnProbe"/> half (U4): what one position IS, so
        /// <see cref="BedrockWalk"/> can find the ground under a capped or dug-out column.
        ///
        /// This is a DIFFERENT question from <see cref="Classify"/> and cannot be answered
        /// from it. <see cref="Classify"/> returns <see cref="BlockClassification.NotRemovable"/>
        /// for empty space, a built wall, a world object's footprint and the world floor
        /// alike, so "not removable" says nothing about bedrock. The world floor is identified
        /// here by the engine's own <c>Impenetrable</c> block attribute — the same test
        /// vanilla's <c>DrillItem.ProspectBlock</c> makes, paired with the same world-floor
        /// bound — rather than by matching a block type name, so a modded or renamed floor
        /// block still reads correctly.
        /// </summary>
        public ColumnBlock ProbeColumn(int x, int y, int z)
        {
            // An unreadable position (outside the world, or an unloaded chunk) is reported as
            // ground rather than as a void. Every ambiguous answer must stop the walk WITHOUT
            // claiming bedrock, because a column that cannot be proven at bedrock must not let
            // an area read [cleared].
            if (!WrappedWorldPosition3i.TryCreate(new Vector3i(x, y, z), out var position))
                return ColumnBlock.Terrain;

            var block = EcoWorld.GetBlock(position);
            if (block == null)
                return ColumnBlock.Terrain;

            // The world floor. Tested first: the generator pads every column down to it with
            // ImpenetrableStoneBlock, so this is the one affirmative answer the walk wants and
            // nothing else may shadow it.
            if (block.Is<Impenetrable>())
                return ColumnBlock.Impenetrable;

            // Nothing there. Water counts as nothing: it is what floods a shaft the drone dug
            // out, and it is not ground standing above the floor.
            if (block.Is<Empty>() || block is IWaterBlock || block.Is<UnderWater>())
                return ColumnBlock.Empty;

            // Put there rather than grown or generated — R7's "obstructs one column, not the
            // ground". Same set the mining pass refuses to remove and steps past, so the walk
            // and the digging agree about what a column contains.
            if (block is WorldObjectBlock || block is WorldObjectManyBlock)
                return ColumnBlock.Built;
            if (block.Get<Ramp>() != null)
                return ColumnBlock.Built;
            if (BlockContainerManager.Obj.IsBlockContained(position))
                return ColumnBlock.Built;
            if (block.Is<Constructed>() || BlockFormManager.HasForms(block.GetType()))
                return ColumnBlock.Built;
            if (block.Is<TreeDebris>() || block is PlantBlock)
                return ColumnBlock.Built;

            // Anything left is natural terrain above the floor: dirt (including dirt holding a
            // plant, which is ground, not an obstruction), sand, stone, ore.
            return ColumnBlock.Terrain;
        }
    }
}
