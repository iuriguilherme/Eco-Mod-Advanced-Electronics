using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Civics.Laws;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.Plants;
using Eco.Mods.TechTree;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Math;
using Eco.Shared.Utils;
using Eco.Simulation;
using Eco.Simulation.Agents;
using Eco.Simulation.Types;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    public enum FarmActionOutcome
    {
        Succeeded,
        Refused
    }

    /// <summary>One farming attempt: succeeded, or refused with the stage that refused it and the engine's own message.</summary>
    public sealed class FarmActionResult
    {
        public FarmActionOutcome Outcome { get; }
        public RemovalRefusalStage RefusalStage { get; }
        public string Message { get; }

        /// <summary>What the harvest actually put in the hold, empty for the other two verbs.</summary>
        public IReadOnlyList<ItemStack> Harvested { get; }

        private FarmActionResult(
            FarmActionOutcome outcome, RemovalRefusalStage stage, string message, IReadOnlyList<ItemStack> harvested)
        {
            this.Outcome = outcome;
            this.RefusalStage = stage;
            this.Message = message;
            this.Harvested = harvested ?? Array.Empty<ItemStack>();
        }

        public static FarmActionResult Success(IReadOnlyList<ItemStack> harvested = null) =>
            new(FarmActionOutcome.Succeeded, default, null, harvested);

        public static FarmActionResult Refusal(RemovalRefusalStage stage, string message) =>
            new(FarmActionOutcome.Refused, stage, message, null);
    }

    /// <summary>
    /// Plow, sow and harvest as three hand-built game-action packs (U7, R14, R24, R27).
    ///
    /// Hand-built rather than routed through Eco's own farming helpers for one reason:
    /// those helpers take a <c>Player</c>, and the drone has a stamped <c>User</c> who is
    /// very likely offline. <c>PlantEntity.CalculateHarvestResources</c> dereferences
    /// <c>player.User</c> unconditionally, and <c>TryHarvest</c> does the same, so calling
    /// either would fault rather than refuse. This mirrors what
    /// <see cref="MiningRemovalService"/> already had to do for digging.
    ///
    /// Never unit-tested -- every collaborator is an Eco type. The same static-review
    /// checklist the removal and placement services carry applies to all three packs
    /// here: the engine's own action types, the citizen-taking <c>Fill</c> overload, no
    /// access override, unset pack flags, no waived authorization, world writes only in
    /// post-effects, and <c>TryPerform</c> with neither dry run nor force.
    /// </summary>
    public sealed class FarmingActionService
    {
        /// <summary>
        /// Plows one surface into tilled dirt, burying anything standing on it.
        ///
        /// R14's plow clause and its plow-under clause are one implementation, because
        /// they are one action: the ground becomes tilled and whatever was growing there
        /// goes with it.
        /// </summary>
        public FarmActionResult Plow(BlockPos position, User stampedCitizen, Item tool)
        {
            if (stampedCitizen == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, "No stamped citizen -- refusing fail-closed.");

            if (!WrappedWorldPosition3i.TryCreate(new Vector3i(position.X, position.Y, position.Z), out var wrapped))
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "Position is outside world bounds.");

            var pack = new GameActionPack();
            var actions = new List<GameAction>();

            var plow = (GameAction)new PlowField().Fill(stampedCitizen, tool, wrapped, null);
            pack.AddGameAction(plow);
            actions.Add(plow);

            // A plant standing above the surface is destroyed and yields nothing. The
            // action is raised so a law against it still applies; no harvest stacks are
            // attached, because plowing under is not harvesting.
            if (wrapped.TryIncreaseY(1, out var above))
                AddPlantDestruction(pack, actions, above, stampedCitizen, tool);

            var tilledPos = wrapped;
            pack.AddPostEffect(() => EcoWorld.SetBlock(typeof(TilledDirtBlock), tilledPos));

            return Perform(pack, actions);
        }

        /// <summary>
        /// Sows the area's crop into the empty cell above a tilled block (R24), drawing
        /// one seed from linked storage.
        /// </summary>
        /// <param name="groundPosition">The tilled block. The seed spawns in the cell above it.</param>
        public FarmActionResult Sow(
            BlockPos groundPosition, string cropKey, User stampedCitizen, Item tool, Inventory source)
        {
            if (stampedCitizen == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, "No stamped citizen -- refusing fail-closed.");

            if (source == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, "No source inventory to draw seed from.");

            var species = ResolveSpecies(cropKey);
            if (species == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, $"No plant species named '{cropKey}'.");

            var seedType = ResolveSeedItemType(species);
            if (seedType == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, $"{species.DisplayName} has no seed item.");

            if (!WrappedWorldPosition3i.TryCreate(
                    new Vector3i(groundPosition.X, groundPosition.Y, groundPosition.Z), out var ground))
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "Position is outside world bounds.");

            // Vanilla's own target tests, reproduced from SeedItem.Plant rather than
            // recalled: tilled below, the cell above free, and nothing already growing.
            if (!EcoWorld.GetBlock(ground).Is<Tilled>())
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "The ground is not tilled.");

            if (!ground.TryIncreaseY(1, out var spawnPos))
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "Cannot plant in the skies.");

            if (EcoSim.PlantSim.GetPlant(spawnPos) != null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "A plant already stands there.");

            if (species.Water)
            {
                if (!species.IsGoodPlacement(spawnPos))
                    return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "Not enough water depth for this plant.");
            }
            else if (!EcoWorld.GetBlock(spawnPos).Is<Empty>())
            {
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "The cell above the ground is not empty.");
            }

            var pack = new GameActionPack();

            // The engine's own seed action, which takes a User rather than a Player -- the
            // one farming helper the drone can reach directly (KTD8). It raises PlantSeeds
            // and spawns the tended plant as a post-effect.
            pack.AddSeedAction(species, (Vector3i)spawnPos, stampedCitizen, tool);

            // Through the pack's change set, so a refused pack keeps the seed.
            pack.RemoveFromInventory(stampedCitizen, source, seedType);

            // AddSeedAction keeps its action inside the pack -- GameActionPack.GameActions
            // is internal -- so refusal classification needs an equivalent to probe with.
            // This mirrors the fields the helper sets, and is never performed: it exists
            // only for the read-only law and authorization checks in ClassifyRefusal.
            // If the helper's action shape ever changes, this probe must follow it.
            var probe = new PlantSeeds
            {
                Species = species.GetType(),
                ActionLocation = (Vector3i)spawnPos,
                Citizen = stampedCitizen,
                ToolUsed = tool,
            };

            return Perform(pack, new List<GameAction> { probe });
        }

        /// <summary>
        /// Harvests a ripe plant into the drone's hold (R27), destroying or leaving it to
        /// regrow by the species' own rule.
        /// </summary>
        public FarmActionResult Harvest(BlockPos plantPosition, User stampedCitizen, Item tool, Inventory hold)
        {
            if (stampedCitizen == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, "No stamped citizen -- refusing fail-closed.");

            if (hold == null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, "No hold to harvest into.");

            if (!WrappedWorldPosition3i.TryCreate(
                    new Vector3i(plantPosition.X, plantPosition.Y, plantPosition.Z), out var wrapped))
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "Position is outside world bounds.");

            if (!(EcoSim.PlantSim.GetPlant(wrapped) is { } plant))
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "Nothing is growing there.");

            // The species' own thresholds, not a single fully-grown flag: Plant.Ripe folds
            // in the pickable percentage, the post-harvest regrowth floor, and the
            // zero-yield case, and each species answers it differently.
            if (!plant.Ripe)
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, "The plant is not ripe enough to harvest.");

            var pack = new GameActionPack();
            var actions = new List<GameAction>();

            var harvested = CalculateBaseYield(plant);
            foreach (var stack in harvested)
                pack.AddToInventory(hold, stack.Item, stack.Quantity, stampedCitizen);

            // Harvested as a careful pick rather than a scythe cut: the drone takes the
            // crop off the plant, so the species' regrowth rule gets its say instead of
            // ScythingKills overriding it.
            var destroysPlant = species_DestroysOnHarvest(plant);

            var harvest = new HarvestOrHunt
            {
                ActionLocation = (Vector3i)wrapped,
                Citizen = stampedCitizen,
                ToolUsed = tool,
                Species = plant.Species.GetType(),
                HarvestedStacks = harvested,
                AccessNeeded = AccessType.ConsumerAccess,
                DamagedOrDestroyed = destroysPlant
                    ? DamagedOrDestroyed.DestroyingOrganism
                    : DamagedOrDestroyed.NotDestroyingOrganism,
                DestroyedByBlock = false,
            };
            pack.AddGameAction(harvest);
            actions.Add(harvest);

            var harvestedPlant = plant;
            pack.AddPostEffect(() =>
            {
                if (destroysPlant)
                {
                    EcoSim.PlantSim.DestroyPlant(harvestedPlant, DeathType.Harvesting, true, stampedCitizen);
                    EcoWorld.ForceUpdate((Vector3i)wrapped);
                }
                else
                {
                    // Left standing at the species' own post-harvest growth, which is what
                    // lets a re-harvestable crop come round again without being resown.
                    harvestedPlant.GrowthPercent = harvestedPlant.Species.PostHarvestingGrowth;
                }
            });

            var result = Perform(pack, actions);
            return result.Outcome == FarmActionOutcome.Succeeded
                ? FarmActionResult.Success(harvested)
                : result;
        }

        /// <summary>
        /// The species' own resource roll with no citizen bonus applied (KTD2).
        ///
        /// Reimplemented rather than delegated because the engine's own path is
        /// player-bound end to end: <c>CalculateHarvestResources</c> reads
        /// <c>player.User</c> for the yield attribute, the talent bonus context, and the
        /// talent set. The drone's citizen may be offline, and more to the point the
        /// drone is not the citizen -- R39 states this trade on the item so a player reads
        /// it before crafting.
        ///
        /// The arithmetic is <c>Plant.CalculateResourceYield</c>'s, which is protected and
        /// so not callable: <c>(round(range.Diff * YieldPercent) + range.Min)</c> scaled by
        /// growth squared.
        /// </summary>
        private static List<ItemStack> CalculateBaseYield(Plant plant)
        {
            var harvestList = new List<ItemStack>();
            if (!plant.Alive || plant.Species.ResourceList.Count == 0) return harvestList;

            foreach (var resource in plant.Species.ResourceList)
            {
                if (resource.ResourceType == null) continue;
                if (!RandomUtil.Chance(resource.ResourceChance)) continue;

                var quantity = BaseResourceYield(plant, resource.ResourceRange);
                if (quantity <= 0) continue;

                harvestList.Add(new ItemStack(Item.GetNonUniqueOrClone(resource.ResourceType), quantity));
            }

            return harvestList;
        }

        private static int BaseResourceYield(Plant plant, Eco.Shared.Math.Range range)
        {
            var growthMultiplier = plant.GrowthPercent * plant.GrowthPercent;
            return (int)((Math.Round(range.Diff * plant.YieldPercent, MidpointRounding.AwayFromZero) + range.Min)
                         * growthMultiplier);
        }

        /// <summary>
        /// Whether this harvest ends the plant, by the species' own rule: a species with
        /// no post-harvest growth is always destroyed, and one with a regrowth floor is
        /// destroyed only when it has not reached it.
        /// </summary>
        private static bool species_DestroysOnHarvest(Plant plant) =>
            plant.Species.PostHarvestingGrowth == 0
            || plant.GrowthPercent < plant.Species.PostHarvestingGrowth + .1f;

        private static void AddPlantDestruction(
            GameActionPack pack, List<GameAction> actions, WrappedWorldPosition3i position, User citizen, Item tool)
        {
            if (!(EcoSim.PlantSim.GetPlant(position) is { } plant)) return;

            var harvest = new HarvestOrHunt
            {
                ActionLocation = (Vector3i)position,
                Citizen = citizen,
                ToolUsed = tool,
                Species = plant.Species.GetType(),
                DamagedOrDestroyed = DamagedOrDestroyed.DestroyingOrganism,
                AccessNeeded = AccessType.ConsumerAccess,
                DestroyedByBlock = false,
            };
            pack.AddGameAction(harvest);
            actions.Add(harvest);

            var doomed = plant;
            pack.AddPostEffect(() => EcoSim.PlantSim.DestroyPlant(doomed, DeathType.Construction, true, citizen));
        }

        /// <summary>The crop key is the engine's own species name, resolved case-insensitively.</summary>
        private static PlantSpecies ResolveSpecies(string cropKey) =>
            string.IsNullOrEmpty(cropKey) ? null : EcoSim.GetSpecies(cropKey) as PlantSpecies;

        /// <summary>
        /// Corn needs corn seed. Resolved through the engine's own species link on
        /// <c>SeedItem</c> rather than by matching names, so a crop whose seed is named
        /// differently still finds it (KTD15).
        /// </summary>
        private static Type ResolveSeedItemType(PlantSpecies species) =>
            Item.AllItemsIncludingHidden
                .OfType<SeedItem>()
                .FirstOrDefault(seed => seed.Species == species)
                ?.GetType();

        /// <summary>
        /// The invariants and the perform, shared by all three verbs so none of them can
        /// quietly skip a check the others keep.
        /// </summary>
        private static FarmActionResult Perform(GameActionPack pack, List<GameAction> actions)
        {
            var invariantFailure = GameActionPackGuards.InvariantFailure(pack, actions);
            if (invariantFailure != null)
                return FarmActionResult.Refusal(RemovalRefusalStage.Unrecognised, invariantFailure);

            // A failed early result is a refusal in its own right, reported with the
            // engine's wording rather than left to be classified below.
            if (!pack.EarlyResult)
                return FarmActionResult.Refusal(RemovalRefusalStage.Pretest, pack.EarlyResult.Message.ToString());

            // Never dry-run, never forced. No user to notify -- the drone works unattended.
            var result = pack.TryPerform(null);
            if (result) return FarmActionResult.Success();

            return FarmActionResult.Refusal(GameActionPackGuards.ClassifyRefusal(pack, actions), result.Message.ToString());
        }

    }
}
