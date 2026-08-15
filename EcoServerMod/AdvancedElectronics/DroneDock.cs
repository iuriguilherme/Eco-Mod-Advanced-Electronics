using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Core.Items;
using Eco.Core.Utils;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Players;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Systems.NewTooltip;
using Eco.Mods.TechTree;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Logging;
using Eco.Shared.Math;
using Eco.Shared.Serialization;
using Eco.Shared.Time;
using Eco.Shared.Voxel;
using Eco.World.Blocks;
using static Eco.Gameplay.Components.PartsComponent;
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
    // SurveyComponent is no longer required here (R29, R44, R45; U15) -- the survey drone
    // installs it, the same way the mining drone installs the Mining tab, so a dock shows
    // only the tab of whichever drone is slotted. Removing a required component deletes it
    // from every already-placed dock at the next server load (see
    // docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md);
    // that is the intended outcome here.
    // TEMPORARY: UI vocabulary probes. Remove once the layout brainstorm has its screenshots.
    //
    // v8 brings the container probe back onto the dock. v7 quarantined it on the drone to keep a
    // possible disconnect off the dock, but the drone renders no tabs at all, so the probe never
    // ran and the container question stayed untested. The dock is the only object in this mod
    // that reliably shows a mod tab, so the question cannot be answered anywhere else.
    //
    // Accepted risk: if a list of View elements still crashes, the dock becomes un-interactable
    // until the next deploy. ContainerProbeNote is a plain StringDisplay declared first, so a
    // blank tab distinguishes "component not attached" from "list crashed the view".
    // DETACHED 2026-07-31 while chasing the dock's invisibility. Correlation across four
    // live tests: every build carrying this probe rendered no dock mesh (only the floating
    // name label and the map marker); the one build without it -- HEAD during the crash
    // bisect -- rendered the dock with all six tabs. Nothing else about the dock is unique:
    // PartsComponent, the Mods hooks and base.Initialize() all also sit on the assembly
    // and/or the drone, both of which render.
    //
    // Mechanism (unconfirmed): the asset bundle was NOT rebuilt between the rendering and
    // non-rendering runs, and the server logs no exception, so the object initializes fine
    // and the client simply never builds its view. A component whose view fails to
    // decode client-side would do exactly that. Probe v5 already recorded this component's
    // container elements crashing dock interaction once before.
    //
    // The probe is temporary by its own comment and the UI brainstorm is deferred (task
    // #34), so detaching costs nothing now. The component itself is untouched -- restoring
    // is uncommenting one line.
    // DETACHED 2026-07-31 for the 0.0.3 release. The probe answered its questions -- which
    // attribute shape delivers writes, that DynamicTitle resolves a label once and never again,
    // and that a Changed() inside a setter needs the dock's tick to reach the client. Findings
    // are in docs/solutions/runtime-errors/autogen-template-binding-contract.md. The component
    // stays in the tree for the next binding question; re-attaching is uncommenting one line.
    // [RequireComponent(typeof(UIShowcaseComponent))]
    [RequireComponent(typeof(PartsComponent))]
    // Installs whatever the slotted drone declares -- fuel supply and fuel consumption today,
    // a cargo hold when some future drone brings one. The dock declares none of them itself.
    [RequireComponent(typeof(DroneModuleComponent))]
    // R26: 20 rather than the engine's default of 9, so enough linked storage to absorb a
    // mining area's output can be reached -- the vanilla Store's own radius (Engine
    // Reference), shared with the waste sorters and the largest any vanilla object takes.
    // Existing docks acquire this at the next server load (U7).
    [RequireComponent(typeof(LinkComponent))]
    [Tag("Usable")]
    public partial class DroneDockObject : WorldObject, IRepresentsItem
    {
        /// <summary>
        /// Player-facing name for the area type, shared by both dock tabs (R1, KD2). The
        /// rename is display-only -- code identifiers and persisted area names (e.g. a
        /// freshly created area's default "Survey Area N") are unchanged, so no save
        /// migration is required.
        /// </summary>
        public const string DroneAreaLabel = "Drone Area";

        // Single storage slot, drone item only (see Initialize). Slot count is a
        // vanilla-proven Initialize(int) arg; the item-type and stack-limit
        // restrictions are applied per the vanilla AddInvRestriction pattern
        // (Mods/__core__/AutoGen/WorldObject/StorageChest.cs on the dedicated server
        // ships this exact shape in source).
        private const int DockSlotCount = 1;

        /// <summary>R26: the vanilla Store's own radius, not the engine's default of 9.</summary>
        private const float LinkRadius = 20f;

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
            var occupancy = new List<BlockOccupancy>(FootprintOffsets.Length);
            foreach (var offset in FootprintOffsets)
                occupancy.Add(new BlockOccupancy(offset));
            AddOccupancy<DroneDockObject>(occupancy);
        }

        /// <summary>
        /// The dock's block footprint, as offsets from its own position.
        ///
        /// Declared once and read twice: the static constructor registers it as the
        /// engine's occupancy, and <see cref="OccupiedColumns"/> hands the same cells to
        /// the drone's pathfinder as columns it may cross. Two consumers of one list, so
        /// the pad the world reserves and the pad the drone can walk on cannot drift
        /// apart -- a dock that reserved cells its own drone treated as another player's
        /// object would be unreachable at its own doorstep.
        /// </summary>
        /// <remarks>
        /// A 4x4 pad, centred on the dock's own position: the mesh is a cube scaled
        /// 4 x 0.5 x 4 about its local origin, so it spans -2..+2 on both horizontal axes
        /// and these cells describe exactly the volume a player sees. Four cells across
        /// puts the dock's own block at the corner junction of the middle four rather than
        /// at a cell centre -- which is what makes the pad's centre, and so the park point,
        /// the dock's position itself.
        ///
        /// Sized from the drone, not from a round number (KD4): the HRVSTR chassis measures
        /// 3.5 x 1 x 2.75, so a 4x4 pad contains it with about half a block of margin on its
        /// long axis. That margin is thin, and a later dock model with less of it breaks the
        /// rule silently.
        /// </remarks>
        private static readonly Vector3i[] FootprintOffsets = BuildPadOffsets();

        private static Vector3i[] BuildPadOffsets()
        {
            var offsets = new Vector3i[16];
            var index = 0;
            for (var x = -2; x <= 1; x++)
                for (var z = -2; z <= 1; z++)
                    offsets[index++] = new Vector3i(x, 0, z);
            return offsets;
        }

        /// <summary>
        /// The world columns this dock's footprint covers, in world space.
        ///
        /// Every placed WorldObject writes <c>Occupied</c> blocks across its footprint,
        /// and the pathfinder treats an occupied column as impassable. The drone's own
        /// dock is the one object it must be able to path into and out of, so these
        /// columns are passed to <see cref="DroneMoverComponent.SetDestination"/> as the
        /// exempt set on every leg -- outbound as much as homebound, since a drone parked
        /// on the pad has to cross the pad to leave it.
        ///
        /// Read from the engine's own placed footprint rather than recomputed from
        /// <see cref="FootprintOffsets"/>. The offsets are declared in the object's
        /// unrotated frame, and the engine rotates them when the object is placed; adding
        /// raw offsets to Position therefore describes the wrong cells for any dock the
        /// player turned. The consequence is not a cosmetic one -- the drone would find a
        /// row of its own pad reserved but unexempt, which is an impassable wall built out
        /// of its own dock. Asking the engine what it actually reserved cannot drift.
        /// </summary>
        internal IReadOnlyCollection<Vector3> OccupiedColumns
        {
            get
            {
                var placed = this.WorldOccupancy;
                if (placed == null)
                    return Array.Empty<Vector3>();

                var columns = new List<Vector3>(placed.Count);
                foreach (var cell in placed)
                    columns.Add(new Vector3(cell.X, cell.Y, cell.Z));
                return columns;
            }
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
        /// Every drone item this dock accepts, mapped to the WorldObject it dispatches for that
        /// item. The dock used to name <see cref="SurveyDroneItem"/> and
        /// <see cref="SurveyDroneObject"/> directly in five places, so it had no concept of "a
        /// drone" at all -- only of the survey drone. This table is the whole concept for now.
        ///
        /// DELIBERATELY MINIMAL, NOT THE END STATE. The drones share a chassis and will grow a
        /// mining variant, so the honest model is a common drone item/object abstraction that
        /// carries its own WorldObject type and owner-stamping. That reshapes the pairing and
        /// lifecycle path, which is the part of this mod least worth breaking casually, so it is
        /// planned work rather than a late-night edit. Adding a drone here in the meantime is one
        /// line plus a SetOwner case in <see cref="SpawnDrone"/>.
        /// </summary>
        private static readonly Dictionary<Type, Type> DroneObjectForItem = new Dictionary<Type, Type>
        {
            [typeof(SurveyDroneItem)]  = typeof(SurveyDroneObject),
            [typeof(HarvestDroneItem)] = typeof(HarvestDroneObject),
            [typeof(MiningDroneItem)] = typeof(MiningDroneObject),
        };

        /// <summary>
        /// The physical drone WorldObject spawned for the currently paired drone item, or null
        /// when no drone is paired. See the KNOWN LIMITATION on this class's doc comment -- not
        /// serialized.
        ///
        /// Typed as <c>WorldObject</c> rather than a concrete drone: every consumer reaches
        /// through <c>TryGetComponent</c> or <c>IsDestroyed</c>, both of which live on
        /// <c>WorldObject</c>, so nothing needed the narrower type.
        /// </summary>
        public WorldObject SpawnedDrone { get; private set; }

        // ---------------------------------------------------------------
        // U4: dock-owned survey areas (R1a/R2a/R3, KTD9). Areas are the dock's
        // own serialized data -- no mod-wide registry -- so they persist because
        // the dock does and are discarded with it. The dock's PropertyAuthComponent
        // is the only access gate (RPC callers enforce ConsumerAccess; these methods
        // are the plain state operations behind them, deliberately auth-free so the
        // survey-areas tab component owns the [RPC] surface). Areas fully replaced the
        // earlier named-district assignment (retired in U10).
        // ---------------------------------------------------------------

        /// <summary>Every survey area this dock owns. Serialized; survives a restart with the dock.</summary>
        [Serialized] public ThreadSafeList<SurveyAreaEntry> SurveyAreas { get; private set; } = new();

        /// <summary>Id of the area the drone is assigned to survey, or 0 when unassigned.</summary>
        [Serialized] public int AssignedSurveyAreaId { get; private set; }

        // ---------------------------------------------------------------
        // Material target selection. A DISPLAY-time filter: the drone records every material it
        // detects, and this narrows what the readout shows. Switching targets is therefore instant
        // and never requires re-surveying. Empty list = show everything found. Dock-level state, like
        // the area assignment -- it belongs to the dock and applies to whichever drone is docked.
        // ---------------------------------------------------------------

        /// <summary>Material names the readout is limited to. Empty means "show every material found".</summary>
        [Serialized] public ThreadSafeList<string> MaterialFilter { get; set; } = new();

        /// <summary>True when <paramref name="material"/> should appear in the readout under the current filter.</summary>
        public bool IsMaterialShown(string material) =>
            this.MaterialFilter.Count == 0 || this.MaterialFilter.Contains(material);

        /// <summary>Adds or removes <paramref name="material"/> from the display filter.</summary>
        public void ToggleMaterialFilter(string material)
        {
            if (string.IsNullOrWhiteSpace(material)) return;
            if (this.MaterialFilter.Contains(material)) this.MaterialFilter.Remove(material);
            else this.MaterialFilter.Add(material);
        }

        /// <summary>Clears the filter so every found material shows again.</summary>
        public void ClearMaterialFilter() => this.MaterialFilter = new ThreadSafeList<string>();

        /// <summary>
        /// Every material discovered across this dock's areas — the catalog the filter selects from.
        /// Derived from the persisted findings rather than a hardcoded list, so it grows with what the
        /// drone actually finds.
        /// </summary>
        public List<string> KnownMaterials =>
            this.SurveyAreas
                .SelectMany(a => a.ReadFindings())
                .Where(f => f.Found)
                .Select(f => f.OreType)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        // Monotonic id source. Never reused, so a deleted area's id cannot collide with a
        // later one and a stale assignment to a deleted area resolves to "no area".
        [Serialized] private int nextAreaId = 1;

        /// <summary>The dock's assigned area entry, or null when unassigned or the assigned id no longer resolves.</summary>
        public SurveyAreaEntry AssignedSurveyArea =>
            this.AssignedSurveyAreaId == 0 ? null : this.SurveyAreas.FirstOrDefault(a => a.Id == this.AssignedSurveyAreaId);

        // ---------------------------------------------------------------
        // U8: a mining dock's reference to an area published by a survey dock (KTD2), and
        // this dock's own per-plot mined stamps (KTD12). Only meaningful on a dock holding
        // a mining drone, but declared here rather than on the drone: the reference and
        // the mined ledger are dock-owned state, the same way SurveyAreas is.
        // ---------------------------------------------------------------

        /// <summary>The area this mining dock currently consumes, or null when unassigned.</summary>
        [Serialized] public MiningAreaRef AssignedMiningArea { get; set; }

        // Bumped on every AssignMiningArea call (U10), so the lifecycle's change-detection
        // token changes even when the SAME area is reassigned -- mirroring
        // assignedAreaEpoch's own reasoning for a redraw. A fresh token is what starts a
        // fresh MiningJob rather than silently resuming a finished or stale one.
        [Serialized] private int miningAssignmentEpoch;

        /// <summary>Change-detection token for the mining assignment (U10), or null when unassigned.</summary>
        public string AssignedMiningAreaToken =>
            this.AssignedMiningArea == null ? null : $"mining:{this.AssignedMiningArea.AreaId}:{this.miningAssignmentEpoch}";

        /// <summary>
        /// This dock's own mined stamps (KTD12), flattened as (x, z, stamp) triples --
        /// compared against the SOURCE area's surveyed stamps to decide which plots are
        /// mineable (<see cref="AdvancedElectronics.Navigation.PlotFreshness.IsMineable"/>).
        /// Deliberately not cleared on reassignment: a plot already mined stays recorded
        /// mined even if the dock is later pointed at a different area, since the mined
        /// stamp describes the WORLD position, not the assignment.
        /// </summary>
        [Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();

        /// <summary>This dock's persisted mined stamps, rehydrated into a live accumulator.</summary>
        public PlotStampAccumulator ReadMinedStamps()
        {
            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < this.MinedStamps.Count; i += 3)
                entries[new PlotCoord((int)this.MinedStamps[i], (int)this.MinedStamps[i + 1])] = this.MinedStamps[i + 2];
            return PlotStampAccumulator.FromSnapshot(entries);
        }

        /// <summary>Records <paramref name="plot"/> mined at <paramref name="stampValue"/> and persists it immediately -- unlike the survey side, there is no live/throttled projection step, since a mined stamp is written once per plot, not accumulated per column.</summary>
        public void RecordMinedPlot(PlotCoord plot, long stampValue)
        {
            var accumulator = this.ReadMinedStamps();
            accumulator.Record(plot, stampValue);

            var flat = new ThreadSafeList<long>();
            foreach (var entry in accumulator.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            this.MinedStamps = flat;
        }

        /// <summary>True when <paramref name="citizen"/> holds full access on this dock (R39, R40) -- the level the dig-or-mine action itself declares, not the attribute default.</summary>
        public bool HasFullAccess(User citizen) =>
            citizen != null && ServiceHolder<IAuthManager>.Obj.IsAuthorized(this, citizen, AccessType.FullAccess, null, out _).Success;

        // ---------------------------------------------------------------
        // U12: the assignment's citizen stamp (R18, R33, R37, R40, KD10) -- who is
        // accountable for every removal this dock's mining job performs. A plain (name,
        // id) snapshot, not a live User reference (mirrors DroneOwnership's own reasoning:
        // a stale live reference would dangle across a session boundary).
        // ---------------------------------------------------------------

        [Serialized] public string StampedCitizenName { get; private set; }
        [Serialized] public int StampedCitizenId { get; private set; }

        /// <summary>The stamped citizen, re-resolved live every read, or null if never stamped or now offline/unknown.</summary>
        public User StampedCitizen => this.StampedCitizenId == 0 ? null : UserManager.FindUserByID(this.StampedCitizenId);

        /// <summary>
        /// Assigns this mining dock to consume <paramref name="area"/>, published by
        /// <paramref name="sourceDock"/> (R2, R3, R5, KD15), and stamps <paramref name="actingCitizen"/>
        /// as the party accountable for it (R18, R40) -- re-stamping on every reassignment,
        /// including to the same area. Refuses the whole call (no assignment, no stamp) if
        /// the acting citizen has a permission-ignoring tool selected (R37) or lacks full
        /// access on this dock (R40); returns whether it succeeded.
        /// </summary>
        public bool AssignMiningArea(DroneDockObject sourceDock, SurveyAreaEntry area, User actingCitizen)
        {
            if (area != null)
            {
                if (actingCitizen == null || actingCitizen.DevToolSelected || !this.HasFullAccess(actingCitizen))
                    return false;

                this.StampedCitizenName = actingCitizen.Name;
                this.StampedCitizenId = actingCitizen.Id;
            }

            this.AssignedMiningArea = area == null ? null : MiningAreaRef.For(sourceDock, area);
            this.miningAssignmentEpoch++;
            return true;
        }

        /// <summary>Clears this dock's mining area assignment (R7). The mined ledger, hold, and stamp are untouched.</summary>
        public void UnassignMiningArea()
        {
            this.AssignedMiningArea = null;
            this.miningAssignmentEpoch++;
        }

        /// <summary>
        /// Re-checks the stamped citizen against live access (KTD9 -- at each plot arrival,
        /// not once per dispatch): full access on this dock (R33, R40), and not a
        /// permission-ignoring tool now selected (R37). False ends the job.
        /// </summary>
        public bool RecheckStamp()
        {
            var citizen = this.StampedCitizen;
            return citizen != null && !citizen.DevToolSelected && this.HasFullAccess(citizen);
        }

        // ---------------------------------------------------------------
        // U9: the mining job's ledger lives with the dock, not the drone world object, so
        // it survives a drone despawn (U9's own reasoning). Persisted as a flattened
        // snapshot (U2's ToSnapshot/FromSnapshot), the live-accumulator-plus-snapshot
        // pattern the survey side already uses for findings.
        // ---------------------------------------------------------------

        [Serialized] private int miningJobStatusValue = -1; // -1 = no job yet
        [Serialized] private int miningJobEndReasonValue = -1;
        [Serialized] private ThreadSafeList<int> miningJobLedger = new();

        private MiningJob liveMiningJob;

        /// <summary>The current mining job, rehydrated from the persisted snapshot on first access after load. Null until one is created.</summary>
        public MiningJob MiningJob
        {
            get
            {
                if (this.liveMiningJob == null && this.miningJobStatusValue >= 0)
                    this.liveMiningJob = this.RehydrateMiningJob();
                return this.liveMiningJob;
            }
            set => this.liveMiningJob = value;
        }

        /// <summary>Projects the live job onto its persisted snapshot fields. Called from the dock's own throttled tick.</summary>
        public void PersistMiningJob()
        {
            if (this.liveMiningJob == null) return;

            var snapshot = this.liveMiningJob.ToSnapshot();
            this.miningJobStatusValue = (int)snapshot.Status;
            this.miningJobEndReasonValue = snapshot.EndReason.HasValue ? (int)snapshot.EndReason.Value : -1;

            var flat = new ThreadSafeList<int>();
            foreach (var entry in snapshot.Ledger)
            {
                flat.Add(entry.Plot.X);
                flat.Add(entry.Plot.Z);
                flat.Add((int)entry.Outcome);
                flat.Add(entry.Category.HasValue ? (int)entry.Category.Value : -1);
            }
            this.miningJobLedger = flat;
        }

        private MiningJob RehydrateMiningJob()
        {
            var entries = new List<MiningJobSnapshot.LedgerEntry>();
            for (var i = 0; i + 3 < this.miningJobLedger.Count; i += 4)
            {
                var plot = new PlotCoord(this.miningJobLedger[i], this.miningJobLedger[i + 1]);
                var outcome = (PlotOutcome)this.miningJobLedger[i + 2];
                var categoryValue = this.miningJobLedger[i + 3];
                entries.Add(new MiningJobSnapshot.LedgerEntry(plot, outcome, categoryValue < 0 ? (SkipCategory?)null : (SkipCategory)categoryValue));
            }

            var status = (MiningJobStatus)this.miningJobStatusValue;
            var endReason = this.miningJobEndReasonValue < 0 ? (MiningEndReason?)null : (MiningEndReason)this.miningJobEndReasonValue;
            return MiningJob.FromSnapshot(new MiningJobSnapshot(status, endReason, entries));
        }

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
            this.ClearSurveyData(id);
            if (this.AssignedSurveyAreaId == id)
                this.AssignedSurveyAreaId = 0;
        }

        /// <summary>
        /// Drops an area's findings from both the serialized snapshot (if the entry still exists)
        /// and the live in-memory record (KTD11). Called on delete and on edit — an edit redraws
        /// the geometry, so its old survey no longer describes it. Reassignment does NOT call this:
        /// findings belong to the area, not the drone's current target.
        /// </summary>
        public void ClearSurveyData(int id)
        {
            this.SurveyAreas.FirstOrDefault(a => a.Id == id)?.ClearFindings();
            this.surveyRecord?.ClearArea(id);
        }

        // Bumped whenever the ASSIGNED area's geometry is edited, so it becomes part of the token
        // the drone's lifecycle watches -- an edit then re-dispatches the drone exactly like an
        // unassign+reassign (fresh pathfinding, fresh sweep of the new shape). Not serialized: it
        // is a transient change nonce, and the lifecycle's own change-detection field is likewise
        // in-memory, so a restart re-dispatches regardless.
        private int assignedAreaEpoch;

        /// <summary>
        /// The drone's standing assignment as a change-detection token, or null when unassigned.
        /// Encodes both the area id AND an edit epoch, so editing the assigned area's plots changes
        /// the token and forces a re-dispatch even though the id is unchanged.
        /// </summary>
        public string AssignedAreaToken =>
            this.AssignedSurveyAreaId != 0 ? $"area:{this.AssignedSurveyAreaId}:{this.assignedAreaEpoch}" : null;

        /// <summary>
        /// Called after an area's plots are redrawn (edit): clears its survey data (new geometry =
        /// new survey) and, when it is the assigned area, bumps the epoch so the drone restarts its
        /// pathfinding and sweep for the new shape as if it had been unassigned and reassigned.
        /// </summary>
        public void OnAreaEdited(int id)
        {
            this.ClearSurveyData(id);
            if (this.AssignedSurveyAreaId == id)
                this.assignedAreaEpoch++;
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

        /// <summary>
        /// True when <paramref name="worldPos"/> falls inside one of the dock's assigned survey
        /// area's plots; false when no area is assigned. Re-resolves the area from its
        /// serialized entry on every call (no cached geometry). Rounds the world column the same
        /// way the pathfinder does, then maps to a plot via the Eco-free <see cref="SurveyArea"/>.
        /// </summary>
        public bool IsPositionInAssignedArea(Vector3 worldPos)
        {
            var entry = this.AssignedSurveyArea;
            if (entry == null) return false;

            return entry.ToSurveyArea().ContainsWorldColumn(
                (int)System.MathF.Round(worldPos.X),
                (int)System.MathF.Round(worldPos.Z),
                PlotUtil.PropertyPlotLength);
        }

        // The dock's live, in-memory survey record (KTD11): the OreSensorComponent feeds every
        // sample here attributed to the assigned area id, and RefreshReadout projects the assigned
        // area's findings into that area's serialized snapshot for persistence + display. NOT
        // serialized itself — it is the running accumulator (raw sampled blocks + per-plot
        // concentration); the durable, restart-surviving copy is the per-area OreFindingSnapshot
        // list on each SurveyAreaEntry. plotSize matches IsPositionInAssignedArea's plot mapping.
        private SurveyRecord surveyRecord;

        /// <summary>The dock's per-area survey accumulator, created on first use.</summary>
        public SurveyRecord SurveyRecord =>
            this.surveyRecord ??= new SurveyRecord(PlotUtil.PropertyPlotLength);

        /// <summary>
        /// Copies the assigned area's live findings into its persisted snapshot (KTD11), so they
        /// survive a restart and remain readable while another area is assigned. Skips when there
        /// is no assigned area or no samples yet, so an empty post-restart record does not wipe a
        /// previously-persisted snapshot before the drone has re-surveyed.
        /// </summary>
        private void PersistAssignedAreaFindings()
        {
            var entry = this.AssignedSurveyArea;
            if (entry == null || this.surveyRecord == null) return;

            var area = entry.ToSurveyArea();
            var coverage = this.surveyRecord.Coverage(area);
            if (coverage <= 0f) return; // no samples for this area yet — keep any persisted snapshot.

            // How deep the survey looked (the drone sensor's reach) and the area's median surface
            // level, persisted with the findings so they show without a drone present.
            var depth = 0;
            if (this.SpawnedDrone != null && !this.SpawnedDrone.IsDestroyed
                && this.SpawnedDrone.TryGetComponent<OreSensorComponent>(out var sensor))
                depth = sensor.SurveyReach;
            var median = this.surveyRecord.MedianSurfaceLevel(entry.Id) ?? 0;

            entry.SetFindings(this.surveyRecord.Findings(entry.Id), coverage * 100f, depth, median);
        }

        /// <summary>Hook for mods to customize WorldObject before initialization. You can change housing values here.</summary>
        partial void ModsPreInitialize();
        /// <summary>Hook for mods to customize WorldObject after initialization.</summary>
        partial void ModsPostInitialize();

        protected override void Initialize()
        {
            this.ModsPreInitialize();
            base.Initialize();  
            // ModsPreInitialize/base.Initialize()/.../ModsPostInitialize is the AutoGen
            // object shape (Mods/__core__/AutoGen/Vehicle/Excavator.cs on the dedicated
            // server ships this exact ordering in source).
            // An earlier comment here claimed vanilla objects never call base from their
            // Initialize override; the generated sources do, so the call above is correct
            // and that claim has been removed rather than left contradicting the code.
            if (this.TryGetComponent<PublicStorageComponent>(out var storage))
            {
                // Vanilla storage-init shape (verified against the dedicated server's
                // shipped source, e.g. Mods/__core__/AutoGen/WorldObject/StorageChest.cs):
                // Initialize(slotCount), then restrictions via AddInvRestriction. The
                // previously-used 3-arg Initialize overload is never used by any vanilla
                // object and its parameter semantics were unproven.
                storage.Initialize(DockSlotCount);
                storage.Storage.AddInvRestriction(new SpecificItemTypesRestriction(DroneObjectForItem.Keys.ToArray()));
                storage.Storage.AddInvRestriction(new StackLimitRestriction(1));
                storage.Storage.OnChanged.Add(this.OnDockStorageChanged);

                // The module driver attaches here rather than from its own Initialize: component
                // initialization order is not guaranteed, and its restrictions have to attach to a
                // storage that already exists. Both this handler and the driver subscribe to the
                // same event without an ordering hazard, because the case they would have raced on
                // -- removal while the drone is out -- is refused outright (R19).
                this.GetComponent<DroneModuleComponent>().AttachTo(storage);
            }

            // R26: 20-block link radius, so enough linked storage to absorb a mining
            // area's output can be reached.
            this.GetComponent<LinkComponent>().Initialize(LinkRadius);

            this.ModsPostInitialize();
            {
                this.GetComponent<PartsComponent>().Config(() => LocString.Empty, new PartInfo[]
                {
                    // Every entry here must name a type deriving from PartItem, or
                    // PartsComponent.Config throws "PartsComponent can only be used with
                    // PartItem." and aborts DroneDockObject.Initialize().
                    //
                    // FramedGlassItem was here and is NOT a part -- it is [Tag("Constructable")]
                    // in AutoGen/Block/FramedGlass.cs. Replaced with SteelPlateItem, which the
                    // dock recipe already consumes (x20) and which is declared
                    // "class SteelPlateItem : PartItem". Swap it for any other PartItem if the
                    // repair cost should differ; the constraint is the base class, not the choice.
                    new() { TypeName = nameof(AdvancedCircuitItem), Quantity = 1},
                    new() { TypeName = nameof(SteelPlateItem), Quantity = 1},
                    new() { TypeName = nameof(SteelGearItem), Quantity = 2},
                });
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
        // KTD10: the ObjectID of the spawned drone, serialized so the dock can re-link to its
        // (persisted) drone WorldObject after a restart -- SpawnedDrone itself is a live reference
        // that does not survive serialization.
        [Serialized] private Guid spawnedDroneObjectId;

        /// <summary>
        /// Diagnostic only: the drone WorldObject id this dock believes it owns, whether or not the
        /// live reference survived a restart. Exposed for <c>/drone orphans</c>, which cross-checks
        /// every drone in the world against every dock's claim.
        /// </summary>
        internal Guid ClaimedDroneObjectId => this.spawnedDroneObjectId;

        // One-shot guard so the restart re-link runs once, on the first tick after load.
        private bool restartRelinkDone;

        /// <summary>
        /// Where this dock's drone stands when it is home: the centre of the pad, on its top
        /// surface. Used to spawn a fresh drone, as the target of the ordinary return leg, and
        /// as the last rung of the return ladder for a drone that could not walk back (R11).
        ///
        /// It used to sit one cell to the side, because the dock occupied a single column and
        /// standing on it meant standing inside it. A pad is something to land on, so the drone
        /// belongs in the middle of it -- and with the pad centred on this position, the middle
        /// is here.
        ///
        /// A whole block up, not the mesh's own surface. Every walking waypoint the pathfinder
        /// produces is <c>GroundHeightAt + 1</c> -- the cell an entity stands IN, one above the
        /// solid block it stands ON (see GridPathfinder.ToWaypoint). The dock occupies the block
        /// at its own position, so a drone standing on the dock belongs one block above it. An
        /// earlier version used the mesh's top surface instead, a quarter of a block, which put
        /// the drone inside the pad and made the teleport rung land somewhere the walking rung
        /// never would.
        ///
        /// Provisional, per KTD5: a real dock model may want the drone somewhere else on it.
        /// </summary>
        internal Vector3 DroneParkPosition => this.Position + new Vector3(0f, StandingHeightAboveDock, 0f);

        /// <summary>
        /// How far above the dock's own block a drone resting on it sits.
        ///
        /// One block puts the drone's ORIGIN on top of the pad, which is not the same as the
        /// drone resting on it: the HRVSTR's origin sits at its centre, with the hull reaching
        /// about half a block below (its collider is ~1.03 tall, centred on the origin), so a
        /// drone parked at exactly one block sank its belly into the pad.
        ///
        /// The half block is the model's own lower extent, not a fudge -- change it if the
        /// chassis is replaced by one whose origin sits at its feet, in which case it becomes
        /// one block again.
        /// </summary>
        private const float StandingHeightAboveDock = 2f;

        private void SpawnDrone(User user)
        {
            // Which drone to dispatch follows from which drone item is docked. The storage
            // restriction only admits keys of the table, so a paired item with no entry means the
            // table and the restriction have drifted apart -- bail rather than spawn the wrong
            // drone, which would look like a working dock dispatching someone else's robot.
            if (this.PairedDrone == null
                || !DroneObjectForItem.TryGetValue(this.PairedDrone.GetType(), out var droneObjectType))
                return;

            var obj = WorldObjectManager.ForceAdd(droneObjectType, user, this.DroneParkPosition, Quaternion.Identity, false) as WorldObject;
            if (obj == null)
            {
                // Say so. The park point sits inside the dock's own reserved footprint, and
                // whether the engine refuses to place an object there could not be settled
                // offline -- the reference assemblies ship with method bodies stripped. A
                // silent return here means inserting a drone into a dock appears to do
                // nothing at all, with no server log to search for.
                Log.WriteErrorLineLoc(
                    $"Drone Dock at {this.Position} could not spawn {droneObjectType.Name} at its park position {this.DroneParkPosition} -- placement was refused, possibly because the park point lies inside the dock's own registered occupancy.");
                return;
            }

            if (user != null && obj is IDroneOwnable ownable)
                ownable.SetOwner(user);

            if (obj.TryGetComponent<DroneLifecycle>(out var lifecycle))
                lifecycle.HomeDock = this;

            this.SpawnedDrone = obj;
            this.spawnedDroneObjectId = obj.ObjectID;
        }

        /// <summary>
        /// After a server restart the dock's <see cref="SpawnedDrone"/> reference is null even
        /// though the drone WorldObject persists, so the dock reports "no drone" until the drone
        /// is removed and re-inserted (B3). Runs once on first tick after load: re-links the
        /// persisted drone by its serialized id, or -- if that drone is gone but a drone item is
        /// still docked -- respawns one.
        /// </summary>
        private void RestoreDroneLinkOnce()
        {
            if (this.restartRelinkDone) return;
            this.restartRelinkDone = true;

            if (this.SpawnedDrone != null && !this.SpawnedDrone.IsDestroyed)
                return; // already linked this session.

            // "Is this a drone?" is asked as "does it carry a DroneLifecycle?" rather than by
            // listing drone types: the component is what makes an object a drone here, and the
            // test then covers a new drone type without being updated.
            if (this.spawnedDroneObjectId != Guid.Empty
                && ServiceHolder<IWorldObjectManager>.Obj.GetFromID(this.spawnedDroneObjectId) is WorldObject existing
                && existing.HasComponent<DroneLifecycle>()
                && !existing.IsDestroyed)
            {
                this.SpawnedDrone = existing;
                if (existing.TryGetComponent<DroneLifecycle>(out var lifecycle))
                    lifecycle.HomeDock = this;
                return;
            }

            // Drone WorldObject is gone but the item is still docked: respawn and re-pair.
            if (this.TryGetComponent<PublicStorageComponent>(out var storage))
            {
                var stack = storage.Storage.NonEmptyStacks.FirstOrDefault();
                if (stack?.Item != null)
                {
                    this.PairedDrone = stack.Item;
                    this.SpawnDrone(null);
                }
            }
        }

        /// <summary>
        /// Destroys the spawned drone WorldObject when the item is removed from the dock, resetting
        /// state so a fresh drone spawns on re-insert.
        ///
        /// Removal now requires the drone to be docked (R19, enforced by
        /// <see cref="DroneDockedRestriction"/>). That reverses an earlier rule here that removal was
        /// always allowed, which existed because a roaming drone could strand and needed an escape
        /// hatch; the return-leg escalation (relax climb height, then hover, then clip, then
        /// teleport) means a return can no longer fail, so the escape hatch is obsolete rather than
        /// merely inconvenient -- and blocking it is what keeps a live drone from having its
        /// components torn off underneath it.
        ///
        /// The <see cref="SpawnedDrone"/> reference can be
        /// stale or null (e.g. the item is pulled before the post-restart re-link runs), which would
        /// otherwise leave the persisted drone orphaned and still ticking — so resolve it by its
        /// serialized id as a fallback and destroy that.
        /// </summary>
        private void DespawnDrone()
        {
            var drone = this.SpawnedDrone;
            if ((drone == null || drone.IsDestroyed) && this.spawnedDroneObjectId != Guid.Empty)
                drone = ServiceHolder<IWorldObjectManager>.Obj.GetFromID(this.spawnedDroneObjectId) as WorldObject;

            if (drone != null && !drone.IsDestroyed)
                WorldObjectManager.DestroyPermanently(drone);

            this.SpawnedDrone = null;
            this.spawnedDroneObjectId = Guid.Empty;
        }

        // ---------------------------------------------------------------
        // Dock readout driver. The survey results live in the dock's Survey tab (the in-window
        // panel) and the chat commands -- KTD3 retired the world-space floating text and the object
        // tooltip. This section only (a) folds the assigned area's live samples into its persisted
        // snapshot and refreshes the tab on a throttled tick (a WorldObjectComponent's own Tick does
        // not reliably fire on the dock, so the dock drives it), and (b) pushes the boolean Working
        // animation state for future art. No survey text is synced to the client.
        // ---------------------------------------------------------------

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

        /// <summary>
        /// Why this dock is not dispatching, or None when it is fine (R10, R12). Distinct reasons
        /// rather than a bare bool, because a player who is out of fuel must be told that -- not
        /// shown an area that looks like nobody asked for it.
        /// </summary>
        internal DockStopReason StopReason
        {
            get
            {
                // The fuel supply is the drone's, installed unnamed (see SurveyDroneItem's
                // ComponentsToInstall for why it cannot be named). Absent means no drone slotted,
                // which is a separate condition the panel already reports.
                if (this.TryGetComponent<FuelSupplyComponent>(out var fuel) && !fuel.Enabled)
                    return DockStopReason.NoFuel;

                if (this.TryGetComponent<PartsComponent>(out var parts) && !parts.AllPartsWorking)
                    return DockStopReason.BrokenParts;

                if (this.PairedDrone is RepairableItem { Broken: true }) return DockStopReason.BrokenDrone;

                return DockStopReason.None;
            }
        }

        /// <summary>True when the dock can currently support work: fuel to burn, parts unbroken, drone intact.</summary>
        internal bool IsServiceable => this.StopReason == DockStopReason.None;

        /// <summary>
        /// True while the paired drone is doing work that costs fuel, dock parts, and its own
        /// condition (R9). False on the return leg, so a shortage never strands the drone.
        /// </summary>
        internal bool DroneIsWorking =>
            this.SpawnedDrone != null
            && !this.SpawnedDrone.IsDestroyed
            && this.SpawnedDrone.TryGetComponent<DroneLifecycle>(out var lifecycle)
            && lifecycle.IsWorking;

        public override void Tick()
        {
            base.Tick();

            this.RestoreDroneLinkOnce();
            this.PushWorkingState();

            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            var deltaTime = manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;

            this.DriveWear(deltaTime);

            this.secondsSinceLastReadoutRefresh += deltaTime;
            if (this.secondsSinceLastReadoutRefresh < ReadoutRefreshIntervalSeconds)
                return;

            this.secondsSinceLastReadoutRefresh = 0f;
            this.RefreshReadout();
        }

        // Wear rates, in condition points per hour of work. No vanilla analogue is close enough to
        // copy -- no vanilla attachment wears passively -- so these are starting points, not
        // decisions: low enough that a full survey costs a visible but small fraction of condition.
        // Named constants so a live session can move them without hunting (plan U5 execution note).
        private const float DockPartsWearPerHour = 4f;
        private const float DroneWearPerHour     = 8f;

        /// <summary>
        /// Wears the dock's parts and the docked drone's own condition while the drone works (R9,
        /// R18). Fuel needs nothing here: FuelConsumptionComponent burns whenever its parent is
        /// Operating, and SurveyComponent.Operating is already exactly "the drone is working".
        ///
        /// All three channels therefore share one definition of working, which excludes the return
        /// leg -- so a dock that recalled its drone for want of fuel cannot then run it out of the
        /// condition it needs to get home.
        ///
        /// The dock's parts stay with the dock; the drone's condition rides the drone item, so it
        /// travels when the drone is moved to another dock.
        /// </summary>
        private void DriveWear(float deltaTime)
        {
            if (!this.DroneIsWorking) return;

            var hours = TimeUtil.SecondsToHours(deltaTime);

            if (this.TryGetComponent<PartsComponent>(out var parts))
                parts.ConsumeDurabilityAccumulated(null, hours * DockPartsWearPerHour);

            // No player is present for a drone working unattended, and UseDurability only needs one
            // to send the "your item broke" message. The drone still breaks; nobody is told at the
            // moment it happens, which is what the panel's stopped-reason line is for (R12).
            if (this.PairedDrone is RepairableItem drone)
                drone.UseDurability((float)(hours * DroneWearPerHour), player: null);
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
                working = lifecycle.Status == DroneStatus.EnRoute || lifecycle.Status == DroneStatus.OnStation;
            }

            if (this.lastPushedWorking == working)
                return;

            this.lastPushedWorking = working;
            this.SetAnimatedState(WorkingStateName, working);
        }

        /// <summary>
        /// Folds the assigned area's live samples into its persisted snapshot (KTD11) and refreshes
        /// the dock's Survey tab -- the survey-readout surface. No survey text is synced to the
        /// client; the world-space text and object tooltip were retired (KTD3).
        /// </summary>
        private void RefreshReadout()
        {
            this.PersistAssignedAreaFindings();

            // A WorldObjectComponent's own Tick does not reliably fire on the dock, so the dock
            // drives the tab's refresh from its own (proven) tick. This is not merely a convenience:
            // a Changed() raised inside a setter does not reach the client on its own, so without
            // this push the tab's controls look dead however much they update server-side.
            if (this.TryGetComponent<SurveyComponent>(out var surveyTab))
                surveyTab.RefreshAll();

            this.PersistMiningJob();
            if (this.TryGetComponent<MiningComponent>(out var miningTab))
                miningTab.RefreshAll();

            // Temporary, with the U1 probe: drives the showcase's server-state mirror so a write
            // can be observed without a restart. Goes when the showcase does.
            if (this.TryGetComponent<UIShowcaseComponent>(out var showcase))
                showcase.RefreshMirror();
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
    [LocDescription("Home point for a survey drone. Insert a Survey Drone to pair and dispatch it, then draw and assign a survey area from the dock's Survey tab.")]
    [Ecopedia("Crafted Objects", "Advanced Electronics", true, true, null)]
    [Weight(1000)]
    public class DroneDockItem : WorldObjectItem<DroneDockObject>, IPersistentData
    {
        protected override OccupancyContext GetOccupancyContext =>
            new SideAttachedContext(0 | DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(this.WorldObjectType));

        /// <summary>
        /// Carries the dock's component state across pickup and replacement (R14). Declaring
        /// IPersistentData is the entire opt-in: Eco's pickup path then sweeps every component's
        /// state onto this item and pours it back when the item is placed again, so a worn dock
        /// stays worn instead of returning to a fresh one.
        ///
        /// Side effect, and the correct one: an IPersistentData item stops stacking. Two docks of
        /// different condition are genuinely different items and must not merge.
        /// </summary>
        [Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
        public object PersistentData { get; set; }
    }

    /// <summary>Recipe unlocking <see cref="DroneDockItem"/>.</summary>
    [RequiresSkill(typeof(AdvancedElectronicsSkill), 1)]
    public partial class DroneDockRecipe : RecipeFamily
    {
        // Eco force-creates one instance of every RecipeFamily-derived type at startup
        // (RecipeFamily carries [ForceCreateViewAllDerived]) -- registration belongs in
        // the instance constructor, mirroring vanilla recipes (e.g. StorageChestRecipe).
        public DroneDockRecipe()
        {
            var recipe = new Recipe();
            recipe.Init(
                name: "DroneDock",
                displayName: Localizer.DoStr("Drone Dock"),
                ingredients: new List<IngredientElement>
                {
                    new IngredientElement(typeof(AdvancedCircuitItem), 4, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(SteelPlateItem), 20, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(FramedGlassItem), 20, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(SteelGearItem), 6, typeof(AdvancedElectronicsSkill)),
                    new IngredientElement(typeof(RadiatorItem), 1, true),
                    new IngredientElement(typeof(LightBulbItem), 1, true),
                },
                items: new List<CraftingElement>
                {
                    new CraftingElement<DroneDockItem>(1),
                });

            this.Recipes = new List<Recipe> { recipe };
            this.ExperienceOnCraft = 15;
            this.LaborInCalories = CreateLaborInCaloriesValue(500, typeof(AdvancedElectronicsSkill));
            this.CraftMinutes = CreateCraftTimeValue(beneficiary: typeof(DroneDockRecipe), start: 20, skillType: typeof(AdvancedElectronicsSkill));
            this.ModsPreInitialize();
            this.Initialize(displayText: Localizer.DoStr("Drone Dock"), recipeType: typeof(DroneDockRecipe));
            this.ModsPostInitialize();

            CraftingComponent.AddRecipe(tableType: typeof(RoboticAssemblyLineObject), recipeFamily: this);
        }

        partial void ModsPreInitialize();
        partial void ModsPostInitialize();
    }
}
