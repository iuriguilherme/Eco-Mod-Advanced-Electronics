using System.Collections.Generic;
using System.Linq;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Mods.TechTree;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace AdvancedElectronics
{
    /// <summary>
    /// The survey drone's home point (R10): a craftable WorldObject with a single
    /// storage slot restricted to <see cref="SurveyDroneItem"/>. Inserting a drone item
    /// there pairs it to this dock (R11) -- see <see cref="OnDockStorageChanged"/>.
    ///
    /// Out of scope for this unit (U1 is scaffold + pairing only): actually dispatching
    /// the paired drone to roam and return. That needs a real, movable drone
    /// WorldObject, which this unit deliberately does not create -- see
    /// docs/solutions/best-practices/eco-013-server-driven-movement.md for the proven
    /// approach (WorldObjectComponent.Tick() driving Position/Rotation +
    /// SyncPositionAndRotation()) a future unit should build the dispatch logic on.
    /// </summary>
    [Serialized]
    [RequireComponent(typeof(PropertyAuthComponent), null)]
    [RequireComponent(typeof(PublicStorageComponent), null)]
    [Tag("Usable")]
    public class DroneDock : WorldObject
    {
        // ASSUMPTION -- verify against a live server (see U1 verification note):
        // PublicStorageComponent.Initialize(int, int, InventoryRestriction[]) is read
        // here as (slot count, per-slot weight capacity, restrictions), by analogy with
        // the simpler Initialize(int) overload and vanilla single-purpose slots (e.g.
        // fuel/input slots on machines). The Eco.ReferenceAssemblies package ships
        // method bodies stripped, so this parameter order could not be confirmed by
        // reading vanilla source -- only the signature. If the in-game check in this
        // unit's Verification section shows the dock accepting more than one item or
        // rejecting the drone item outright, this is the first place to look.
        private const int DockSlotCount = 1;
        private const int DockSlotWeightCapacity = 1000;

        public override LocString DisplayName => Localizer.DoStr("Drone Dock");

        /// <summary>The drone item currently docked here, or null if the dock is empty.</summary>
        public Item PairedDrone { get; private set; }

        /// <summary>True once a <see cref="SurveyDroneItem"/> has been inserted and paired.</summary>
        public bool HasDrone => this.PairedDrone != null;

        protected override void Initialize()
        {
            base.Initialize();

            if (this.TryGetComponent<PublicStorageComponent>(out var storage))
            {
                storage.Initialize(DockSlotCount, DockSlotWeightCapacity, new InventoryRestriction[]
                {
                    new SpecificItemTypesRestriction(new[] { typeof(SurveyDroneItem) }),
                });
                storage.Storage.OnChanged.Add(this.OnDockStorageChanged);
            }
        }

        /// <summary>
        /// Fires on any change to the dock's storage slot. Single-slot dock, so the
        /// first non-empty stack (if any) is the paired drone.
        /// </summary>
        private void OnDockStorageChanged(User user)
        {
            if (!this.TryGetComponent<PublicStorageComponent>(out var storage))
                return;

            var stack = storage.Storage.NonEmptyStacks.FirstOrDefault();
            this.PairedDrone = stack?.Item;

            // TODO (future unit): once HasDrone flips true, dispatch the paired drone
            // from this dock and route it back here on return -- the rest of R10/R11.
            // This unit only tracks pairing state.
        }
    }

    /// <summary>Craftable item that places a <see cref="DroneDock"/> WorldObject.</summary>
    [Serialized]
    public class DroneDockItem : WorldObjectItem<DroneDock>
    {
    }

    /// <summary>Recipe unlocking <see cref="DroneDockItem"/>.</summary>
    public class DroneDockRecipe : RecipeFamily
    {
        // Eco force-creates one instance of every RecipeFamily-derived type at startup
        // (RecipeFamily carries [ForceCreateViewAllDerived]) -- registration belongs in
        // the instance constructor, mirroring vanilla recipes (e.g. StorageChestRecipe).
        public DroneDockRecipe()
        {
            var recipe = new Recipe(
                "DroneDock",
                Localizer.DoStr("Drone Dock"),
                new IngredientElement[]
                {
                    new IngredientElement(typeof(SteelPlateItem), 8, true),
                    new IngredientElement(typeof(BasicCircuitItem), 4, true),
                },
                new CraftingElement[]
                {
                    new CraftingElement<DroneDockItem>(1),
                });

            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 5;
            this.LaborInCalories = new ConstantValue(400);
            this.CraftMinutes = new ConstantValue(10);
            this.Initialize(Localizer.DoStr("Drone Dock"), typeof(DroneDockRecipe));

            // ASSUMPTION: ElectricMachinistTableObject picked as the most thematically
            // fitting vanilla crafting table for an "Advanced Electronics" bench. No
            // dedicated mod crafting table exists yet -- revisit if/when one is designed.
            CraftingComponent.AddRecipe(typeof(ElectricMachinistTableObject), this);
        }
    }
}
