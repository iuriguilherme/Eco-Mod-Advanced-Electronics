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
using Eco.Shared.Serialization;
using Eco.Shared.Services;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's single "Survey" tab: manage areas, choose which one the drone works on, and read
    /// any area's findings.
    ///
    /// ONE selector and three buttons, mirroring the mining tab. The selector chooses which area you
    /// are looking at AND which one Assign acts on; assignment happens only when a button is pressed.
    ///
    /// This panel was built around a one-button rule, and that rule has been retired deliberately.
    /// The rule bought a compact layout and cost correctness: with assignment living on the stepper
    /// itself, moving the stepper to read a neighbouring area's findings reassigned the working drone
    /// to it. A player cannot browse without disturbing the drone, and no amount of layout thrift is
    /// worth that. Three BigButtons cost roughly ten standard rows; the panel wears it.
    ///
    /// What the button budget was protecting against is still true and still shapes this file.
    /// BigButton is the panel's COMMIT control — fixed size, not groupable — so at ~70px each costs
    /// 3.2 standard rows and leaves the horizontal axis empty. And RPC methods render AFTER all
    /// properties whatever the declaration order, so no button can serve as a top anchor.
    ///
    /// Selection is ONE stepper rather than a control per area, for a reason beyond layout: N
    /// controls cannot share one field. The client writes back EVERY editable member as a batch on
    /// any interaction, in declaration order, so one click on a per-area checkbox arrived as pos1=true
    /// followed by pos2..pos6=false and the trailing writes unassigned what the first one set —
    /// measured as six setter calls per click, dock left at id 0. Comparing against derived state to
    /// make the siblings no-ops did not save it. One member holding the value has no sibling to stomp
    /// it, and costs one row whatever the area count. That batch write-back is also why assignment
    /// must not live in a setter at all: an RPC fires once, on purpose, when the player asks for it.
    ///
    /// Members render one per row in declaration order, so the order below IS the reading order. The
    /// numbered list sits above the steppers because their values are positions and mean nothing
    /// until the list that names them has been read — not a preference: a row label cannot be
    /// generated at runtime, since DynamicTitle resolves once at window-open and never re-resolves.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
    public class SurveyComponent : WorldObjectComponent, IOperatingWorldObjectComponent
    {
        private const int MaxAreaPlots = 40; // v1 tier cap (R1b); drone-tier-owned later.

        /// <summary>
        /// How many survey areas one dock may hold. A product number, not a layout budget: with one
        /// selector serving both viewing and assignment, nothing costs a row per area except the list text.
        ///
        /// Ten fits: the panel measures roughly 552px against a ~605px viewport at ten areas. The
        /// worst case — ten areas AND one fully surveyed area reporting every material the drone
        /// detects — lands within a few pixels of the fold, so eight would buy headroom if scrolling
        /// ever becomes a complaint.
        ///
        /// It must be a compile-time constant because the steppers' Range is a plain C# attribute and
        /// the view system has no RangeParam(nameof(...)) sibling to track a live count.
        /// </summary>
        public const int MaxSurveyAreas = 10;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>
        /// True exactly while the drone is working (R9). This is what makes the dock Operating, and
        /// FuelConsumptionComponent burns only while its parent is Operating -- so the fuel half of
        /// R9 needs no code of its own, including the return-leg exemption.
        /// </summary>
        // NOT [Serialized] -- derived on every read, so there is no member to load a saved value
        // back into. It answers "is the dock working right now", which is recomputed, never stored.
        public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;

        /// <summary>
        /// False until <see cref="Initialize"/> has run. Deserialization assigns `[Serialized]`
        /// members by invoking their setters, and the setters below write through to the dock — so
        /// without this gate a world load replays persisted positions as drone assignments. Not
        /// itself `[Serialized]`: it must start false on every load.
        /// </summary>
        private bool ready;

        /// <summary>
        /// Index into the dock's area list of the area whose findings are shown. Purely a VIEW
        /// cursor: it never changes what the drone is assigned to, so reading area 4 does not
        /// dispatch the drone there.
        /// </summary>
        private int viewIndex;

        // ---------------------------------------------------------------
        // Status and area list
        // ---------------------------------------------------------------

        /// <summary>
        /// What the drone is DOING. Three states, not two: assignment does not require a drone to
        /// exist, so without the no-drone variant the tab reports success while nothing happens.
        /// </summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string DroneStatus { get; private set; } = "none docked";

        // StringTitle, not LinedHeader. LinedHeader and SectionHeader render the MEMBER NAME and
        // discard the value -- an earlier build of this tab showed "Assign Header" on screen.
        // StringTitle and GeneralHeader are the two that render what you assign.
        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string AssignHeader { get; private set; } = "Areas";

        /// <summary>The dock's numbered area list — what the position numbers below refer to.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        /// <summary>Names the assigned area, so the selector below is not read on its own.</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string AssignedDisplay { get; private set; } = string.Empty;

        // ---------------------------------------------------------------
        // Selection and findings
        // ---------------------------------------------------------------

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string FindingsHeader { get; private set; } = "Selected area";

        /// <summary>
        /// The selected area, by position: whose findings are shown below, AND what
        /// <see cref="AssignSelectedArea"/> acts on. One selector for both, matching the mining
        /// tab's Browse Position.
        ///
        /// Selecting is deliberately inert with respect to the drone. This used to be two
        /// controls, and the assigning one wrote through on every change -- so scrolling the list
        /// to look at a neighbouring area's findings reassigned the working drone to it. Moving
        /// the selection now changes only what you are LOOKING at; the Assign and Unassign
        /// buttons are the only things that change what the drone does.
        /// </summary>
        [Serialized, Eco, Range(1, MaxSurveyAreas), UITypeName("Int32")]
        public int ViewPosition
        {
            get => this.viewIndex + 1;
            set
            {
                if (!this.ready) return;                    // deserialization, not a player
                if (this.Parent is not DroneDockObject dock) return;
                if (this.viewIndex == value - 1) return;    // batch write-back of an unchanged value

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

        // ---------------------------------------------------------------
        // Buttons. Declared last because that is where they render anyway.
        // ---------------------------------------------------------------

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.RefreshAll();
        }

        /// <summary>
        /// Sends the drone to the area currently selected above (R13 -- reassignment re-paths
        /// immediately). Assigning is an explicit act, which is the whole point of this button
        /// existing: the selector used to assign as a side effect of being moved.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign Selected Area")]
        public void AssignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            if (this.viewIndex < 0 || this.viewIndex >= dock.SurveyAreas.Count)
            {
                player?.MsgLocStr("No area is selected to assign.", NotificationStyle.Error);
                return;
            }

            var area = dock.SurveyAreas[this.viewIndex];
            dock.AssignSurveyArea(area.Id);
            this.RefreshAll();
            player?.MsgLocStr($"Survey area '{area.Name}' assigned.", NotificationStyle.Info);
        }

        /// <summary>
        /// Clears this dock's survey assignment. The map picker can assign but had no way to
        /// un-assign, so stopping a survey drone meant pointing it at a different area -- and
        /// picking a non-existent one left it flipping between idle and unreachable, chasing
        /// somewhere that was never there.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Unassign Area")]
        public void UnassignArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            if (dock.AssignedSurveyAreaId == 0)
            {
                player?.MsgLocStr("This dock has no survey area assigned.", NotificationStyle.Warning);
                return;
            }

            dock.AssignSurveyArea(0);
            this.RefreshAll();
            player?.MsgLocStr("Survey area unassigned. The drone returns to its dock.", NotificationStyle.Info);
        }

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        // --- Refresh ---

        /// <summary>
        /// Rebuilds every synced member and pushes it. Driven by the dock's one-second tick, which is
        /// what actually makes a change appear: a Changed() raised inside a setter does not reach the
        /// client on its own. Nothing here writes a member the player owns, so a push landing in the
        /// same second as a click cannot fight it.
        /// </summary>
        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            this.viewIndex = DockReadout.ClampCursor(this.viewIndex, dock.SurveyAreas.Count);

            this.DroneStatus     = BuildDroneStatus(dock);
            this.AreasDisplay    = this.BuildAreasText(dock);
            this.AssignedDisplay = BuildAssignedText(dock);
            this.ViewingDisplay  = this.BuildViewingText(dock);
            this.ResultsDisplay  = this.BuildResultsText(dock);

            this.Changed(nameof(this.DroneStatus));
            this.Changed(nameof(this.AreasDisplay));
            this.Changed(nameof(this.AssignedDisplay));
            this.Changed(nameof(this.ViewingDisplay));
            this.Changed(nameof(this.ResultsDisplay));
            this.Changed(nameof(this.ViewPosition));
        }

        // --- Text ---

        private static string BuildDroneStatus(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)
                return dock.AssignedSurveyAreaId != 0
                    ? "none docked -- build and dock one to start surveying"
                    : "none docked";

            // R12: a stopped dock names the reason, and each reason is distinct from "nobody
            // assigned an area". Without this the three shortages and an idle dock all read the
            // same, and the player has no way to tell which one to go fix.
            switch (dock.StopReason)
            {
                case DockStopReason.NoFuel:      return "stopped -- out of fuel";
                case DockStopReason.BrokenParts: return "stopped -- a dock part is broken";
                case DockStopReason.BrokenDrone: return "stopped -- the drone needs repair";
            }

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
                sb.Append(DockReadout.FormatAreaLine(Snapshot(area, position++, dock))).Append('\n');

            return sb.ToString();
        }

        private static string BuildAssignedText(DroneDockObject dock)
        {
            var area = dock.AssignedSurveyArea;
            return area == null
                ? "none -- select an area below, then Assign Selected Area"
                : $"{dock.SurveyAreas.IndexOf(area) + 1} -- {area.Name}";
        }

        private string BuildViewingText(DroneDockObject dock)
        {
            var area = this.ViewedArea(dock);
            if (area == null) return "no areas yet -- draw one on the map";

            return DockReadout.FormatViewingLine(
                Snapshot(area, this.viewIndex + 1, dock), dock.SurveyAreas.Count);
        }

        private string BuildResultsText(DroneDockObject dock)
        {
            this.ApplyPickerSelection(dock);

            var entry = this.ViewedArea(dock);
            if (entry == null)
                return "Draw an area on the map, then set the assign number to survey it.";

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
                return "Not surveyed yet. Set the assign number above to this area's position.";
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
        private static AreaSnapshot Snapshot(SurveyAreaEntry area, int position, DroneDockObject dock)
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
