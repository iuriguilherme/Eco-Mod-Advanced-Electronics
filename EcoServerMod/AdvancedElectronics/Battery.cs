
namespace Eco.Mods.TechTree
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using Eco.Gameplay.Blocks;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.DynamicValues;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Skills;
    using Eco.Gameplay.Systems;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Core.Items;
    using Eco.Shared.Localization;
    using Eco.Shared.Serialization;
    using Eco.Shared.Utils;
    using Eco.World;
    using Eco.World.Blocks;
    using Eco.Gameplay.Pipes;
    using Eco.Core.Controller;
    using Eco.Gameplay.Items.Recipes;

    [RequiresSkill(typeof(AdvancedElectronicsSkill), 1)]
    [Ecopedia("Items", "Electronics", subPageName: "Battery Item")]
    public partial class BatteryRecipe : RecipeFamily
    {
        public BatteryRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "Battery",  //noloc
                displayName: Localizer.DoStr("Battery"),

                // Defines the ingredients needed to craft this recipe. An ingredient items takes the following inputs
                // type of the item, the amount of the item, the skill required, and the talent used.
                // Every ingredient keeps the (Type, float, Type skill) overload: it builds a ModuleModifiedValue,
                // which is what lets the Advanced Electronics Upgrade reduce these quantities. The (Type, float, bool)
                // overload would build a ConstantValue no module can touch.
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(NitricAcidItem), 4, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(CopperConcentrateItem), 2, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(PlasticItem), 10, typeof(AdvancedElectronicsSkill)),
                },
                // Define our recipe output items.
                // For every output item there needs to be one CraftingElement entry with the type of the final item and the amount
                // to create.
                items: new List<CraftingElement>
                {
                    new CraftingElement<BatteryItem>(1)
                });
            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 0.5f; // Defines how much experience is gained when crafted.

            // Defines the amount of labor required and the required skill to add labor
            this.LaborInCalories = CreateLaborInCaloriesValue(60, typeof(AdvancedElectronicsSkill));


            // Defines our crafting time for the recipe
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(BatteryRecipe), start: 0.8f, skillType: typeof(AdvancedElectronicsSkill));

            // Perform pre/post initialization for user mods and initialize our recipe instance with the display name "Battery"
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Battery"), recipeType: typeof(BatteryRecipe));
            this.ModsPostInitialize();

            // Register our RecipeFamily instance with the crafting system so it can be crafted.
            CraftingComponent.AddRecipe(tableType: typeof(ElectronicsAssemblyObject), recipeFamily: this);
        }

        /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
        partial void ModsPreInitialize();

        /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
        partial void ModsPostInitialize();
    }

    // TODO: v14 Recipe
    // [RequiresSkill(typeof(AdvancedElectronicsSkill), 3)]
    // public partial class SulfuricBatteryRecipe : RecipeFamily
    // {
    //     public SulfuricBatteryRecipe()
    //     {
    //         var recipe = new Recipe();
    //         recipe.Init(
    //             name: "SulfuricBattery",  //noloc
    //             displayName: Localizer.DoStr("Sulfuric Battery"),

    //             // Defines the ingredients needed to craft this recipe. An ingredient items takes the following inputs
    //             // type of the item, the amount of the item, the skill required, and the talent used.
    //             ingredients: new List<IngredientElement>
    //             {
    //                 new IngredientElement(typeof(NitricAcidItem), 2, typeof(AdvancedElectronicsSkill)),
    //                 new IngredientElement(typeof(IronConcentrateItem), 1, typeof(AdvancedElectronicsSkill)),
    //                 new IngredientElement(typeof(SulfuricAcidItem), 2, typeof(AdvancedElectronicsSkill)),
    //                 new IngredientElement(typeof(PlasticItem), 10, typeof(AdvancedElectronicsSkill)),
    //             },
    //             // Define our recipe output items.
    //             // For every output item there needs to be one CraftingElement entry with the type of the final item and the amount
    //             // to create.
    //             items: new List<CraftingElement>
    //             {
    //                 new CraftingElement<BatteryItem>(1)
    //             });
    //         this.Recipes = new List<Recipe> { recipe };
    //         this.ExperienceOnCraft = 0.5f; // Defines how much experience is gained when crafted.

    //         // Defines the amount of labor required and the required skill to add labor
    //         this.LaborInCalories = CreateLaborInCaloriesValue(60, typeof(AdvancedElectronicsSkill));


    //         // Defines our crafting time for the recipe
    //         this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(SulfuricBatteryRecipe), start: 0.8f, skillType: typeof(AdvancedElectronicsSkill));

    //         // Perform pre/post initialization for user mods and initialize our recipe instance with the display name "SulfuricBattery"
    //         this.ModsPreInitialize();
    //         this.Initialize(displayText: Localizer.DoStr("Sulfuric Battery"), recipeType: typeof(SulfuricBatteryRecipe));
    //         this.ModsPostInitialize();

    //         // Register our RecipeFamily instance with the crafting system so it can be crafted.
    //         CraftingComponent.AddRecipe(tableType: typeof(AdvancedElectronicsAssemblyObject), recipeFamily: this);
    //     }

    //     /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
    //     partial void ModsPreInitialize();

    //     /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
    //     partial void ModsPostInitialize();
    // }

    /// <summary>
    /// The survey drone's fuel. An inventory item with no placeable form: the mod ships an empty
    /// BlockSetContainer, so a placed battery block would draw nothing. Modelled on vanilla's
    /// Charcoal (AutoGen/Item/Charcoal.cs), which is a non-block fuel item of the same shape.
    ///
    /// Fuel(270000) is one hour of continuous drone operation at the dock's current burn rate. With a
    /// stack of five, the dock's two fuel slots hold about ten hours of surveying. Weight is sized to
    /// the player's 30 kg default pack rather than to the liquid-fuel barrel this file was derived
    /// from, so a full dock load of ten batteries is a third of a pack.
    ///
    /// The "Electric Fuel" tag is what DroneDock's FuelSupplyComponent filters on, and no other item
    /// carries it. Both tags are attributes rather than runtime registrations because only the
    /// attribute form reaches the client -- see
    /// docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md.
    ///
    /// No [SalvageCost]: adding one would change the derived garbage of every recipe that consumes a
    /// battery, which is not an effect this item is meant to have.
    /// </summary>
    [Serialized]
    [LocDisplayName("Battery")]
    [LocDescription("A portable electric energy container.")]
    [MaxStackSize(5)]
    [Weight(1000)]
    [Fuel(270000)][Tag("Fuel")]
    [Tag("Electric Fuel")]
    // TODO: Create Ecopedia page
    //[Ecopedia("Items", "Electronics", createAsSubPage: true)]
    public partial class BatteryItem : Item
    {
        public override LocString DisplayNamePlural { get { return Localizer.DoStr("Batteries"); } }
    }

}
