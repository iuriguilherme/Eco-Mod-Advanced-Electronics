namespace Eco.Mods.TechTree
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using Eco.Core.Items;
    using Eco.Core.Utils;
    using Eco.Core.Utils.AtomicAction;
    using Eco.Gameplay.Blocks;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.DynamicValues;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Property;
    using Eco.Gameplay.Skills;
    using Eco.Gameplay.Systems;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Shared.Localization;
    using Eco.Shared.Serialization;
    using Eco.Shared.Services;
    using Eco.Shared.Utils;
    using Eco.Gameplay.Systems.NewTooltip;
    using Eco.Core.Controller;
    using Eco.Gameplay.Items.Recipes;

    [Serialized]
    [LocDisplayName("Advanced Electronics")]
    [LocDescription("Advanced Electronics uses advanced technology to produce automated machines. Levels up by crafting related recipes.")]
    [Ecopedia("Professions", "Engineer", createAsSubPage: true)]
    [RequiresSkill(typeof(EngineerSkill), 0), Tag("Engineer Specialty"), Tier(5)]
    [Tag("Specialty")]
    [Tag("Teachable")]
    // Draws vanilla's own skills emblem. The client keeps ONE flat icon registry filled from
    // vanilla's Addressables plus every mod bundle, so any name vanilla registered is a name a
    // mod can ask for -- no asset, no scene object, no bundle rebuild.
    //
    // WHY HasStaticIcon RATHER THAN [HasIcon("Skills")]. GetIconName reads the static one FIRST
    // and unconditionally (ControllerMarshalerService.cs:414); the [HasIcon] path below it takes
    // the first match of an INHERITED lookup, and every Item already inherits Item's own bare
    // [HasIcon] whose IconName is null -- so the name falls back to the class name and the
    // explicit one is ignored. That is why vanilla only ever passes a name to [HasIcon] on
    // components, never on an Item subclass: it does not work there.
    //
    // See docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md
    [HasStaticIcon(nameof(StaticIconName))]
    public partial class AdvancedElectronicsSkill : Skill
    {

        /// <summary>The vanilla icon this draws. Read by [HasStaticIcon] above.</summary>
        public static string StaticIconName(Type type) => "Skills";

        public override void OnLevelUp(User user)
        {
            user.Skillset.AddExperience(typeof(SelfImprovementSkill), 20, Localizer.DoStr("for leveling up another specialization."));
        }


        public static MultiplicativeStrategy MultiplicativeStrategy =
            new MultiplicativeStrategy(new float[] { 
                1,
                1 - 0.2f,
                1 - 0.25f,
                1 - 0.3f,
                1 - 0.35f,
                1 - 0.4f,
                1 - 0.45f,
                1 - 0.5f,
            });
        public override MultiplicativeStrategy MultiStrategy => MultiplicativeStrategy;

        public static AdditiveStrategy AdditiveStrategy =
            new AdditiveStrategy(new float[] { 
                0,
                0.5f,
                0.55f,
                0.6f,
                0.65f,
                0.7f,
                0.75f,
                0.8f,
            });
        public override AdditiveStrategy AddStrategy => AdditiveStrategy;
        public override int MaxLevel { get { return 7; } }
        public override int Tier { get { return 5; } }
        public override int SpecialtyCost => 5;
    }

    [Serialized]
    [Weight(1000)]
    [LocDisplayName("Advanced Electronics Skill Book")]
    [Ecopedia("Items", "Skill Books", createAsSubPage: true)]
    // Draws vanilla's own generic skill book. The client keeps ONE flat icon registry filled from
    // vanilla's Addressables plus every mod bundle, so any name vanilla registered is a name a
    // mod can ask for -- no asset, no scene object, no bundle rebuild.
    //
    // WHY HasStaticIcon RATHER THAN [HasIcon("Skill Book")]. GetIconName reads the static one FIRST
    // and unconditionally (ControllerMarshalerService.cs:414); the [HasIcon] path below it takes
    // the first match of an INHERITED lookup, and every Item already inherits Item's own bare
    // [HasIcon] whose IconName is null -- so the name falls back to the class name and the
    // explicit one is ignored. That is why vanilla only ever passes a name to [HasIcon] on
    // components, never on an Item subclass: it does not work there.
    //
    // See docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md
    [HasStaticIcon(nameof(StaticIconName))]
    public partial class AdvancedElectronicsSkillBook : SkillBook<AdvancedElectronicsSkill, AdvancedElectronicsSkillScroll>
    {
        /// <summary>The vanilla icon this draws. Read by [HasStaticIcon] above.</summary>
        public static string StaticIconName(Type type) => "Skill Book";
    }

    [Serialized]
    [Weight(100)]
    [LocDisplayName("Advanced Electronics Skill Scroll")]
    // The Ecopedia page is not decoration, and vanilla's skill scrolls do not have one.
    // It is what makes this class visible to the client's enumerated missing-icon report:
    // that report walks Ecopedia categories, pages and subpages only, and takes each page's
    // icon name from its declaring type, so a class with no page can never appear in it.
    // Remove the page and the scroll's icon becomes uncheckable in one log read.
    // See docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md (KTD3).
    [Ecopedia("Items", "Skill Books", createAsSubPage: true)]
    // Draws vanilla's own generic skill scroll. The client keeps ONE flat icon registry filled from
    // vanilla's Addressables plus every mod bundle, so any name vanilla registered is a name a
    // mod can ask for -- no asset, no scene object, no bundle rebuild.
    //
    // WHY HasStaticIcon RATHER THAN [HasIcon("Skill Scrolls")]. GetIconName reads the static one FIRST
    // and unconditionally (ControllerMarshalerService.cs:414); the [HasIcon] path below it takes
    // the first match of an INHERITED lookup, and every Item already inherits Item's own bare
    // [HasIcon] whose IconName is null -- so the name falls back to the class name and the
    // explicit one is ignored. That is why vanilla only ever passes a name to [HasIcon] on
    // components, never on an Item subclass: it does not work there.
    //
    // See docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md
    [HasStaticIcon(nameof(StaticIconName))]
    public partial class AdvancedElectronicsSkillScroll : SkillScroll<AdvancedElectronicsSkill, AdvancedElectronicsSkillBook>
    {
        /// <summary>The vanilla icon this draws. Read by [HasStaticIcon] above.</summary>
        public static string StaticIconName(Type type) => "Skill Scrolls";
    }


    [RequiresSkill(typeof(ElectronicsSkill), 1)]
    // TODO: Include in Ecopedia
    //[Ecopedia("Professions", "Engineer", subPageName: "Advanced Electronics Skill Book Item")]
    public partial class AdvancedElectronicsSkillBookRecipe : RecipeFamily
    {
        public AdvancedElectronicsSkillBookRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "Advanced Electronics",  //noloc
                displayName: Localizer.DoStr("Advanced Electronics Skill Book"),

                // Defines the ingredients needed to craft this recipe. An ingredient items takes the following inputs
                // type of the item, the amount of the item, the skill required, and the talent used.
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(EngineeringResearchPaperModernItem), 20, typeof(ElectronicsSkill)),
                    new IngredientElement(typeof(MetallurgyResearchPaperModernItem), 20, typeof(ElectronicsSkill)),
                    new IngredientElement(typeof(EngineeringResearchPaperPostModernItem), 10, typeof(ElectronicsSkill)),
                    new IngredientElement("Basic Research", 30, typeof(ElectronicsSkill)), //noloc
                    new IngredientElement("Advanced Research", 20, typeof(ElectronicsSkill)), //noloc
                    new IngredientElement("Modern Research", 10, typeof(ElectronicsSkill)), //noloc
                },
                // Define our recipe output items.
                // For every output item there needs to be one CraftingElement entry with the type of the final item and the amount
                // to create.
                items: new List<CraftingElement>
                {
                    new CraftingElement<AdvancedElectronicsSkillBook>()
                });
            this.Recipes = new List<Recipe> { recipe };

            // Defines the amount of labor required and the required skill to add labor
            this.LaborInCalories = CreateLaborInCaloriesValue(6000, typeof(ElectronicsSkill));


            // Defines our crafting time for the recipe
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(AdvancedElectronicsSkillBookRecipe), start: 30, skillType: typeof(ElectronicsSkill));

            // Perform pre/post initialization for user mods and initialize our recipe instance with the display name "Electronics Skill Book"
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Advanced Electronics Skill Book"), recipeType: typeof(AdvancedElectronicsSkillBookRecipe));
            this.ModsPostInitialize();

            // Register our RecipeFamily instance with the crafting system so it can be crafted.
            CraftingComponent.AddRecipe(tableType: typeof(LaboratoryObject), recipeFamily: this);
        }

        /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
        partial void ModsPreInitialize();

        /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
        partial void ModsPostInitialize();
    }
}
