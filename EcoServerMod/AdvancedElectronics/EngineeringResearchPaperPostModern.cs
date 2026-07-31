
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
    using Eco.Gameplay.Settlements;
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
	using Eco.Gameplay.Housing.PropertyValues;


    [RequiresSkill(typeof(IndustrySkill), 1)]
    [Ecopedia("Items", "Research Papers", subPageName: "Engineering Research Paper Post Modern Item")]
    public partial class EngineeringResearchPaperPostModernRecipe : RecipeFamily
    {
        public EngineeringResearchPaperPostModernRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "EngineeringResearchPaperPostModern",  //noloc
                displayName: Localizer.DoStr("Engineering Research Paper Post Modern"),

                // Defines the ingredients needed to craft this recipe. An ingredient items takes the following inputs
                // type of the item, the amount of the item, the skill required, and the talent used.
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(SteelAxleItem), 8,typeof(IndustrySkill)),
                    new IngredientElement(typeof(SteelPlateItem), 8,typeof(IndustrySkill)),
                    new IngredientElement(typeof(SteelGearItem), 20,typeof(IndustrySkill)),
                    new IngredientElement(typeof(InkItem), 4, true),
                    new IngredientElement(typeof(PaperItem), 20, true),
                },
                // Define our recipe output items.
                // For every output item there needs to be one CraftingElement entry with the type of the final item and the amount
                // to create.
                items: new List<CraftingElement>
                {
                    new CraftingElement<EngineeringResearchPaperModernItem>()
                });
            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 3; // Defines how much experience is gained when crafted.

            // Defines the amount of labor required and the required skill to add labor
            this.LaborInCalories = CreateLaborInCaloriesValue(600,typeof(IndustrySkill));


            // Defines our crafting time for the recipe
            this.CraftMinutes = CreateCraftTimeValue(1);

            // Perform pre/post initialization for user mods and initialize our recipe instance with the display name "Engineering Research Paper Post Modern"
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Engineering Research Paper Post Modern"), recipeType: typeof(EngineeringResearchPaperPostModernRecipe));
            this.ModsPostInitialize();

            // Register our RecipeFamily instance with the crafting system so it can be crafted.
            CraftingComponent.AddRecipe(tableType: typeof(LaboratoryObject), recipeFamily: this);
        }

        /// <summary>Hook for mods to customize RecipeFamily before initialization. You can change recipes, xp, labor, time here.</summary>
        partial void ModsPreInitialize();

        /// <summary>Hook for mods to customize RecipeFamily after initialization, but before registration. You can change skill requirements here.</summary>
        partial void ModsPostInitialize();
    }
    
    [Serialized] // Tells the save/load system this object needs to be serialized. 
    [LocDisplayName("Engineering Research Paper Post Modern")] // Defines the localized name of the item.
    [Weight(10)] // Defines how heavy EngineeringResearchPaperPostModern is.
    [Ecopedia("Items", "Research Papers", createAsSubPage: true)]
    // TODO: add this tag so we can use it
    //[Tag("Post Modern Research")]
    [Tag("Research")]
    [LocDescription("A document containing important research information. Used to discover new skills at the research table.")] //The tooltip description for the item.
    public partial class EngineeringResearchPaperPostModernItem : Item    {

        

    }
}