using System.Collections.Generic;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Players;
using Eco.Mods.TechTree;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Craftable survey drone item (R11). Deliberately just an <see cref="Item"/> in
    /// this unit, not a placeable WorldObject: it lives in a player's or a
    /// <see cref="DroneDockObject"/>'s inventory until inserted into a dock's storage slot,
    /// which pairs it (see DroneDockObject.OnDockStorageChanged). See <see cref="SurveyDrone"/>
    /// below for the physical roaming WorldObject a dock would dispatch -- actually
    /// spawning/dispatching one from a paired dock (and wiring SurveyDroneObject.SetOwner into
    /// that dispatch/pairing flow) is still a future unit's concern (U8); this unit (U7)
    /// only makes the drone's invulnerability, free-roam, and owner-stamping surface
    /// available on the entity.
    /// </summary>
    [Serialized]
    [Weight(500)]
    [LocDisplayName("Survey Drone")]
    [LocDescription("A craftable ground survey drone. Insert into a Drone Dock to pair it for dispatch.")]
    [Ecopedia("Crafted Objects", "Advanced Electronics", true, true, null)]
    public class SurveyDroneItem : Item
    {
    }

    /// <summary>
    /// The physical roaming drone WorldObject that a <see cref="DroneDockObject"/> dispatches
    /// (U7, R3/R4/R5). Spawned and destroyed by <see cref="DroneDockObject.OnDockStorageChanged"/>
    /// when a <see cref="SurveyDroneItem"/> is inserted into / removed from the dock --
    /// see that method for the pairing-to-spawn wiring (an orchestrator-level integration
    /// pass connecting U1/U2/U5/U7/U8's independently-built pieces, since no single unit's
    /// Files list owned the actual spawn call). This unit (U7) contributes the class
    /// itself as the shell the invulnerability/free-roam/attribution requirements attach
    /// to, plus the <c>[RequireComponent]</c> declarations that pull in
    /// <see cref="DroneMoverComponent"/> (U2), <see cref="OreSensorComponent"/> (U5), and
    /// <see cref="DroneLifecycle"/> (U8) automatically on spawn.
    ///
    /// Per KTD7:
    /// <list type="bullet">
    /// <item><description>
    /// R3 (invulnerable to tool/animal damage): this class deliberately implements no
    /// damage-taking interface and attaches no health/damage component -- invulnerability
    /// is the absence of a damage surface, not a "take zero damage" handler. Confirmed by
    /// reflecting over the exact Eco.ReferenceAssemblies build this project compiles
    /// against (0.13.0.4-beta-release-1024, Eco.Gameplay.dll / Eco.Mods.dll /
    /// Eco.Simulation.dll): the two damage-taking surfaces that exist in this API surface
    /// are <c>Eco.Gameplay.Interactions.IDamageable</c> (what tools call to hit
    /// something) and <c>Eco.Simulation.Agents.ICanTakeDamage</c> (what
    /// <c>TryDamage</c> is called against); across all three assemblies the only
    /// implementers are <c>Player</c>, <c>User</c>, and <c>AnimalEntity</c> (and its
    /// TechTree subclasses, e.g. Wolf, Coyote). <c>WorldObject</c> itself implements
    /// neither, and no stock WorldObject subclass in those assemblies implements either
    /// one -- there is no vanilla "structure health" surface for WorldObjects to opt out
    /// of. <see cref="SurveyDrone"/> follows the same pattern: implement neither, attach
    /// nothing damage-related, and it is invulnerable by construction.
    /// </description></item>
    /// <item><description>
    /// R4 (free-roam, crosses claims): movement is driven entirely by
    /// <see cref="DroneMoverComponent"/> (U2), required here via
    /// <c>[RequireComponent]</c>. That component's Tick() only reads/writes
    /// Position/Rotation and calls SyncPositionAndRotation() -- audited and confirmed it
    /// calls no claim/permission/auth API anywhere in its movement path (see this unit's
    /// report). Nothing added by this class adds one either; free-roam is simply the
    /// absence of such a check, not a bypass flag.
    /// </description></item>
    /// <item><description>
    /// R5 (owner attribution, law enforcement deferred): <see cref="OwnerName"/> /
    /// <see cref="OwnerId"/> are plain serialized fields (mirrors
    /// DroneDockObject.AssignedDistrictName's own "trivially serializable" reasoning, rather
    /// than serializing a <see cref="DroneOwnership"/> value type directly, since Eco's
    /// serializer support for custom structs was not verified), stamped via
    /// <see cref="SetOwner"/>. Wiring SetOwner into the dock's pairing/dispatch flow is
    /// left to U8. No citizenship/law-violation API is touched here -- explicitly
    /// deferred per KTD7, even though a drone crossing a claim boundary is an obvious
    /// integration point for one.
    /// </description></item>
    /// </list>
    /// </summary>
    [Serialized]
    [RequireComponent(typeof(DroneMoverComponent))]
    [RequireComponent(typeof(OreSensorComponent))]
    [RequireComponent(typeof(DroneLifecycle))]
    // TEMPORARY v7: the container probe lives here rather than on the dock. A list whose elements
    // do not deserialize to View disconnects the client from the whole object on interact, so it
    // is quarantined onto the object the mod can afford to lose. Delete with the other probes.
    [RequireComponent(typeof(UIContainerProbeComponent))]
    public class SurveyDroneObject : WorldObject
    {
        /// <summary>
        /// Registers the drone's single-block placement footprint. Required even though
        /// the drone is spawned via WorldObjectManager.ForceAdd (not player-placed) --
        /// a WorldObject with no registered occupancy has no valid footprint and the
        /// spawn is silently rejected the same way manual placement is. See DroneDockObject's
        /// static constructor for the full explanation (copied from the Advanced
        /// Mixology reference mod).
        /// </summary>
        static SurveyDroneObject()
        {
            AddOccupancy<SurveyDroneObject>(new List<BlockOccupancy>
            {
                new BlockOccupancy(new Vector3i(0, 0, 0)),
            });
        }

        public override LocString DisplayName => Localizer.DoStr("Survey Drone");

        /// <summary>Display name of the owner this drone acts on behalf of, or null if never stamped.</summary>
        [Serialized]
        public string OwnerName { get; private set; }

        /// <summary>Eco user ID of the owner, or 0 if never stamped.</summary>
        [Serialized]
        public int OwnerId { get; private set; }

        /// <summary>True once <see cref="SetOwner"/> has stamped a real user.</summary>
        public bool HasOwner => this.OwnerId != 0;

        /// <summary>
        /// Stamps this drone's owner (R5) from the acting user. A plain setter here --
        /// mirrors DroneDockObject.SetAssignedDistrict -- so SurveyDroneObject itself does not need
        /// to know about the dock/pairing/chat-command layers that decide WHEN to call
        /// it (that wiring is U8's job). Delegates the actual (name, id) assignment to
        /// <see cref="DroneOwnership.FromUser"/>.
        /// </summary>
        public void SetOwner(User user)
        {
            var ownership = DroneOwnership.FromUser(user);
            this.OwnerName = ownership.OwnerName;
            this.OwnerId = ownership.OwnerId;
        }
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

            // ASSUMPTION: see DroneDockRecipe in DroneDockObject.cs -- same crafting-table
            // pick (ElectricMachinistTableObject), same caveat about no dedicated mod
            // bench existing yet.
            CraftingComponent.AddRecipe(typeof(ElectricMachinistTableObject), this);
        }
    }
}
