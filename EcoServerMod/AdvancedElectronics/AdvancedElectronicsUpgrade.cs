namespace Eco.Mods.TechTree
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using Eco.Gameplay.Blocks;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.DynamicValues;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Modules;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Skills;
    using Eco.Gameplay.Systems;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Shared.Localization;
    using Eco.Shared.Serialization;
    using Eco.Shared.Utils;
    using Eco.Core.Items;
    using Eco.World;
    using Eco.World.Blocks;
    using Eco.Gameplay.Pipes;
    using Eco.Core.Controller;
    using Eco.Gameplay.Items.Recipes;

    /// <summary>
    /// The plugin module for <see cref="AdvancedElectronicsAssemblyObject"/>, modelled on
    /// vanilla's Electronics Upgrade (Mods/__core__/AutoGen/PluginModule/
    /// ElectronicsUpgrade.cs on the dedicated server) with the skill swapped to
    /// <see cref="AdvancedElectronicsSkill"/> and the crafting table swapped to the
    /// mod's own assembly.
    ///
    /// This type is referenced by the assembly object's [AllowPluginModules] attribute, so
    /// it must exist for that file to compile at all.
    /// </summary>
    [RequiresSkill(typeof(AdvancedElectronicsSkill), 7)]
    // TODO: Add this in Ecopedia
    //[Ecopedia("Upgrade Modules", "Specialty Upgrades", subPageName: "Advanced Electronics Upgrade Item")]
    public partial class AdvancedElectronicsUpgradeRecipe : RecipeFamily
    {
        public AdvancedElectronicsUpgradeRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "AdvancedElectronicsUpgrade",  //noloc
                displayName: Localizer.DoStr("Advanced Electronics Upgrade"),

                // Defines the ingredients needed to craft this recipe. An ingredient items takes the following inputs
                // type of the item, the amount of the item, the skill required, and the talent used.
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(ModernUpgradeLvl4Item), 1, true),
                },
                // Define our recipe output items.
                // For every output item there needs to be one CraftingElement entry with the type of the final item and the amount
                // to create.
                items: new List<CraftingElement>
                {
                    new CraftingElement<AdvancedElectronicsUpgradeItem>()
                });
            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 4; // Defines how much experience is gained when crafted.

            // Defines the amount of labor required and the required skill to add labor
            this.LaborInCalories = CreateLaborInCaloriesValue(9000, typeof(AdvancedElectronicsSkill));


            // Defines our crafting time for the recipe
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(AdvancedElectronicsUpgradeRecipe), start: 18, skillType: typeof(AdvancedElectronicsSkill));

            // Perform pre/post initialization for user mods and initialize our recipe instance with the display name "Advanced Electronics Upgrade"
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Advanced Electronics Upgrade"), recipeType: typeof(AdvancedElectronicsUpgradeRecipe));
            this.ModsPostInitialize();

            // Register our RecipeFamily instance with the crafting system so it can be crafted.
            CraftingComponent.AddRecipe(tableType: typeof(AdvancedElectronicsAssemblyObject), recipeFamily: this);
        }

        /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
        partial void ModsPreInitialize();

        /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
        partial void ModsPostInitialize();
    }

    [Serialized]
    [LocDisplayName("Advanced Electronics Upgrade")]
    [LocDescription("Modern Upgrade that greatly increases efficiency when crafting Advanced Electronics recipes.")]
    [Weight(1)]
    [Ecopedia("Upgrade Modules", "Specialty Upgrades", createAsSubPage: true)]
    [Tag("Upgrade")]
    public partial class AdvancedElectronicsUpgradeItem :
        EfficiencyModule
    {

        public AdvancedElectronicsUpgradeItem() : base(
            ModuleTypes.ResourceEfficiency | ModuleTypes.SpeedEfficiency,
            0.75f + 0.05f,
            typeof(AdvancedElectronicsSkill),
            0.75f
        ) { }
    }
}
