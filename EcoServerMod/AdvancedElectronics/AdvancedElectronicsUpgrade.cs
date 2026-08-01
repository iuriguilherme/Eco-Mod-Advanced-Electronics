
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
    using Eco.Gameplay.Garbage;
    using Eco.Gameplay.Bonuses;

    /// <summary>
    /// Efficiency module for the Advanced Electronics specialty, derived from vanilla's
    /// AutoGen/PluginModule/ElectronicsUpgrade.cs in the dedicated server's __core__ mod.
    ///
    /// Crafted at the Electronics Assembly; slots into the Robotic Assembly Line, where the drone
    /// and dock recipes live. Those have dynamic ingredients, so the module actually buys
    /// something there -- this recipe's own ingredients are static, so nothing would.
    ///
    /// The Robotic Assembly Line is a vanilla table and its [AllowPluginModules] enumerates item
    /// types, with no tag it would match us on, so accepting this module needs a UserCode override
    /// of that AutoGen file. A mod assembly cannot add the attribute itself: attributes merge
    /// across partial declarations only within one assembly. See EcoServerMod/UserCode/.
    ///
    /// Gated on AdvancedElectronicsSkill at its cap (level 7), matching how vanilla gates a
    /// specialty upgrade behind its own skill.
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
                    new IngredientElement(typeof(AdvancedCircuitItem), 3, true),
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

            // Crafted at the Electronics Assembly. Its own ingredients are static, so no upgrade
            // module affects making it -- the module matters at the Robotic Assembly Line, where
            // the drone and dock recipes it is meant to cheapen actually live.
            CraftingComponent.AddRecipe(tableType: typeof(ElectronicsAssemblyObject), recipeFamily: this);
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
    [SalvageCost(typeof(Trash), 1.0f)]
    [Ecopedia("Upgrade Modules", "Specialty Upgrades", createAsSubPage: true)]
    [Tag("Upgrade")]
    [Tag("SpecialtyModule")] //noloc
    public partial class AdvancedElectronicsUpgradeItem :
        EfficiencyModule
    {
        // v14 module shape, matching ElectronicsUpgradeItem in the shipped __core__ mod.
        //
        // The old form passed (ResourceEfficiency | SpeedEfficiency, 0.80f, skillType, 0.75f) and
        // let PluginModule derive its bonuses from those multipliers. That still compiles, but it
        // takes the four-argument constructor's default materialTierBump of 1f, which quietly
        // raises the required room tier of any table this is slotted into. Every modern specialty
        // upgrade declares ModuleTypes.None, a tier bump of zero, and its effects explicitly.
        public AdvancedElectronicsUpgradeItem() : base(ModuleTypes.None, 1f) { }

        public override float MaterialTierBump => 0f;

        /// <summary>
        /// Stated outright rather than derived, which is what "modern" buys: each effect names the
        /// action it changes and the skill it applies to. The numbers preserve what the old
        /// multipliers bought for Advanced Electronics recipes -- a quarter off both ingredients
        /// and craft time -- rather than copying the vanilla Electronics Upgrade's weaker figures.
        /// Balance is a product call; these are the previous values carried across, not a new bet.
        /// </summary>
        public override IEnumerable<Bonus> Bonuses => new[]
        {
            new Bonus
            {
                Causes  = new List<BonusCause>  { new CraftBonusCause { Action = BonusAction.ResourceCost, SkillTypes = new HashSet<Type> { typeof(AdvancedElectronicsSkill) } } },
                Effects = new List<BonusEffect> { new BonusEffectMultiplicative { Value = 0.75f, LowerIsBetter = true } },
            },
            new Bonus
            {
                Causes  = new List<BonusCause>  { new CraftBonusCause { Action = BonusAction.CraftTime, SkillTypes = new HashSet<Type> { typeof(AdvancedElectronicsSkill) } } },
                Effects = new List<BonusEffect> { new BonusEffectMultiplicative { Value = 0.75f, LowerIsBetter = true } },
            },
        };
    }
}
