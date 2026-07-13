using System.Collections.Generic;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Mods.TechTree;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace AdvancedElectronics
{
    /// <summary>
    /// Craftable survey drone item (R11). Deliberately just an <see cref="Item"/> in
    /// this unit, not a placeable WorldObject: it lives in a player's or a
    /// <see cref="DroneDock"/>'s inventory until inserted into a dock's storage slot,
    /// which pairs it (see DroneDock.OnDockStorageChanged). The physical roaming
    /// WorldObject a dock would dispatch is a future unit's concern -- see
    /// docs/solutions/best-practices/eco-013-server-driven-movement.md for the proven
    /// movement approach it should build on.
    /// </summary>
    [Serialized]
    [Weight(500)]
    [LocDisplayName("Survey Drone")]
    [LocDescription("A craftable ground survey drone. Insert into a Drone Dock to pair it for dispatch.")]
    [Ecopedia("Crafted Objects", "Advanced Electronics", true, true, null)]
    public class SurveyDroneItem : Item
    {
    }

    /// <summary>Recipe unlocking <see cref="SurveyDroneItem"/>.</summary>
    public class SurveyDroneRecipe : RecipeFamily
    {
        // Eco force-creates one instance of every RecipeFamily-derived type at startup
        // (RecipeFamily carries [ForceCreateViewAllDerived]) -- registration belongs in
        // the instance constructor, mirroring vanilla recipes (e.g. StorageChestRecipe).
        public SurveyDroneRecipe()
        {
            var recipe = new Recipe(
                "SurveyDrone",
                Localizer.DoStr("Survey Drone"),
                new IngredientElement[]
                {
                    new IngredientElement(typeof(AdvancedCircuitItem), 6, true),
                    new IngredientElement(typeof(CopperWiringItem), 4, true),
                    new IngredientElement(typeof(PlasticItem), 4, true),
                },
                new CraftingElement[]
                {
                    new CraftingElement<SurveyDroneItem>(1),
                });

            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 5;
            this.LaborInCalories = new ConstantValue(400);
            this.CraftMinutes = new ConstantValue(10);
            this.Initialize(Localizer.DoStr("Survey Drone"), typeof(SurveyDroneRecipe));

            // ASSUMPTION: see DroneDockRecipe in DroneDock.cs -- same crafting-table
            // pick (ElectricMachinistTableObject), same caveat about no dedicated mod
            // bench existing yet.
            CraftingComponent.AddRecipe(typeof(ElectricMachinistTableObject), this);
        }
    }
}
