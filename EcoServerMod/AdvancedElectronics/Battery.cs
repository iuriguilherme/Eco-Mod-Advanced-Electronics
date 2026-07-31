
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
    using Eco.Shared.SharedTypes;
    using Eco.World;
    using Eco.World.Blocks;
    using Eco.World.Water;
    using Eco.Gameplay.Pipes;
    using Eco.Gameplay.Pipes.LiquidComponents;
    using Eco.Core.Controller;
    using Eco.Gameplay.Items.Recipes;
    using Eco.Shared.Graphics;
    using Eco.World.Color;

    [RequiresSkill(typeof(AdvancedElectronicsSkill), 1)]
    [Ecopedia("Blocks", "Electronics", subPageName: "Battery Item")]
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
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(NitricAcidItem), 4, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(IronConcentrateItem), 2, typeof(AdvancedElectronicsSkill)),
                    // TODO: v14 item
                    //new IngredientElement(typeof(SulfuricAcidItem), 2, typeof(AdvancedElectronicsSkill)),
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
            CraftingComponent.AddRecipe(tableType: typeof(AdvancedElectronicsAssemblyObject), recipeFamily: this);
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

    [Serialized]
    [Solid]
    [RequiresSkill(typeof(AdvancedElectronicsSkill), 1)]
    public partial class BatteryBlock :
        PickupableBlock
        , IRepresentsItem
    {
        public virtual Type RepresentedItemType { get { return typeof(BatteryItem); } }
    }

    [Serialized]
    [LocDisplayName("Battery")]
    [LocDescription("A portable electric energy container.")]
    [MaxStackSize(10)]
    [Weight(30000)]
    [Fuel(80000)][Tag("Fuel")]
    // TODO: Create Ecopedia page
    //[Ecopedia("Blocks", "Electronics", createAsSubPage: true)]
    // TODO: Create this tag
    [Tag("Electric Fuel")]
    public partial class BatteryItem :
 
    BlockItem<BatteryBlock>
    {
        public override LocString DisplayNamePlural { get { return Localizer.DoStr("Batteries"); } }

        public override bool CanStickToWalls { get { return false; } }
    }

}
