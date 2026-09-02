using System;
using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Civics.Laws;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Math;
using Eco.Shared.Settings;
using Eco.Simulation;
using Eco.Simulation.WorldLayers;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    public enum PlacementOutcome
    {
        Succeeded,
        Refused
    }

    /// <summary>One placement attempt: succeeded, or refused with the stage that refused it and the engine's own message.</summary>
    public sealed class PlacementResult
    {
        public PlacementOutcome Outcome { get; }
        public RemovalRefusalStage RefusalStage { get; }
        public string Message { get; }

        private PlacementResult(PlacementOutcome outcome, RemovalRefusalStage stage, string message)
        {
            this.Outcome = outcome;
            this.RefusalStage = stage;
            this.Message = message;
        }

        public static PlacementResult Success() => new(PlacementOutcome.Succeeded, default, null);

        public static PlacementResult Refusal(RemovalRefusalStage stage, string message) =>
            new(PlacementOutcome.Refused, stage, message);
    }

    /// <summary>
    /// Puts blocks into the world as the stamped citizen, drawing each one from a source
    /// inventory (U6, R14's place-dirt rule and R20's fill phase).
    ///
    /// This is the verb the mod has never performed. Every world write it has done until
    /// now has been a deletion, so the direction is new even though the pack machinery is
    /// not: <see cref="MiningRemovalService"/> already constructs a pickup action inside
    /// its pack, and this is that action's other value.
    ///
    /// Never unit-tested -- every collaborator is an Eco type. Proven by the checklist
    /// below plus the live pass, exactly as the removal service is. Static-review
    /// checklist -- confirm each by reading this file:
    /// 1. The action type is the engine's own <see cref="DropOrPickupBlock"/>, never derived.
    /// 2. The citizen comes from the direct <c>Fill(User, ...)</c> overload, never the
    ///    <c>MultiblockActionContext</c> overload.
    /// 3. No access argument is assigned (<c>Fill</c> is always called with <c>access: null</c>).
    /// 4. Pack flags are unset (assertion below).
    /// 5. No action waives authorization (assertion below).
    /// 6. The pack is performed via <c>TryPerform</c>, never dry-run and never forced.
    /// 7. Every position gets exactly one drop action (assertion below).
    /// 8. The citizen assertion runs before performing and refuses on failure.
    /// 9. The world write is a post-effect, so a refused pack sets no block.
    /// 10. The item leaves the inventory through the pack's own change set, so a refused
    ///     pack consumes no dirt.
    /// </summary>
    public sealed class BlockPlacementService
    {
        /// <summary>
        /// Places <paramref name="blockType"/> at every position, drawing one
        /// <paramref name="blockItemType"/> from <paramref name="source"/> per block.
        /// </summary>
        /// <param name="tool">
        /// Named on the action so a settlement can regulate the drone's placement.
        /// <see cref="DropOrPickupBlock"/> has no tool field of its own -- it derives from
        /// <c>ItemInteractAction</c> -- so what a law's picker offers for this action is
        /// the block item, not the arm. The tool is still passed because
        /// <c>Fill</c> takes it and a future action type may read it.
        /// </param>
        public PlacementResult Place(
            IReadOnlyList<BlockPos> positions,
            Type blockType,
            Type blockItemType,
            User stampedCitizen,
            Item tool,
            Inventory source)
        {
            if (positions == null || positions.Count == 0)
                return PlacementResult.Success();

            if (blockType == null || blockItemType == null)
                return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, "No block type to place.");

            if (source == null)
                return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, "No source inventory to draw from.");

            // Fail closed on the one condition the engine itself fails open on: a null
            // citizen makes the authorization pass vacuous rather than refusing.
            if (stampedCitizen == null)
                return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, "No stamped citizen -- refusing fail-closed.");

            var pack = new GameActionPack();
            var actions = new List<GameAction>();
            var wrappedPositions = new List<WrappedWorldPosition3i>();

            var blockItem = Item.Get(blockItemType);
            if (blockItem == null)
                return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, "The block item type resolves to no item.");

            foreach (var pos in positions)
            {
                if (pos.Equals(default(BlockPos)))
                    return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, "A position was left at its sentinel default.");

                if (!WrappedWorldPosition3i.TryCreate(new Vector3i(pos.X, pos.Y, pos.Z), out var wrapped))
                    return PlacementResult.Refusal(RemovalRefusalStage.Pretest, "Position is outside world bounds.");

                wrappedPositions.Add(wrapped);

                // The engine's own action for a block entering the world, so a law
                // written against block placement applies to the drone as it does to a
                // citizen.
                var drop = (GameAction)new DropOrPickupBlock(DroppedOrPickedUp.Dropped)
                    .Fill(stampedCitizen, tool, wrapped, null, blockItem);
                pack.AddGameAction(drop);
                actions.Add(drop);

                // Vanilla's placement path destroys any plant standing where the block
                // goes. Reproduced with the removal service's own block: the harvest
                // action is raised so a law against it still applies, and no yield is
                // attached, because burying a plant is not harvesting it.
                if (EcoSim.PlantSim.GetPlant(wrapped) is { } plant)
                {
                    var harvest = new HarvestOrHunt
                    {
                        ActionLocation = (Vector3i)wrapped,
                        Citizen = stampedCitizen,
                        ToolUsed = tool,
                        Species = plant.Species.GetType(),
                        DamagedOrDestroyed = DamagedOrDestroyed.DestroyingOrganism,
                        AccessNeeded = AccessType.ConsumerAccess,
                        DestroyedByBlock = true,
                    };
                    pack.AddGameAction(harvest);
                    actions.Add(harvest);

                    var buriedPlant = plant;
                    pack.AddPostEffect(() => EcoSim.PlantSim.DestroyPlant(buriedPlant, DeathType.Construction, true, stampedCitizen));
                }

                // Through the pack's own change set, so a pack that goes on to refuse
                // consumes no dirt. Sets EarlyResult when the inventory is short, which is
                // checked below before anything is performed.
                pack.RemoveFromInventory(stampedCitizen, source, blockItemType);

                // A post-effect, never a direct write: nothing reaches the world unless
                // the whole pack succeeds.
                var setPos = wrapped;
                pack.AddPostEffect(() => EcoWorld.SetBlock(blockType, setPos));
            }

            // Reproduced from the engine's own placement helper rather than invented, and
            // re-read immediately before performing so a position that changed between
            // planning and performing refuses instead of overwriting.
            pack.AddChangeSet(new PlaceableChangeSet(wrappedPositions));

            var invariantFailure = CheckInvariants(pack, actions, positions.Count);
            if (invariantFailure != null)
                return invariantFailure;

            // The inventory draw failing is a refusal in its own right, reported with the
            // engine's wording rather than left to be classified as a pretest below.
            if (!pack.EarlyResult)
                return PlacementResult.Refusal(RemovalRefusalStage.Pretest, pack.EarlyResult.Message.ToString());

            // Never dry-run, never forced. No user to notify -- the drone works unattended
            // and its stamped citizen is very likely offline.
            var result = pack.TryPerform(null);
            if (result)
                return PlacementResult.Success();

            return PlacementResult.Refusal(GameActionPackGuards.ClassifyRefusal(pack, actions), result.Message.ToString());
        }

        /// <summary>
        /// The shared fail-closed assertions plus the one this service owns: exactly one
        /// drop action per position, so a pack can neither place a block it never
        /// announced nor announce one it never places.
        /// </summary>
        private static PlacementResult CheckInvariants(GameActionPack pack, List<GameAction> actions, int positionCount)
        {
            var shared = GameActionPackGuards.InvariantFailure(pack, actions);
            if (shared != null)
                return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, shared);

            var dropCount = 0;
            foreach (var action in actions)
                if (action is DropOrPickupBlock) dropCount++;

            if (dropCount != positionCount)
                return PlacementResult.Refusal(RemovalRefusalStage.Unrecognised, "Drop-action count did not equal the placed-position count.");

            return null;
        }


        /// <summary>
        /// The engine's own placement pretests, reproduced rather than recalled: read from
        /// <c>AtomicActions.PlaceBlock</c>, which checks world-object containment, the
        /// deep-ocean building restriction, and block availability, in that order.
        ///
        /// Wired in through a change set because a mod cannot add to
        /// <c>GameActionPack.PreTests</c> -- that collection is internal. Running here
        /// also means the tests re-read the world immediately before performing, which is
        /// what catches a position that changed since the pass planned it.
        /// </summary>
        private sealed class PlaceableChangeSet : IGameActionPackChangeSet
        {
            private readonly IReadOnlyList<WrappedWorldPosition3i> positions;

            public PlaceableChangeSet(IReadOnlyList<WrappedWorldPosition3i> positions) => this.positions = positions;

            public Eco.Shared.Localization.LocString GameActionPackPostEffect() => Eco.Shared.Localization.LocString.Empty;

            public Eco.Core.Utils.Result GameActionPackPretest()
            {
                foreach (var position in this.positions)
                {
                    if (BlockContainerManager.Obj.IsBlockContained(position))
                        return Eco.Core.Utils.Result.FailLocStr("Can't place block within another world object");

                    var existingBlock = EcoWorld.GetBlock(position);

                    if (!DifficultySettingsConfig.Advanced.AllowDeepOceanBuilding)
                    {
                        // Built explicitly rather than through an XZ() extension: several
                        // are in scope here and the float-vector overload wins, which
                        // silently would not be the layer coordinate this needs.
                        var world = (Vector3i)position;
                        var column = new Vector2i(world.X, world.Z);
                        if (column.IsInDeepOcean())
                        {
                            // Both halves of the engine's rule: nothing above the water
                            // line, and no replacing the ocean's own water.
                            if (position.Y > EcoWorld.GetWaterHeight(column))
                                return Eco.Core.Utils.Result.FailLocStr("Can't build above the ocean.");
                            if (existingBlock is IWaterBlock)
                                return Eco.Core.Utils.Result.FailLocStr("Can't build in the deep ocean.");
                        }
                    }

                    if (!OccupancyUtils.IsBlockAvailable(existingBlock))
                        return Eco.Core.Utils.Result.FailLocStr("Can't place block there");
                }

                return Eco.Core.Utils.Result.Succeeded;
            }

            public void GameActionPackDispose()
            {
            }
        }
    }
}
