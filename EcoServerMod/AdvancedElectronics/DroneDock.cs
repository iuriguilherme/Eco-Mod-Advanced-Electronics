using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Items;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.NewTooltip;
using Eco.Mods.TechTree;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;
using Eco.World.Blocks;
using Quaternion = Eco.Shared.Math.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The survey drone's home point (R10): a craftable WorldObject with a single
    /// storage slot restricted to <see cref="SurveyDroneItem"/>. Inserting a drone item
    /// there pairs it to this dock and spawns its physical <see cref="SurveyDroneObject"/>
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
    [RequireComponent(typeof(PropertyAuthComponent))]
    [RequireComponent(typeof(PublicStorageComponent))]
    [RequireComponent(typeof(OccupancyRequirementComponent))]
    [Tag("Usable")]
    public class DroneDockObject : WorldObject, IRepresentsItem
    {
        // Single storage slot, drone item only (see Initialize). Slot count is a
        // vanilla-proven Initialize(int) arg; the item-type and stack-limit
        // restrictions are applied per the vanilla AddInvRestriction pattern
        // (Mods/__core__/AutoGen/WorldObject/StorageChest.cs on the dedicated server
        // ships this exact shape in source).
        private const int DockSlotCount = 1;

        public override LocString DisplayName => Localizer.DoStr("Drone Dock");

        /// <summary>
        /// Registers this object's placement footprint (a single 1x1x1 block) so the
        /// client can place it. A custom modded WorldObject MUST declare its occupancy
        /// in code via AddOccupancy&lt;T&gt; in a static constructor -- vanilla AutoGen
        /// objects get this baked by the WorldObjectTemplate.tt generator, so it never
        /// appears in their visible source, but a hand-written mod object has no
        /// generator and must do it itself. Pattern copied from the Advanced Mixology
        /// reference mod (AdvancedMixologyTableObject's static constructor). Without
        /// this, GetOccupancyInfo(typeof(DroneDockObject)) is empty and the object silently
        /// cannot be placed (no ghost, no error) -- the actual root cause of the dock
        /// being unplaceable, distinct from the occupancy attributes on the item.
        /// </summary>
        static DroneDockObject()
        {
            AddOccupancy<DroneDockObject>(new List<BlockOccupancy>
            {
                new BlockOccupancy(new Vector3i(0, 0, 0)),
            });
        }

        /// <summary>
        /// Links this WorldObject back to its craftable item (vanilla placement
        /// contract -- every placeable vanilla object implements IRepresentsItem).
        /// </summary>
        public virtual Type RepresentedItemType => typeof(DroneDockItem);

        /// <summary>The drone item currently docked here, or null if the dock is empty.</summary>
        public Item PairedDrone { get; private set; }

        /// <summary>True once a <see cref="SurveyDroneItem"/> has been inserted and paired.</summary>
        public bool HasDrone => this.PairedDrone != null;

        /// <summary>
        /// The physical <see cref="SurveyDroneObject"/> WorldObject spawned for the currently
        /// paired drone item, or null when no drone is paired. See the KNOWN LIMITATION
        /// on this class's doc comment -- not serialized.
        /// </summary>
        public SurveyDroneObject SpawnedDrone { get; private set; }

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
        /// DroneCommands.District before calling this) so DroneDockObject itself does not
        /// need to depend on the chat-command layer.
        /// </summary>
        public void SetAssignedDistrict(string districtName)
        {
            this.AssignedDistrictName = string.IsNullOrWhiteSpace(districtName) ? null : districtName;
        }

        // ---------------------------------------------------------------
        // U4: dock-owned survey areas (R1a/R2a/R3, KTD9). Areas are the dock's
        // own serialized data -- no mod-wide registry -- so they persist because
        // the dock does and are discarded with it. The dock's PropertyAuthComponent
        // is the only access gate (RPC callers enforce ConsumerAccess; these methods
        // are the plain state operations behind them, deliberately auth-free so the
        // survey-areas tab component owns the [RPC] surface). Coexists with
        // AssignedDistrictName above until the end-of-plan cleanup retires the
        // district scaffold.
        // ---------------------------------------------------------------

        /// <summary>Every survey area this dock owns. Serialized; survives a restart with the dock.</summary>
        [Serialized] public ThreadSafeList<SurveyAreaEntry> SurveyAreas { get; private set; } = new();

        /// <summary>Id of the area the drone is assigned to survey, or 0 when unassigned.</summary>
        [Serialized] public int AssignedSurveyAreaId { get; private set; }

        // Monotonic id source. Never reused, so a deleted area's id cannot collide with a
        // later one and a stale assignment to a deleted area resolves to "no area".
        [Serialized] private int nextAreaId = 1;

        /// <summary>The dock's assigned area entry, or null when unassigned or the assigned id no longer resolves.</summary>
        public SurveyAreaEntry AssignedSurveyArea =>
            this.AssignedSurveyAreaId == 0 ? null : this.SurveyAreas.FirstOrDefault(a => a.Id == this.AssignedSurveyAreaId);

        /// <summary>
        /// Creates and stores a new survey area from already-validated plots (the picker
        /// enforces the tier cap before calling this). Returns the new entry.
        /// </summary>
        public SurveyAreaEntry CreateSurveyArea(string name, IEnumerable<PlotCoord> plots)
        {
            var entry = new SurveyAreaEntry(this.nextAreaId++, string.IsNullOrWhiteSpace(name) ? "Survey Area" : name, plots);
            this.SurveyAreas.Add(entry);
            return entry;
        }

        /// <summary>Renames the area with <paramref name="id"/>, if present. No-op otherwise.</summary>
        public void RenameSurveyArea(int id, string name)
        {
            var entry = this.SurveyAreas.FirstOrDefault(a => a.Id == id);
            if (entry != null && !string.IsNullOrWhiteSpace(name))
                entry.Name = name;
        }

        /// <summary>
        /// Deletes the area with <paramref name="id"/>. If it was the assigned area, the dock
        /// becomes unassigned (R1a: deleting the assigned area unassigns rather than breaks).
        /// </summary>
        public void DeleteSurveyArea(int id)
        {
            var entry = this.SurveyAreas.FirstOrDefault(a => a.Id == id);
            if (entry == null) return;

            this.SurveyAreas.Remove(entry);
            if (this.AssignedSurveyAreaId == id)
                this.AssignedSurveyAreaId = 0;
        }

        /// <summary>
        /// Assigns the area with <paramref name="id"/> as the drone's standing target, or clears
        /// the assignment when <paramref name="id"/> is 0. Ignores an id that does not resolve to
        /// one of this dock's areas.
        /// </summary>
        public void AssignSurveyArea(int id)
        {
            if (id == 0)
            {
                this.AssignedSurveyAreaId = 0;
                return;
            }

            if (this.SurveyAreas.Any(a => a.Id == id))
                this.AssignedSurveyAreaId = id;
        }

        protected override void Initialize()
        {
            // No base.Initialize() call -- matches every vanilla object and the Advanced
            // Mixology reference mod, none of which call base from their Initialize
            // override. Component setup below reads components directly (they are already
            // attached by the [RequireComponent] declarations by the time Initialize runs).
            if (this.TryGetComponent<PublicStorageComponent>(out var storage))
            {
                // Vanilla storage-init shape (verified against the dedicated server's
                // shipped source, e.g. Mods/__core__/AutoGen/WorldObject/StorageChest.cs):
                // Initialize(slotCount), then restrictions via AddInvRestriction. The
                // previously-used 3-arg Initialize overload is never used by any vanilla
                // object and its parameter semantics were unproven.
                storage.Initialize(DockSlotCount);
                storage.Storage.AddInvRestriction(new SpecificItemTypesRestriction(new[] { typeof(SurveyDroneItem) }));
                storage.Storage.AddInvRestriction(new StackLimitRestriction(1));
                storage.Storage.OnChanged.Add(this.OnDockStorageChanged);
            }
        }

        /// <summary>
        /// Fires on any change to the dock's storage slot. Single-slot dock, so the
        /// first non-empty stack (if any) is the paired drone. Spawns the physical
        /// <see cref="SurveyDroneObject"/> WorldObject on a null-to-paired transition and
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
        /// Spawns and wires a <see cref="SurveyDroneObject"/> WorldObject for a freshly-paired
        /// drone item. Mirrors the spike's proven
        /// <c>WorldObjectManager.ForceAdd(type, user, position, rotation, bool)</c> spawn
        /// call (see docs/solutions/best-practices/eco-013-server-driven-movement.md).
        /// A null/non-<see cref="SurveyDroneObject"/> spawn result (placement rejected, or the
        /// type failed to resolve) leaves <see cref="SpawnedDrone"/> null rather than
        /// throwing -- the dock stays paired-but-not-dispatched, a degraded but safe
        /// state, since ForceAdd's exact rejection conditions are unconfirmed offline
        /// (reference assemblies' stripped method bodies -- same caveat as this file's
        /// other ASSUMPTION-flagged Eco API calls).
        /// </summary>
        private void SpawnDrone(User user)
        {
            var spawnPos = this.Position + new Vector3(1.5f, 0f, 0f);
            var obj = WorldObjectManager.ForceAdd(typeof(SurveyDroneObject), user, spawnPos, Quaternion.Identity, false) as SurveyDroneObject;
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

        /// <summary>
        /// Animation-state contract name (v1 closure plan KTD1): true while the paired
        /// drone is EnRoute or Surveying. The client prefab declares this name in its
        /// bool States array; future art binds an animator parameter to it. Frozen —
        /// renaming touches server, prefab, and bundle at once.
        /// </summary>
        internal const string WorkingStateName = "Working";

        private bool? lastPushedWorking;

        public override void Tick()
        {
            base.Tick();

            this.PushWorkingState();

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
        /// Pushes the dock's <see cref="WorkingStateName"/> animation state, change-gated
        /// so the synced AnimatedStates dictionary is only written on transitions rather
        /// than every tick (SetAnimatedState is a synced write; same-value churn has no
        /// consumer).
        /// </summary>
        private void PushWorkingState()
        {
            var working = false;
            if (this.SpawnedDrone != null && !this.SpawnedDrone.IsDestroyed
                && this.SpawnedDrone.TryGetComponent<DroneLifecycle>(out var lifecycle))
            {
                working = lifecycle.Status == DroneStatus.EnRoute || lifecycle.Status == DroneStatus.Surveying;
            }

            if (this.lastPushedWorking == working)
                return;

            this.lastPushedWorking = working;
            this.SetAnimatedState(WorkingStateName, working);
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

        /// <summary>
        /// Renders the detailed survey readout (per-ore densest-cell lines + coverage
        /// gauge, R8/R14) into this dock's in-game info window via Eco's NewTooltip
        /// system. This is the "popup panel" surface: the world-space text above the
        /// dock (<see cref="DockReadoutDisplay"/>) is deliberately reserved for the short
        /// drone status line only, so the volumetric per-ore detail lives here where it
        /// has room. <see cref="CacheAs.Disabled"/> recomputes on every view -- the same
        /// choice vanilla PumpJackItem.OilTooltip makes for live, position/state-derived
        /// data -- because the paired drone's sampled ore data changes continuously while
        /// it roams. Reads the same <see cref="SpawnedDrone"/> sensor inputs as
        /// <see cref="RefreshReadout"/> and formats them through <see cref="DockReadout"/>.
        /// </summary>
        [NewTooltip(CacheAs.Disabled, 100)]
        public LocString SurveyReadoutTooltip()
        {
            IReadOnlyList<(string OreType, DensestCellResult Result)> oreResults =
                Array.Empty<(string, DensestCellResult)>();

            if (this.SpawnedDrone != null && !this.SpawnedDrone.IsDestroyed
                && this.SpawnedDrone.TryGetComponent<OreSensorComponent>(out var sensor))
            {
                oreResults = sensor.SampledOreTypes
                    .Select(oreType => (oreType, sensor.DensestCell(oreType)))
                    .ToList();
            }

            var oreLines = oreResults
                .Where(e => e.Result.Found)
                .OrderBy(e => e.OreType, StringComparer.Ordinal)
                .Take(DockReadout.MaxOreLines)
                .Select(e => DockReadout.FormatOreLine(e.OreType, e.Result))
                .ToList();

            var coverage = DockReadout.ComputeCoveragePercent(oreResults);
            var body = oreLines.Count > 0
                ? string.Join("\n", oreLines)
                : "No survey data yet.";

            return Localizer.DoStr($"Survey Readout\n{body}\nCoverage: {coverage:F0}%");
        }
    }

    /// <summary>
    /// Craftable item that places a <see cref="DroneDock"/> WorldObject. The
    /// <see cref="GetOccupancyContext"/> override is the placement contract: without it
    /// the item silently cannot be placed at all (no ghost, no error) -- every placeable
    /// vanilla item ships this exact SideAttachedContext(Down) override (see the
    /// dedicated server's Mods/__core__/AutoGen/WorldObject sources), and its absence
    /// here is what blocked the first live placement test.
    /// </summary>
    [Serialized]
    [LocDisplayName("Drone Dock")]
    [LocDescription("Home point for a survey drone. Insert a Survey Drone to pair and dispatch it; assign a survey district with /drone district <name>.")]
    [Ecopedia("Crafted Objects", "Advanced Electronics", true, true, null)]
    [Weight(1000)]
    public class DroneDockItem : WorldObjectItem<DroneDockObject>
    {
        protected override OccupancyContext GetOccupancyContext =>
            new SideAttachedContext(0 | DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(this.WorldObjectType));
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
