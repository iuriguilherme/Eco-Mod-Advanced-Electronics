
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
    /// Efficiency module for the Advanced Electronics specialty, derived from vanilla's
    /// AutoGen/PluginModule/ElectronicsUpgrade.cs in the dedicated server's __core__ mod.
    ///
    /// Slots into <see cref="AdvancedElectronicsAssemblyObject"/> through that object's
    /// [AllowPluginModules] ItemTypes list. Gated on AdvancedElectronicsSkill at its cap
    /// (level 7), matching how vanilla gates a specialty upgrade behind its own skill.
    ///
    /// Cycle safety: this recipe requires AdvancedElectronicsSkill and outputs
    /// AdvancedElectronicsUpgradeItem, which is NOT an ingredient of
    /// AdvancedElectronicsSkillBookRecipe (that consumes research papers only). A recipe
    /// whose required skill is reachable from its own output makes that skill its own
    /// ancestor, and SkillTree.MakeNonTransitive -> GetParentSet then recurses without
    /// bound -- an uncatchable stack overflow during "Initializing skills". See
    /// docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md.
    /// </summary>
    [RequiresSkill(typeof(AdvancedElectronicsSkill), 7)]
    // TODO: Include in Ecopedia
    //[Ecopedia("Upgrade Modules", "Specialty Upgrades", subPageName: "Advanced Electronics Upgrade Item")]
    public partial class AdvancedElectronicsUpgradeRecipe : RecipeFamily
    {
        public AdvancedElectronicsUpgradeRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "AdvancedElectronicsUpgrade",  //noloc
                displayName: Localizer.DoStr("Advanced Electronics Upgrade"),

                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(ModernUpgradeLvl4Item), 1, true),
                },
                items: new List<CraftingElement>
                {
                    new CraftingElement<AdvancedElectronicsUpgradeItem>()
                });
            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 4;

            this.LaborInCalories = CreateLaborInCaloriesValue(9000, typeof(AdvancedElectronicsSkill));
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(AdvancedElectronicsUpgradeRecipe), start: 18, skillType: typeof(AdvancedElectronicsSkill));

            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Advanced Electronics Upgrade"), recipeType: typeof(AdvancedElectronicsUpgradeRecipe));
            this.ModsPostInitialize();

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
    [Tag("ModernUpgrade")] //noloc
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
