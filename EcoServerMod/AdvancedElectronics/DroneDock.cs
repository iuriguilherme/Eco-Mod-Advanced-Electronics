using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AdvancedElectronics.Navigation;
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
using Eco.Shared.IoC;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Quaternion = Eco.Shared.Math.Quaternion;

namespace AdvancedElectronics
{
    /// <summary>
    /// The survey drone's home point (R10): a craftable WorldObject with a single
    /// storage slot restricted to <see cref="SurveyDroneItem"/>. Inserting a drone item
    /// there pairs it to this dock and spawns its physical <see cref="SurveyDrone"/>
    /// WorldObject (R11) -- see <see cref="OnDockStorageChanged"/>. Removing it despawns
    /// that WorldObject.
    ///
    /// The spawn/despawn wiring below is an orchestrator-level integration pass, not a
    /// single plan unit's Files: list -- U1 built the pairing/slot scaffold, U2/U5/U7/U8
    /// each independently built a piece the spawned drone needs (movement, sensing,
    /// invulnerability/ownership, lifecycle), and none of their Files: lists included
    /// this dock method, so it was left as a TODO until all the pieces existed to wire
    /// together. See docs/solutions/best-practices/eco-013-server-driven-movement.md for
    /// the WorldObjectManager.ForceAdd spawn pattern this follows (proven in the spike).
    ///
    /// KNOWN LIMITATION: <see cref="SpawnedDrone"/> is not <c>[Serialized]</c> -- a
    /// server restart/save-reload with a drone already paired and roaming will lose the
    /// dock's reference to its spawned WorldObject (the WorldObject itself persists via
    /// its own serialization, but this dock will no longer track/despawn it on removal).
    /// Flagged for live-server verification, not fixed here -- WorldObject references are
    /// not confirmed serializable given the reference assemblies' stripped method bodies.
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

        /// <summary>
        /// The physical <see cref="SurveyDrone"/> WorldObject spawned for the currently
        /// paired drone item, or null when no drone is paired. See the KNOWN LIMITATION
        /// on this class's doc comment -- not serialized.
        /// </summary>
        public SurveyDrone SpawnedDrone { get; private set; }

        /// <summary>
        /// Name of the survey district assigned via <c>/drone district &lt;name&gt;</c>
        /// (U4, R12), or null when unassigned. Stored as a name -- not a live
        /// <c>District</c> reference -- for two reasons: (1) it keeps this field
        /// trivially serializable, unlike a civics object graph; (2) it self-heals if
        /// the district is later renamed or deleted, since DistrictAssignment
        /// re-resolves the name on every membership check instead of trusting a stale
        /// cached reference. See DistrictAssignment.cs for the resolve/membership-test
        /// logic that reads this field back into a live District.
        /// </summary>
        [Serialized]
        public string AssignedDistrictName { get; private set; }

        /// <summary>
        /// Sets the assigned survey district by name, or clears it when
        /// <paramref name="districtName"/> is null/blank. Kept as a plain setter here
        /// (name validation against the district registry happens in
        /// DroneCommands.District before calling this) so DroneDock itself does not
        /// need to depend on the chat-command layer.
        /// </summary>
        public void SetAssignedDistrict(string districtName)
        {
            this.AssignedDistrictName = string.IsNullOrWhiteSpace(districtName) ? null : districtName;
        }

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
        /// first non-empty stack (if any) is the paired drone. Spawns the physical
        /// <see cref="SurveyDrone"/> WorldObject on a null-to-paired transition and
        /// despawns it on a paired-to-null transition (R10/R11).
        /// </summary>
        private void OnDockStorageChanged(User user)
        {
            if (!this.TryGetComponent<PublicStorageComponent>(out var storage))
                return;

            var wasPaired = this.HasDrone;
            var stack = storage.Storage.NonEmptyStacks.FirstOrDefault();
            this.PairedDrone = stack?.Item;
            var isPaired = this.HasDrone;

            if (!wasPaired && isPaired)
            {
                this.SpawnDrone(user);
            }
            else if (wasPaired && !isPaired)
            {
                this.DespawnDrone();
            }
        }

        /// <summary>
        /// Spawns and wires a <see cref="SurveyDrone"/> WorldObject for a freshly-paired
        /// drone item. Mirrors the spike's proven
        /// <c>WorldObjectManager.ForceAdd(type, user, position, rotation, bool)</c> spawn
        /// call (see docs/solutions/best-practices/eco-013-server-driven-movement.md).
        /// A null/non-<see cref="SurveyDrone"/> spawn result (placement rejected, or the
        /// type failed to resolve) leaves <see cref="SpawnedDrone"/> null rather than
        /// throwing -- the dock stays paired-but-not-dispatched, a degraded but safe
        /// state, since ForceAdd's exact rejection conditions are unconfirmed offline
        /// (reference assemblies' stripped method bodies -- same caveat as this file's
        /// other ASSUMPTION-flagged Eco API calls).
        /// </summary>
        private void SpawnDrone(User user)
        {
            var spawnPos = this.Position + new Vector3(1.5f, 0f, 0f);
            var obj = WorldObjectManager.ForceAdd(typeof(SurveyDrone), user, spawnPos, Quaternion.Identity, false) as SurveyDrone;
            if (obj == null)
                return;

            obj.SetOwner(user);
            if (obj.TryGetComponent<DroneLifecycle>(out var lifecycle))
                lifecycle.HomeDock = this;

            this.SpawnedDrone = obj;
        }

        /// <summary>Destroys the spawned drone WorldObject when the item is removed from the dock.</summary>
        private void DespawnDrone()
        {
            if (this.SpawnedDrone != null && !this.SpawnedDrone.IsDestroyed)
                WorldObjectManager.DestroyPermanently(this.SpawnedDrone);

            this.SpawnedDrone = null;
        }

        // ---------------------------------------------------------------
        // U6: dock readout (R14/R15/R8) -- text status/densest-cell lines via
        // WorldObject.SetAnimatedState(string, string), coverage gauge via
        // WorldObject.SetAnimatedState(string, float). Pure line/number formatting is
        // DockReadout's job (see that class's docs); this section only gathers the
        // live inputs (DroneLifecycle.Status, OreSensorComponent's per-ore results)
        // and pushes DockReadout's output through the real sync API, on a throttled
        // tick per KTD3's proven WorldObject/WorldObjectComponent Tick() surface
        // (confirmed virtual on WorldObject itself via reflection against
        // Eco.Gameplay.dll -- same recurring-callback surface DroneMoverComponent/
        // DroneLifecycle/OreSensorComponent already rely on, just one level up: a
        // WorldObject's own Tick() is what drives TickComponents() for all of those).
        //
        // ASSUMPTION -- verify against a live server: WorldObject.SetAnimatedState's
        // exact sync semantics (whether it diffs against the previous value itself, or
        // sends a network update on every call regardless of change) cannot be
        // confirmed offline -- Eco.ReferenceAssemblies ships method bodies stripped,
        // same caveat as this file's other ASSUMPTION-flagged Eco API calls. This is
        // exactly why the refresh below is throttled to ReadoutRefreshIntervalSeconds
        // rather than called from every raw tick: even in the worst case (no internal
        // diffing), a several-times-a-second network write for a slowly-changing text
        // panel is wasteful, so the throttle is a safe default regardless of which way
        // the unconfirmed sync semantics actually resolve.
        // ---------------------------------------------------------------

        // Named state-slot prefixes. The client-side Unity WorldObject component
        // declares a FIXED array of names in its StringStates/FloatStates inspector
        // fields (see Assets/EcoModKit/Scripts/WorldObject.cs in the Unity project) --
        // there is no dynamic/variable-length synced state, so this dock always writes
        // the same fixed set of slot names (padding unused ore-line slots with an
        // empty string) rather than a variable number of calls. Wiring the matching
        // prefab-side StringStates/FloatStates names is a follow-up Unity-side task
        // (see docs/plans/2026-07-11-001-feat-survey-drone-plan.md's U9), not built by
        // this backend-only unit.
        private const string StatusStateName = "ReadoutStatus";
        private const string OreLineStateNamePrefix = "ReadoutOre";
        private const string CoverageStateName = "ReadoutCoverage";

        private const float ReadoutRefreshIntervalSeconds = 1f;

        // ASSUMPTION -- verify against a live server: mirrors DroneLifecycle's own
        // FallbackTickDeltaSeconds constant/reasoning (same file, same justification):
        // if IWorldObjectManager.TickDeltaTime ever reads as 0, fall back to a
        // plausible interval instead of freezing the readout refresh pacing forever.
        private const float FallbackTickDeltaSeconds = 0.05f;

        private float secondsSinceLastReadoutRefresh;

        public override void Tick()
        {
            base.Tick();

            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            var deltaTime = manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;

            this.secondsSinceLastReadoutRefresh += deltaTime;
            if (this.secondsSinceLastReadoutRefresh < ReadoutRefreshIntervalSeconds)
                return;

            this.secondsSinceLastReadoutRefresh = 0f;
            this.RefreshReadout();
        }

        /// <summary>
        /// Gathers the live status/per-ore inputs from <see cref="SpawnedDrone"/>'s
        /// components (null/no-lifecycle/no-sensor all degrade gracefully to "no data"
        /// rather than throwing -- a docked-but-not-yet-spawned or still-initializing
        /// drone is a normal transient state, not an error) and pushes
        /// <see cref="DockReadout"/>'s formatted output through
        /// <see cref="WorldObject.SetAnimatedState"/>.
        /// </summary>
        private void RefreshReadout()
        {
            DroneStatus? status = null;
            IReadOnlyList<(string OreType, DensestCellResult Result)> oreResults =
                Array.Empty<(string, DensestCellResult)>();

            if (this.SpawnedDrone != null && !this.SpawnedDrone.IsDestroyed)
            {
                if (this.SpawnedDrone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                    status = lifecycle.Status;

                if (this.SpawnedDrone.TryGetComponent<OreSensorComponent>(out var sensor))
                {
                    oreResults = sensor.SampledOreTypes
                        .Select(oreType => (oreType, sensor.DensestCell(oreType)))
                        .ToList();
                }
            }

            var lines = DockReadout.BuildStateLines(status, oreResults);

            this.SetAnimatedState(StatusStateName, lines[0]);
            for (var i = 0; i < DockReadout.MaxOreLines; i++)
            {
                // lines[0] is the status line, so ore line i lives at lines[i + 1].
                var text = i + 1 < lines.Count ? lines[i + 1] : string.Empty;
                this.SetAnimatedState(OreLineStateNamePrefix + i, text);
            }

            this.SetAnimatedState(CoverageStateName, DockReadout.ComputeCoveragePercent(oreResults));
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
