using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.SharedTypes;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's single "Survey" tab: manage areas, choose which one the drone works on, and read
    /// any area's findings. Replaces the earlier Areas and Results tabs, which were split to halve
    /// two over-long panels — a problem that goes away once assignment and navigation stop using
    /// <c>BigButton</c>.
    ///
    /// BigButton is the panel's COMMIT control: fixed size, not groupable, meant to appear once. At
    /// ~70px it costs 3.2 standard rows and leaves the horizontal axis empty, because that is what a
    /// control designed to sit alone at the bottom of a panel does. Six of them for assignment was
    /// 420px of a ~605px viewport. Boolean and Int32 are the vocabulary's repeatable controls and
    /// cost 22px each. See docs/plans/2026-07-31-001-feat-merged-survey-tab-plan.md.
    ///
    /// Members render one per row in declaration order, so the order below IS the reading order:
    ///
    ///   1. the map manager button -- FIRST so it never moves as content below it grows
    ///   2. what the drone is doing
    ///   3. "Assign an area": the numbered list, then one checkbox per position
    ///   4. "Findings": the view cursor, what it points at, the filter, the findings
    ///
    /// The numbered list sits ABOVE the checkboxes because the checkbox labels are static position
    /// numbers and mean nothing until the list that names them has been read. That is not a
    /// preference — a row label cannot be generated at runtime. DynamicTitle resolves a label when
    /// the window opens and never re-resolves, so a label carrying an area's name and coverage would
    /// freeze at its open-time value and lie the moment anything changed.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
    public class SurveyComponent : WorldObjectComponent
    {
        private const int MaxAreaPlots = 40; // v1 tier cap (R1b); drone-tier-owned later.

        /// <summary>
        /// Size of the compile-time assign-checkbox pool. RPCs and properties are both declared at
        /// compile time, so SOME ceiling has to exist -- controls cannot be generated per area. Six
        /// is a product choice, not a technical one: the motivating late-game setup is one area per
        /// resource (coal, iron ore, limestone, gold ore, copper ore), each in a different biome,
        /// which is five with one spare.
        ///
        /// Cheaper rows are NOT a reason to raise this. A checkbox costs a third of what the old
        /// button did, so the layout could afford ten -- but the number encodes how many areas a
        /// player actually works, and that has not changed. Raise it when mod users ask. Areas past
        /// it are assigned with /drone assignarea.
        /// </summary>
        public const int AssignButtonPool = 6;

        /// <summary>
        /// Ceiling on the findings cursor. Deliberately larger than <see cref="AssignButtonPool"/>:
        /// READING an area costs no control, so the cursor should reach areas the checkbox pool
        /// cannot. It has to be a compile-time constant because Range is a plain C# attribute and
        /// there is no RangeParam(nameof(...)) sibling in the view system, so this cannot track the
        /// real area count. The setter clamps to it instead.
        /// </summary>
        private const int ViewCursorMax = 24;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // ---------------------------------------------------------------
        // 1. Manage -- the map is the area manager. Declared FIRST so it holds a fixed position
        //    regardless of how long the content below it grows.
        // ---------------------------------------------------------------

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.RefreshAll();
        }

        /// <summary>
        /// What the drone is DOING. Three states, not two: assignment does not require a drone to
        /// exist, so without the no-drone variant the tab reports success while nothing happens.
        /// </summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string DroneStatus { get; private set; } = "none docked";

        // ---------------------------------------------------------------
        // 2. Assign -- the numbered list, then one checkbox per position.
        // ---------------------------------------------------------------

        // StringTitle, not LinedHeader. LinedHeader and SectionHeader render the MEMBER NAME and
        // discard the value -- the first build of this tab showed "Assign Header" and "Findings
        // Header" on screen. StringTitle and GeneralHeader are the two that render what you assign.
        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string AssignHeader { get; private set; } = "Assign an area";

        /// <summary>The dock's numbered area list, and the overflow notice when the pool runs out.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        // TEMPORARY -- delete once the checkboxes are confirmed writing.
        //
        // The checkboxes tick and then revert on the next push, which is consistent with two very
        // different causes, and guessing between them has already cost two restarts:
        //
        //   the setter is never invoked   -> something blocks the write RPC (VisibilityParam on an
        //                                    editable property is the open suspect; the shape proven
        //                                    on the showcase probe did not carry one)
        //   the setter runs and no-ops    -> the `ready` gate below never became true, i.e.
        //                                    Initialize() does not run for this component
        //
        // A tick mark cannot tell these apart, because the client draws it either way. This line
        // reports server-side truth on the dock's tick: the call counter increments BEFORE the gate,
        // so an invoked-but-gated setter looks different from one that never ran.
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AssignDiagnostic { get; private set; } = string.Empty;

        private int setterCalls;
        private string lastSetter = "none";

        // Each checkbox is a VIEW of the dock's assigned area, holding nothing of its own: the getter
        // asks whether this position is the assigned one, the setter writes assignment through. So
        // "at most one ticked" is a property of there being one source of truth, not bookkeeping
        // this class maintains -- checking area 4 moves the assignment and area 2's getter simply
        // starts returning false. Confirmed live: an external write to the dock ticked derived
        // checkboxes on screen with no window reopen.
        //
        // [Serialized, Eco], and NOT the attribute's parts. Three shapes were tried live:
        //
        //   [SyncToView, Autogen, AutoRPC]   renders, displays, refreshes -- drops every click
        //   [Eco(false)]                     same: the box ticks, then the next tick unticks it
        //   [Serialized, Eco]                writes land (proven on the showcase probe)
        //
        // The failure is silent in every direction: no exception, no log line, and the client draws
        // the tick regardless. Whatever the write path keys off, it is not present unless [Eco]
        // carries its default persistence.
        //
        // [Serialized] on a write-through setter is a hazard, not a preference -- deserialization
        // sets [Serialized] members by INVOKING their setters, so loading a world would replay every
        // persisted row as an assignment, in an order nothing controls. `ready` below is the gate.
        //
        // Visibility gating is the vanilla AreaBonusComponent shape ([SyncToView] bool method +
        // VisibilityParam), so a pool of six costs six rows only when six areas exist.

        [SyncToView] public bool AreaExists1() => this.AreaCount() >= 1;
        [SyncToView] public bool AreaExists2() => this.AreaCount() >= 2;
        [SyncToView] public bool AreaExists3() => this.AreaCount() >= 3;
        [SyncToView] public bool AreaExists4() => this.AreaCount() >= 4;
        [SyncToView] public bool AreaExists5() => this.AreaCount() >= 5;
        [SyncToView] public bool AreaExists6() => this.AreaCount() >= 6;

        [Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists1))]
        public bool AssignArea1 { get => this.IsAssigned(1); set => this.SetAssigned(1, value); }

        [Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists2))]
        public bool AssignArea2 { get => this.IsAssigned(2); set => this.SetAssigned(2, value); }

        [Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists3))]
        public bool AssignArea3 { get => this.IsAssigned(3); set => this.SetAssigned(3, value); }

        [Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists4))]
        public bool AssignArea4 { get => this.IsAssigned(4); set => this.SetAssigned(4, value); }

        [Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists5))]
        public bool AssignArea5 { get => this.IsAssigned(5); set => this.SetAssigned(5, value); }

        [Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists6))]
        public bool AssignArea6 { get => this.IsAssigned(6); set => this.SetAssigned(6, value); }

        // ---------------------------------------------------------------
        // 3. Findings -- cursor, what it points at, filter, results.
        // ---------------------------------------------------------------

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string FindingsHeader { get; private set; } = "Findings";

        /// <summary>
        /// Which area's findings are shown. Purely a VIEW cursor: it never changes what the drone is
        /// assigned to, so reading area 4 does not dispatch the drone there.
        /// </summary>
        private int viewIndex;

        [Serialized, Eco, Range(1, ViewCursorMax), UITypeName("Int32")]
        public int ViewPosition
        {
            get => this.viewIndex + 1;
            set
            {
                if (!this.ready) return;   // deserialization, not a player -- see `ready`
                if (this.Parent is not DroneDockObject dock) return;
                this.viewIndex = DockReadout.ClampCursor(value - 1, dock.SurveyAreas.Count);
                this.RefreshAll();
            }
        }

        /// <summary>Which area the findings below belong to, and whether it is the assigned one.</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string ViewingDisplay { get; private set; } = string.Empty;

        /// <summary>
        /// Material targets: pick which materials the survey results show, the same way items and tags
        /// are picked in a recipe or a law. Empty shows everything found.
        ///
        /// Scoped to the stock "Excavatable" tag. Established by live diagnostics (`/drone tags`):
        /// every material the drone actually detects -- clay, coal, crushed variants, limestone, peat,
        /// sandstone, sulfur -- carries it, while the crafted buildables that polluted a plain
        /// BlockItem picker (ashlar, brick, lumber, hewn log) and the placeables (gasoline, logs) do
        /// not. It is not a perfect fit (a few non-target soils such as dirt and tailings are also
        /// excavatable, and selecting one simply does nothing), but it is the closest stock tag and
        /// the only kind the client can use.
        ///
        /// A custom tag covering exactly the detectable set was built and did not work: the server-side
        /// registry was correct (30 of 113 block items tagged, tag registered, classifier agreeing) yet
        /// the picker stayed empty. The cause is NOT that mods register too late for tags in general --
        /// InitMods() runs before TagManager.Initialize() (Eco.ModKit/ModDataSync.cs:63-66), so a [Tag]
        /// ATTRIBUTE on a mod type, or on a vanilla item replaced by a .override file, does reach the
        /// client. What fails is RUNTIME association: the client filters RequiredTag against
        /// ViewClassInfo.Tags, built once while ControllerManager is constructed
        /// (Eco.Core/Controller/ControllerMarshalerService.cs:367), and anything tagged after that build
        /// is invisible to the picker. So the attribute/.override route remains open; only the runtime
        /// route is closed. See docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md.
        ///
        /// Confirmed live: GamePickerList renders and filters from a WorldObjectComponent tab, even
        /// though every vanilla usage is inside a civics GameValue.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(BlockTags.Excavatable)]
        [LocDescription("Materials to show in the survey results. Leave empty to show everything found.")]
        public GamePickerList<BlockItem> MaterialTargets { get; set; } = new();

        /// <summary>The viewed area's findings (R7). Refreshed from the dock's tick.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ResultsDisplay { get; private set; } = string.Empty;

        /// <summary>
        /// False until <see cref="Initialize"/> has run. Deserialization assigns `[Serialized]`
        /// members by invoking their setters, and these setters write assignment through to the
        /// dock — so without this gate, loading a world replays every persisted checkbox as a
        /// drone assignment, in whatever order the serializer happens to use. Not `[Serialized]`
        /// itself: it must start false on every load.
        /// </summary>
        private bool ready;

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        // --- Assignment ---

        private int AreaCount() => this.Parent is DroneDockObject dock ? dock.SurveyAreas.Count : 0;

        private bool IsAssigned(int position)
        {
            if (this.Parent is not DroneDockObject dock) return false;
            if (position < 1 || position > dock.SurveyAreas.Count) return false;
            return dock.SurveyAreas[position - 1].Id == dock.AssignedSurveyAreaId;
        }

        /// <summary>
        /// Writes assignment through. Checking assigns; unchecking the checked row clears; checking a
        /// different row moves the assignment, and every other row's getter follows on its own.
        /// </summary>
        private void SetAssigned(int position, bool value)
        {
            // Counted BEFORE the gate on purpose: it is the only way to tell "never invoked" from
            // "invoked and refused". Temporary, with AssignDiagnostic.
            this.setterCalls++;
            this.lastSetter = $"pos{position}={value}";

            if (!this.ready) return;   // deserialization, not a player -- see `ready`
            if (this.Parent is not DroneDockObject dock) return;
            if (position < 1 || position > dock.SurveyAreas.Count) return;

            var area = dock.SurveyAreas[position - 1];
            dock.AssignSurveyArea(value ? area.Id : 0);
            this.RefreshAll();
        }

        // --- Refresh ---

        /// <summary>
        /// Rebuilds every synced member and pushes it. Called from the dock's one-second tick, which
        /// is what actually makes a change appear: a Changed() raised inside a setter does not reach
        /// the client on its own. Nothing here WRITES a member the player owns, so a push landing in
        /// the same second as a click cannot fight it.
        /// </summary>
        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            this.viewIndex = DockReadout.ClampCursor(this.viewIndex, dock.SurveyAreas.Count);

            this.DroneStatus = BuildDroneStatus(dock);
            this.AreasDisplay = this.BuildAreasText(dock);
            this.AssignDiagnostic =
                $"[diag] setter calls: {this.setterCalls} | last: {this.lastSetter} | " +
                $"ready: {this.ready} | dock assigned id: {dock.AssignedSurveyAreaId} | " +
                $"box1 reads: {this.IsAssigned(1)}";
            this.ViewingDisplay = this.BuildViewingText(dock);
            this.ResultsDisplay = this.BuildResultsText(dock);

            this.Changed(nameof(this.DroneStatus));
            this.Changed(nameof(this.AreasDisplay));
            this.Changed(nameof(this.AssignDiagnostic));
            this.Changed(nameof(this.ViewingDisplay));
            this.Changed(nameof(this.ResultsDisplay));
            this.Changed(nameof(this.ViewPosition));

            // Without these the client never re-evaluates row visibility, so a newly drawn area
            // gains no checkbox and a deleted one keeps its own until the dock is reopened.
            this.Changed(nameof(this.AreaExists1));
            this.Changed(nameof(this.AreaExists2));
            this.Changed(nameof(this.AreaExists3));
            this.Changed(nameof(this.AreaExists4));
            this.Changed(nameof(this.AreaExists5));
            this.Changed(nameof(this.AreaExists6));

            // The checkboxes derive from the dock, so a reassignment from anywhere -- another player,
            // a chat command, the map editor -- has to be pushed or the ticks go stale.
            this.Changed(nameof(this.AssignArea1));
            this.Changed(nameof(this.AssignArea2));
            this.Changed(nameof(this.AssignArea3));
            this.Changed(nameof(this.AssignArea4));
            this.Changed(nameof(this.AssignArea5));
            this.Changed(nameof(this.AssignArea6));
        }

        // --- Text ---

        private static string BuildDroneStatus(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)
                return dock.AssignedSurveyAreaId != 0
                    ? "none docked -- build and dock one to start surveying"
                    : "none docked";

            // ToString(), not interpolation: Status is an AdvancedElectronics.Navigation.DroneStatus
            // enum, which this component's own DroneStatus property shadows by name.
            return drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                ? lifecycle.Status.ToString()
                : "docked";
        }

        private string BuildAreasText(DroneDockObject dock)
        {
            if (dock.SurveyAreas.Count == 0)
                return "No survey areas yet. Use Manage Areas on Map to draw your first one.";

            var sb = new StringBuilder();

            var position = 1;
            foreach (var area in dock.SurveyAreas)
                sb.Append(DockReadout.FormatAreaLine(this.Snapshot(area, position++, dock))).Append('\n');

            var overflow = DockReadout.FormatOverflowNotice(
                dock.SurveyAreas.Count, AssignButtonPool, "/drone assignarea <id>");
            if (overflow.Length > 0)
                sb.Append('\n').Append(overflow);

            return sb.ToString();
        }

        private string BuildViewingText(DroneDockObject dock)
        {
            var area = this.ViewedArea(dock);
            if (area == null) return "no areas yet -- draw one from the map";

            return DockReadout.FormatViewingLine(
                this.Snapshot(area, this.viewIndex + 1, dock), dock.SurveyAreas.Count);
        }

        private string BuildResultsText(DroneDockObject dock)
        {
            this.ApplyPickerSelection(dock);

            var entry = this.ViewedArea(dock);
            if (entry == null)
                return "Draw an area on the map, then tick it above to survey it.";

            var sb = new StringBuilder();

            // Findings persist with the area (KTD11): these are entry's own, kept until it is edited
            // or deleted -- shown even while the drone is between areas or docked. The material
            // filter narrows what is DISPLAYED; everything stays recorded.
            var all = entry.ReadFindings().Where(f => f.Found).ToList();
            var findings = all
                .Where(f => dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .ToList();

            if (findings.Count == 0 && all.Count > 0)
            {
                sb.Append("No matching materials in this area -- clear the Material Targets picker above to show everything found.\n");
            }
            else if (findings.Count == 0)
            {
                sb.Append(EmptyFindingsMessage(entry)).Append('\n');
            }
            else
            {
                foreach (var f in findings)
                    sb.Append(DockReadout.FormatOreLine(f)).Append('\n');
                sb.Append("Coverage: ").Append(entry.CoveragePercent.ToString("F0")).Append("%\n");
            }

            if (entry.SurveyDepth > 0)
                sb.Append("Scanned to ").Append(entry.SurveyDepth)
                  .Append(" blocks below surface; median surface level ").Append(entry.MedianSurface).Append(".\n");

            if (dock.MaterialFilter.Count > 0)
                sb.Append("Filtered to: ").Append(string.Join(", ", dock.MaterialFilter)).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Coverage-aware message when the viewed area has no findings. Distinguishes "not started"
        /// from "in progress" from "fully covered, nothing here" -- so a finished-but-empty survey no
        /// longer tells the player to keep waiting, which never reveals anything new.
        /// </summary>
        private static string EmptyFindingsMessage(SurveyAreaEntry entry)
        {
            if (entry.CoveragePercent <= 0f)
                return "Not surveyed yet. Tick this area above to survey it.";
            if (entry.CoveragePercent >= 99.5f)
                return "Survey complete -- nothing found in this area.";
            return $"Surveyed {entry.CoveragePercent:F0}% so far -- nothing found yet.";
        }

        private SurveyAreaEntry ViewedArea(DroneDockObject dock) =>
            dock.SurveyAreas.Count == 0 ? null : dock.SurveyAreas[this.viewIndex];

        /// <summary>
        /// Reduces an area to the Eco-free shape <see cref="DockReadout"/> formats. The material
        /// filter is applied HERE, not there: the formatter is handed the top finding the player can
        /// actually see, which is why "nothing matching" and "nothing found" collapse to one case.
        /// </summary>
        private AreaSnapshot Snapshot(SurveyAreaEntry area, int position, DroneDockObject dock)
        {
            var top = area.ReadFindings()
                .Where(f => f.Found && dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .FirstOrDefault();

            return new AreaSnapshot(
                position, area.Name, area.PlotCount, area.CoveragePercent, top,
                area.Id == dock.AssignedSurveyAreaId);
        }

        // --- Material filter ---

        /// <summary>
        /// Projects the picker's current selection into the dock's serialized material filter, so the
        /// readout (and the chat command) work off one source. Maps a picked item type to the material
        /// name the sensor records, mirroring how that name is derived from a block type: strip the
        /// "Item" suffix, then a "Block" suffix if one remains ("IronOreItem" -> "IronOre",
        /// "LimestoneBlockItem" -> "Limestone", matching the sensor's "LimestoneBlock" -> "Limestone").
        /// </summary>
        private void ApplyPickerSelection(DroneDockObject dock)
        {
            var picked = PickedNames(this.MaterialTargets).Distinct().ToList();

            // Only rewrite when the selection actually differs, so the one-second refresh tick does
            // not fight a filter set from chat.
            if (picked.Count == dock.MaterialFilter.Count && picked.All(dock.MaterialFilter.Contains))
                return;

            dock.ClearMaterialFilter();
            foreach (var name in picked)
                dock.ToggleMaterialFilter(name);
        }

        /// <summary>The material names currently selected in the picker (empty when null or unset).</summary>
        private static IEnumerable<string> PickedNames(GamePickerList<BlockItem> picker) =>
            picker?.GetTypes().Select(t => MaterialNameFromItemType(t.Name)) ?? Enumerable.Empty<string>();

        /// <summary>Item type name -> the material name the sensor records. See <see cref="ApplyPickerSelection"/>.</summary>
        private static string MaterialNameFromItemType(string typeName)
        {
            var name = StripSuffix(typeName, "Item");
            return StripSuffix(name, "Block");
        }

        private static string StripSuffix(string value, string suffix) =>
            value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
    }
}
