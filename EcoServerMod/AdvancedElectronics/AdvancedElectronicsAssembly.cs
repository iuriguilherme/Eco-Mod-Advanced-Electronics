
namespace Eco.Mods.TechTree
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using Eco.Core.Items;
    using Eco.Gameplay.Blocks;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.Components.Auth;
    using Eco.Gameplay.DynamicValues;
    using Eco.Gameplay.Economy;
    using Eco.Gameplay.Housing;
    using Eco.Gameplay.Interactions;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Modules;
    using Eco.Gameplay.Minimap;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Occupancy;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Property;
    using Eco.Gameplay.Skills;
    using Eco.Gameplay.Systems;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Gameplay.Pipes.LiquidComponents;
    using Eco.Gameplay.Pipes.Gases;
    using Eco.Shared;
    using Eco.Shared.Math;
    using Eco.Shared.Localization;
    using Eco.Shared.Serialization;
    using Eco.Shared.Utils;
    using Eco.Shared.View;
    using Eco.Shared.Items;
    using Eco.Shared.Networking;
    using Eco.Gameplay.Pipes;
    using Eco.World.Blocks;
    using Eco.Gameplay.Housing.PropertyValues;
    using Eco.Gameplay.Civics.Objects;
    using Eco.Gameplay.Settlements;
    using Eco.Gameplay.Systems.NewTooltip;
    using Eco.Core.Controller;
    using Eco.Core.Utils;
	using Eco.Gameplay.Components.Storage;
    using static Eco.Gameplay.Housing.PropertyValues.HomeFurnishingValue;
    using static Eco.Gameplay.Components.PartsComponent;
    using Eco.Gameplay.Items.Recipes;

    [Serialized]
    [RequireComponent(typeof(OnOffComponent))]
    [RequireComponent(typeof(PropertyAuthComponent))]
    [RequireComponent(typeof(MinimapComponent))]
    [RequireComponent(typeof(LinkComponent))]
    [RequireComponent(typeof(CraftingComponent))]
    [RequireComponent(typeof(PartsComponent))]
    [RequireComponent(typeof(PowerGridComponent))]
    [RequireComponent(typeof(PowerConsumptionComponent))]
    [RequireComponent(typeof(HousingComponent))]
    [RequireComponent(typeof(OccupancyRequirementComponent))]
    [RequireComponent(typeof(PluginModulesComponent))]
    [RequireComponent(typeof(ForSaleComponent))]
    [RequireComponent(typeof(RoomRequirementsComponent))]
    [RequireRoomContainment]
    [RequireRoomVolume(30)]
    [RequireRoomMaterialTier(4.8f)]
    [Tag("Usable")]
    // TODO: Include in Ecopedia
    //[Ecopedia("Work Stations", "Craft Tables", subPageName: "Advanced Electronics Assembly Item")]
    [RepairRequiresSkill(typeof(ElectronicsSkill), 4)]
    [RepairRequiresSkill(typeof(SelfImprovementSkill), 6)]
    public partial class AdvancedElectronicsAssemblyObject : WorldObject, IRepresentsItem
    {
        /// <summary>
        /// Registers this object's placement footprint (a single 1x1x1 block) so the
        /// client can place it. A custom modded WorldObject MUST declare its occupancy
        /// in code via AddOccupancy&lt;T&gt; in a static constructor -- vanilla AutoGen
        /// objects get this baked by the WorldObjectTemplate.tt generator, so it never
        /// appears in their visible source, but a hand-written mod object has no
        /// generator and must do it itself. Without this,
        /// GetOccupancyInfo(typeof(AdvancedElectronicsAssemblyObject)) is empty and the
        /// object silently cannot be placed (no ghost, no error). Same pattern and same
        /// reasoning as DroneDockObject's static constructor.
        ///
        /// 1x1x1 is a placeholder footprint chosen to match the placeholder cube prefab,
        /// not the table's eventual real size. If the footprint grows, the prefab must
        /// grow with it -- a prefab visually larger than its registered occupancy places
        /// wrong and reads as a rendering bug.
        /// </summary>
        static AdvancedElectronicsAssemblyObject()
        {
            AddOccupancy<AdvancedElectronicsAssemblyObject>(new List<BlockOccupancy>
            {
                new BlockOccupancy(new Vector3i(0, 0, 0)),
            });
        }

        public virtual Type RepresentedItemType => typeof(AdvancedElectronicsAssemblyItem);
        public override LocString DisplayName => Localizer.DoStr("Advanced Electronics Assembly");
        public override TableTextureMode TableTexture => TableTextureMode.Metal;

        protected override void Initialize()
        {
            this.ModsPreInitialize();
            this.GetComponent<MinimapComponent>().SetCategory(Localizer.DoStr("Crafting"));
            this.GetComponent<PowerConsumptionComponent>().Initialize(1500);
            this.GetComponent<PowerGridComponent>().Initialize(10, new ElectricPower());
            this.GetComponent<PowerGridComponent>().DurabilityUsedPerHourOfUse = 0.0f;
            this.GetComponent<HousingComponent>().HomeValue = ElectronicsAssemblyItem.homeValue;
            this.GetComponent<PartsComponent>().Config(() => LocString.Empty, new PartInfo[]
            {
                                // ISOLATION TEST: parts left empty. Restore CopperWiringItem
                                // once we know whether the parts entry is what recurses.
                                //new() { TypeName = nameof(CopperWiringItem), Quantity = 10},
                            });
            this.ModsPostInitialize();
        }

        /// <summary>Hook for mods to customize WorldObject before initialization. You can change housing values here.</summary>
        partial void ModsPreInitialize();
        /// <summary>Hook for mods to customize WorldObject after initialization.</summary>
        partial void ModsPostInitialize();
    }

    [Serialized]
    [LocDisplayName("Advanced Electronics Assembly")]
    [LocDescription("A set of machinery to create modern electronics.")]
    [IconGroup("World Object Minimap")]
    [Ecopedia("Work Stations", "Craft Tables", createAsSubPage: true)]
    [Weight(5000)] // Defines how heavy AdvancedElectronicsAssembly is.
    // TODO: re-enable once AdvancedElectronicsUpgradeItem exists again.
    //[AllowPluginModules(Tags = new[] { "ModernUpgrade" }, ItemTypes = new[] { typeof(AdvancedElectronicsUpgradeItem) })] //noloc
    public partial class AdvancedElectronicsAssemblyItem : WorldObjectItem<AdvancedElectronicsAssemblyObject>, IPersistentData
    {
        protected override OccupancyContext GetOccupancyContext => new SideAttachedContext( 0  | DirectionAxisFlags.Down , WorldObject.GetOccupancyInfo(this.WorldObjectType));
        public override HomeFurnishingValue HomeValue => homeValue;
        public static readonly HomeFurnishingValue homeValue = new HomeFurnishingValue()
        {
            ObjectName                              = typeof(AdvancedElectronicsAssemblyObject).UILink(),
            Category                                = HousingConfig.GetRoomCategory("Industrial"),
            TypeForRoomLimit                        = Localizer.DoStr(""),
        };

        [NewTooltip(CacheAs.SubType, 7)] public static LocString PowerConsumptionTooltip() => Localizer.Do($"Consumes: {Text.Info(1500)}w of {new ElectricPower().Name} power.");
        [Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)] public object PersistentData { get; set; }
    }

    [RequiresSkill(typeof(IndustrySkill), 2)]
    // TODO: Add this in Ecopedia
    //[Ecopedia("Work Stations", "Craft Tables", subPageName: "Advanced Electronics Assembly Item")]
    public partial class AdvancedElectronicsAssemblyRecipe : RecipeFamily
    {
        public AdvancedElectronicsAssemblyRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "AdvancedElectronicsAssembly",  //noloc
                displayName: Localizer.DoStr("Advanced Electronics Assembly"),

                // Defines the ingredients needed to craft this recipe. An ingredient items takes the following inputs
                // type of the item, the amount of the item, the skill required, and the talent used.
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(FlatSteelItem), 16, typeof(IndustrySkill)),
                    // Tag ingredient, not a type: "AshlarStone" is the tag carried by
                    // AshlarSandstoneItem and its siblings, so any ashlar stone satisfies it.
                    new IngredientElement("AshlarStone", 20, typeof(IndustrySkill)), //noloc
                    // TODO: v14 item
                    //new IngredientElement(typeof(InsulatedCopperWiringItem), 25, typeof(IndustrySkill)),
                },
                // Define our recipe output items.
                // For every output item there needs to be one CraftingElement entry with the type of the final item and the amount
                // to create.
                items: new List<CraftingElement>
                {
                    new CraftingElement<AdvancedElectronicsAssemblyItem>()
                });
            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 20; // Defines how much experience is gained when crafted.

            // Defines the amount of labor required and the required skill to add labor
            this.LaborInCalories = CreateLaborInCaloriesValue(300,typeof(IndustrySkill));


            // Defines our crafting time for the recipe
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(AdvancedElectronicsAssemblyRecipe), start: 25, skillType: typeof(IndustrySkill));

            // Perform pre/post initialization for user mods and initialize our recipe instance with the display name "Electronics Assembly"
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Advanced Electronics Assembly"), recipeType: typeof(AdvancedElectronicsAssemblyRecipe));
            this.ModsPostInitialize();

            // Register our RecipeFamily instance with the crafting system so it can be crafted.
            CraftingComponent.AddRecipe(tableType: typeof(ElectricMachinistTableObject), recipeFamily: this);
        }

        /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
        partial void ModsPreInitialize();

        /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
        partial void ModsPostInitialize();
    }
}
