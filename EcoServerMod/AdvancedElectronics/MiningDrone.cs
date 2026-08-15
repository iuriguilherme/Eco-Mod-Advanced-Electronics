using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Core.Items;
using Eco.Gameplay.Components;
// FuelSupplyComponent lives here, NOT in Eco.Gameplay.Components alongside
// FuelConsumptionComponent -- the two fuel components sit in different namespaces, so
// importing only the obvious one resolves the consumption half and silently fails on
// the supply half.
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Players;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Systems.NewTooltip;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Craftable mining drone item. Not a placeable WorldObject: it lives in a player's or a
    /// <see cref="DroneDockObject"/>'s inventory until inserted into a dock's storage slot,
    /// which pairs it (see DroneDockObject.OnDockStorageChanged). See
    /// <see cref="MiningDroneObject"/> below for the physical roaming WorldObject a dock
    /// dispatches.
    /// </summary>
    [Serialized]
    [Weight(500)]
    [LocDisplayName("Mining Drone")]
    [LocDescription("A craftable mining drone. Insert into a Drone Dock to pair it for dispatch.")]
    [Ecopedia("Crafted Objects", "Advanced Electronics", true, true, null)]
    public class MiningDroneItem : RepairableItem, IWorldObjectComponentSource, IPersistentData
    {
        /// <summary>
        /// Carries the state of the components this drone installed, across being pulled out of one
        /// dock and put into another.
        ///
        /// Without it the drone's fuel supply is destroyed on removal along with the component
        /// itself, so a partly-burned unit of biodiesel is lost and the dock charges a fresh one to
        /// start up again. That is 0.13 behaviour; 0.14 keeps fuel across pickup, and
        /// FuelSupplyComponent implements IPersistentData precisely so it can. DroneModuleComponent
        /// captures into this on uninstall and restores from it on install.
        /// </summary>
        [Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
        public object PersistentData { get; set; }

        /// <summary>
        /// Repair cost, scaled by the repairer's Advanced Electronics skill: a specialist spends
        /// fewer ingredients to restore more condition. Mirrors the AutoGen tools, e.g.
        /// ModernPickaxeItem, which scales its repair cost by Blacksmith the same way.
        /// </summary>
        private static readonly SkillModifiedValue skilledRepairCost = new SkillModifiedValue(
            2,
            AdvancedElectronicsSkill.MultiplicativeStrategy,
            typeof(AdvancedElectronicsSkill),
            typeof(MiningDroneItem),
            Localizer.DoStr("repair cost"),
            DynamicValueType.Efficiency);

        public override IDynamicValue SkilledRepairCost => skilledRepairCost;

        public override float OriginalMaxDurability => 1000f;

        // IMPLEMENTATION-TIME CHOICE, not specified by the plan: the drone's signature
        // component and one of its own craft ingredients. R10 makes a broken drone stop its
        // dock until repaired, so leaving RepairItem null (the RepairableItem default) would
        // make a worn-out drone permanently dead rather than serviceable.
        public override Item RepairItem => Item.Get<AdvancedCircuitItem>();

        // The mod's own Battery carries this tag. Matches SurveyDroneItem, which switched off
        // "Liquid Fuel" once the Battery shipped as a craftable inventory item -- the comment
        // here still claimed the battery was deferred, which stopped being true then.
        private static readonly string[] fuelTagList = { "Electric Fuel" };

        /// <summary>
        /// The components this drone brings to whatever dock it is slotted into (R5, R7). The
        /// dock installs them on slot and uninstalls them on removal; see DroneDockObject.
        ///
        /// THE FUEL PAIR IS DELIBERATELY UNNAMED, which reverses this plan's KTD4 in the other
        /// direction. Naming them crashed the server on the first tick of real work:
        /// FuelConsumptionComponent resolves its supply in its own Initialize with
        ///
        ///     this.fuelSupply = this.Parent.GetComponent&lt;FuelSupplyComponent&gt;();
        ///
        /// which passes no name, and GetComponent matches on assignability AND `component.Name
        /// == name`. A named supply therefore cannot be found by the vanilla consumer at all --
        /// fuelSupply stayed null and Tick dereferenced it the moment Parent.Operating went true.
        /// The pairing is not name-aware, so it is not ours to name.
        ///
        /// Unnamed is safe here because the dock declares no fuel components of its own; the
        /// ambiguity KTD4 guards against is a real hazard only for a type the dock already
        /// carries unnamed, which today means PublicStorageComponent -- exactly why the cargo
        /// hold below IS named (U6, R23, R25): see <see cref="DroneCargo"/>, the single place
        /// the hold's name and slot count live.
        /// </summary>
        public IEnumerable<ComponentInstallation> ComponentsToInstall => new[]
        {
            ComponentInstallation.For<FuelSupplyComponent>(
                configure:    c => c.Initialize(2, fuelTagList),
                // R8: refuse removal while the tank still holds fuel, so uninstalling can
                // never destroy items. RemoveComponent does not capture state.
                canUninstall: c => c.Inventory.IsEmpty,
                // Vehicles proxy an installed component's interactions onto the module the
                // player can point at. A drone sits inside the dock's storage and has no such
                // surface, so proxying would register interactions that cannot dispatch.
                proxyInteractions: false),
            ComponentInstallation.For<FuelConsumptionComponent>(
                configure:         c => c.Initialize(FuelJoulesPerSecond),
                proxyInteractions: false),
            DroneCargo.Installation(),
        };

        /// <summary>
        /// Burn rate, starting at the generator band rather than the Excavator's 275 the drone
        /// inherited by copying. Vanilla comparisons: Truck 250, Excavator 275, Industrial and
        /// Combustion Generator 75. A drone creeps and idles rather than doing heavy work.
        /// Tune after a long live session.
        /// </summary>
        private const float FuelJoulesPerSecond = 75f;
    }

    /// <summary>
    /// The physical roaming drone WorldObject that a <see cref="DroneDockObject"/> dispatches.
    /// Spawned and destroyed by <see cref="DroneDockObject.OnDockStorageChanged"/> when a
    /// <see cref="MiningDroneItem"/> is inserted into / removed from the dock. The
    /// <c>[RequireComponent]</c> declarations pull in <see cref="DroneMoverComponent"/>,
    /// <see cref="OreSensorComponent"/>, and <see cref="DroneLifecycle"/> automatically on
    /// spawn.
    ///
    /// The three properties below are inherited from the survey drone's design and the
    /// reasoning is reproduced because it is not obvious from the code:
    /// <list type="bullet">
    /// <item><description>
    /// R3 (invulnerable to tool/animal damage): this class deliberately implements no
    /// damage-taking interface and attaches no health/damage component -- invulnerability
    /// is the absence of a damage surface, not a "take zero damage" handler. Confirmed by
    /// reflecting over the exact Eco.ReferenceAssemblies build this project compiles
    /// against (0.13.0.4-beta-release-1024, Eco.Gameplay.dll / Eco.Mods.dll /
    /// Eco.Simulation.dll): the two damage-taking surfaces that exist in this API surface
    /// are <c>Eco.Gameplay.Interactions.IDamageable</c> (what tools call to hit
    /// something) and <c>Eco.Simulation.Agents.ICanTakeDamage</c> (what
    /// <c>TryDamage</c> is called against); across all three assemblies the only
    /// implementers are <c>Player</c>, <c>User</c>, and <c>AnimalEntity</c> (and its
    /// TechTree subclasses, e.g. Wolf, Coyote). <c>WorldObject</c> itself implements
    /// neither, and no stock WorldObject subclass in those assemblies implements either
    /// one -- there is no vanilla "structure health" surface for WorldObjects to opt out
    /// of. <see cref="MiningDroneObject"/> follows the same pattern: implement neither,
    /// attach nothing damage-related, and it is invulnerable by construction.
    /// </description></item>
    /// <item><description>
    /// R4 (free-roam, crosses claims): movement is driven entirely by
    /// <see cref="DroneMoverComponent"/> (U2), required here via
    /// <c>[RequireComponent]</c>. That component's Tick() only reads/writes
    /// Position/Rotation and calls SyncPositionAndRotation() -- audited and confirmed it
    /// calls no claim/permission/auth API anywhere in its movement path (see this unit's
    /// report). Nothing added by this class adds one either; free-roam is simply the
    /// absence of such a check, not a bypass flag.
    /// </description></item>
    /// <item><description>
    /// R5 (owner attribution, law enforcement deferred): <see cref="OwnerName"/> /
    /// <see cref="OwnerId"/> are plain serialized fields (mirrors
    /// DroneDockObject.AssignedDistrictName's own "trivially serializable" reasoning, rather
    /// than serializing a <see cref="DroneOwnership"/> value type directly, since Eco's
    /// serializer support for custom structs was not verified), stamped via
    /// <see cref="SetOwner"/>. Wiring SetOwner into the dock's pairing/dispatch flow is
    /// left to U8. No citizenship/law-violation API is touched here -- explicitly
    /// deferred per KTD7, even though a drone crossing a claim boundary is an obvious
    /// integration point for one.
    /// </description></item>
    /// </list>
    /// </summary>
    [Serialized]
    [RequireComponent(typeof(DroneMoverComponent))]
    [RequireComponent(typeof(OreSensorComponent))]
    [RequireComponent(typeof(DroneLifecycle))]
    // The drone carries no fuel, parts, storage, or auth components, and is not interactable
    // (R1, R2). All three of those moved to the dock, which is an ordinary placed object with
    // an item behind it; a drone is not, and its window opened with no tabs at all no matter
    // what was attached. The drone is a mover now, nothing more.
    //
    // WorldObject itself carries [Tag("Usable")] and the interact key is gated on that tag,
    // so unsetting it is what removes the affordance -- there is no "not interactable" flag.
    // BaseRampObject in Mods/__core__/Items/Roads.cs is the vanilla precedent.
    [Tag("Usable", Unset = true)]
    public partial class MiningDroneObject : WorldObject, IDroneOwnable, IDroneToolbearer
    {
        /// <summary>The mining arm. A class constant, never stored.</summary>
        public DroneTool Tool => DroneTool.Mining;

        /// <summary>Hook for mods to customize WorldObject before initialization. You can change housing values here.</summary>
        partial void ModsPreInitialize();
        /// <summary>Hook for mods to customize WorldObject after initialization.</summary>
        partial void ModsPostInitialize();

        
        /// <summary>
        /// Registers the drone's single-block placement footprint. Required even though
        /// the drone is spawned via WorldObjectManager.ForceAdd (not player-placed) --
        /// a WorldObject with no registered occupancy has no valid footprint and the
        /// spawn is silently rejected the same way manual placement is. See DroneDockObject's
        /// static constructor for the full explanation (copied from the Advanced
        /// Mixology reference mod).
        /// </summary>
        static MiningDroneObject()
        {
            AddOccupancy<MiningDroneObject>(new List<BlockOccupancy>
            {
                new BlockOccupancy(new Vector3i(0, 0, 0)),
            });
        }

        public override LocString DisplayName => Localizer.DoStr("Mining Drone");

        /// <summary>Display name of the owner this drone acts on behalf of, or null if never stamped.</summary>
        [Serialized]
        public string OwnerName { get; private set; }

        /// <summary>Eco user ID of the owner, or 0 if never stamped.</summary>
        [Serialized]
        public int OwnerId { get; private set; }

        /// <summary>True once <see cref="SetOwner"/> has stamped a real user.</summary>
        public bool HasOwner => this.OwnerId != 0;

        /// <summary>
        /// Stamps this drone's owner from the acting user. A plain setter, so
        /// <see cref="MiningDroneObject"/> itself does not need to know about the
        /// dock/pairing layers that decide WHEN to call it. Delegates the actual
        /// (name, id) assignment to <see cref="DroneOwnership.FromUser"/>.
        /// </summary>
        public void SetOwner(User user)
        {
            var ownership = DroneOwnership.FromUser(user);
            this.OwnerName = ownership.OwnerName;
            this.OwnerId = ownership.OwnerId;
        }
        protected override void Initialize()
        {
            this.ModsPreInitialize();
            base.Initialize();
            this.ModsPostInitialize();
        }
    }

    // WITHHELD FOR THE NEXT RELEASE -- the mining drone's arm does not yet behave the way it is
    // meant to during flight, so the drone is not offered to players.
    //
    // The whole class is commented out rather than just its table registration. RecipeFamily
    // carries [ForceCreateViewAllDerived], so the type existing is enough: Eco instantiates it at
    // startup and Initialize() registers the recipe, which leaves it visible in the recipe browser
    // and the skill's tech tree even when it belongs to no bench. Only removing the type removes
    // it from the game.
    //
    // The item and world object are deliberately left defined, so a save that already holds a
    // mining drone keeps loading. Restore by deleting the /* and */ below.
    /*
    /// <summary>Recipe unlocking <see cref="MiningDroneItem"/>.</summary>
    [RequiresSkill(typeof(AdvancedElectronicsSkill), 1)]
    public partial class MiningDroneRecipe : RecipeFamily
    {
        // Eco force-creates one instance of every RecipeFamily-derived type at startup
        // (RecipeFamily carries [ForceCreateViewAllDerived]) -- registration belongs in
        // the instance constructor, mirroring vanilla recipes (e.g. StorageChestRecipe).
        public MiningDroneRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "MiningDrone",
                displayName: Localizer.DoStr("Mining Drone"),
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(AdvancedCircuitItem), 6, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(InsulatedCopperWiringItem), 4, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(GearboxItem), 4, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(PlasticItem), 20, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(FiberglassItem), 20, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(SteelGearItem), 6, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(ElectricMotorItem), 1, true),
                    new IngredientElement(typeof(RubberWheelItem), 2, true),
                    new IngredientElement(typeof(RadiatorItem), 1, true),
                    new IngredientElement(typeof(SteelAxleItem), 1, true),
                    new IngredientElement(typeof(LightBulbItem), 2, true),
                    new IngredientElement(typeof(LubricantItem), 2, true),
                },
                items: new List<CraftingElement>
                {
                    new CraftingElement<MiningDroneItem>(1),
                });

            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 30;
            this.LaborInCalories = CreateLaborInCaloriesValue(1000, typeof(AdvancedElectronicsSkill));
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(MiningDroneRecipe), start: 10, skillType: typeof(AdvancedElectronicsSkill));
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Mining Drone"), recipeType: typeof(MiningDroneRecipe));
            this.ModsPostInitialize();

            // WITHHELD FOR THE NEXT RELEASE. The mining drone's arm does not yet behave the way
            // it is meant to during flight, so it is not offered to players. Registering no
            // table is what hides it: a RecipeFamily that joins no CraftingComponent appears on
            // no bench, while the item and world object stay defined so existing saves that
            // already hold one keep loading. Restore by uncommenting the line below.
            //
            // CraftingComponent.AddRecipe(tableType: typeof(RoboticAssemblyLineObject), recipeFamily: this);
        }

        /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
        partial void ModsPreInitialize();

        /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
        partial void ModsPostInitialize();
    }
    */
}
