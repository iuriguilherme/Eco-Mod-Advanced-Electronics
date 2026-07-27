using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
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
    /// The dock's "Survey" tab. Members are declared in the order they render, because with only
    /// stacked full-width elements available declaration order IS reading order:
    ///
    ///   1. Areas    -- the assignment line, then the numbered area list
    ///   2. Assign   -- one button per existing area (position-labelled; the list supplies the names)
    ///   3. Manage   -- one button opening the map, where areas are created/renamed/redrawn/deleted
    ///   4. Results  -- material picker, the area being viewed, Prev/Next, then that area's findings
    ///
    /// Assign buttons come after the list because their labels are static position numbers and are
    /// meaningless until the list that names them has been read. The Results cursor is INDEPENDENT of
    /// the drone's assignment, so any area's findings can be read without dispatching the drone there.
    ///
    /// Splitting Results into its own tab is planned but deferred (a second mod component's tab did
    /// not register when tried before); see docs/plans/2026-07-26-003-feat-survey-tab-ui-rework-plan.md.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
    public class SurveyAreasComponent : WorldObjectComponent
    {
        private const int MaxAreaPlots = 40; // v1 tier cap (R1b); drone-tier-owned later.

        /// <summary>Size of the compile-time assign-button pool. Areas beyond it use /drone assignarea.</summary>
        public const int AssignButtonPool = 10;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // ---------------------------------------------------------------
        // 1. Areas -- assignment line + numbered list
        // ---------------------------------------------------------------

        /// <summary>The assignment line followed by the dock's numbered area list.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        // ---------------------------------------------------------------
        // 2. Assign -- one button per existing area
        //
        // RPCs are declared at compile time, so N buttons cannot be generated; a fixed pool is gated
        // per position by a [SyncToView] bool, which the client re-evaluates when RefreshAreas pushes
        // Changed() for it. Same shape as the vanilla AreaBonusComponent (a WorldObjectComponent
        // combining [SyncToView] bool + VisibilityParam on a BigButton RPC).
        //
        // Clicking the button of the already-assigned area unassigns, so there is no Unassign button.
        // ---------------------------------------------------------------

        [SyncToView] public bool AreaExists1() => this.AreaCount() >= 1;
        [SyncToView] public bool AreaExists2() => this.AreaCount() >= 2;
        [SyncToView] public bool AreaExists3() => this.AreaCount() >= 3;
        [SyncToView] public bool AreaExists4() => this.AreaCount() >= 4;
        [SyncToView] public bool AreaExists5() => this.AreaCount() >= 5;
        [SyncToView] public bool AreaExists6() => this.AreaCount() >= 6;
        [SyncToView] public bool AreaExists7() => this.AreaCount() >= 7;
        [SyncToView] public bool AreaExists8() => this.AreaCount() >= 8;
        [SyncToView] public bool AreaExists9() => this.AreaCount() >= 9;
        [SyncToView] public bool AreaExists10() => this.AreaCount() >= 10;

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists1)), UITypeName("BigButton"), Description("Assign Area 1")]
        public void AssignArea1(Player player) => this.ToggleAssign(1);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists2)), UITypeName("BigButton"), Description("Assign Area 2")]
        public void AssignArea2(Player player) => this.ToggleAssign(2);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists3)), UITypeName("BigButton"), Description("Assign Area 3")]
        public void AssignArea3(Player player) => this.ToggleAssign(3);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists4)), UITypeName("BigButton"), Description("Assign Area 4")]
        public void AssignArea4(Player player) => this.ToggleAssign(4);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists5)), UITypeName("BigButton"), Description("Assign Area 5")]
        public void AssignArea5(Player player) => this.ToggleAssign(5);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists6)), UITypeName("BigButton"), Description("Assign Area 6")]
        public void AssignArea6(Player player) => this.ToggleAssign(6);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists7)), UITypeName("BigButton"), Description("Assign Area 7")]
        public void AssignArea7(Player player) => this.ToggleAssign(7);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists8)), UITypeName("BigButton"), Description("Assign Area 8")]
        public void AssignArea8(Player player) => this.ToggleAssign(8);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists9)), UITypeName("BigButton"), Description("Assign Area 9")]
        public void AssignArea9(Player player) => this.ToggleAssign(9);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists10)), UITypeName("BigButton"), Description("Assign Area 10")]
        public void AssignArea10(Player player) => this.ToggleAssign(10);

        // ---------------------------------------------------------------
        // 3. Manage -- the map is the area manager
        // ---------------------------------------------------------------

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.ClampViewCursor(dock);
            this.RefreshAll();
        }

        // ---------------------------------------------------------------
        // 4. Results -- material picker, viewed area, Prev/Next, findings
        // ---------------------------------------------------------------

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
        /// A custom tag covering exactly the detectable set was built and PROVEN not to work: the
        /// server-side registry was correct (30 of 113 block items tagged, tag registered, classifier
        /// agreeing) yet the picker stayed empty, because TagManager.Initialize does a one-time naming
        /// pass and calls SetupDone() before mods can register, so a late tag never reaches the client.
        ///
        /// Confirmed live: GamePickerList renders and filters from a WorldObjectComponent tab, even
        /// though every vanilla usage is inside a civics GameValue.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(BlockTags.Excavatable)]
        [LocDescription("Materials to show in the survey results. Leave empty to show everything found.")]
        public GamePickerList<BlockItem> MaterialTargets { get; set; } = new();

        /// <summary>Which area the findings below belong to.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ViewingDisplay { get; private set; } = string.Empty;

        // Prev/Next sit ABOVE the findings: findings are variable-length, so bottom-anchored controls
        // would drift off-screen exactly when an area has many materials.

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("◀ Prev area")]
        public void ViewPrev(Player player) => this.CycleView(-1);

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Next area ▶")]
        public void ViewNext(Player player) => this.CycleView(+1);

        /// <summary>The viewed area's findings (R7). Refreshed from the dock's tick.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ResultsDisplay { get; private set; } = string.Empty;

        /// <summary>
        /// Index into the dock's area list of the area whose findings are shown. Purely a VIEW cursor:
        /// it never changes what the drone is assigned to, which is what keeps reading decoupled from
        /// dispatching (c9d5f12).
        /// </summary>
        private int viewIndex;

        public override void Initialize()
        {
            base.Initialize();
            this.RefreshAll();
        }

        // --- Assignment ---

        private int AreaCount() => this.Parent is DroneDockObject dock ? dock.SurveyAreas.Count : 0;

        /// <summary>
        /// Assigns the area at 1-based <paramref name="position"/>, or unassigns when it is already the
        /// assigned one -- so the same button both starts and stops work on that area.
        /// </summary>
        private void ToggleAssign(int position)
        {
            if (this.Parent is not DroneDockObject dock) return;
            if (position < 1 || position > dock.SurveyAreas.Count) return;

            var area = dock.SurveyAreas[position - 1];
            dock.AssignSurveyArea(dock.AssignedSurveyAreaId == area.Id ? 0 : area.Id);
            this.RefreshAll();
        }

        // --- Results view cursor ---

        private void CycleView(int direction)
        {
            var count = this.AreaCount();
            if (count == 0) { this.viewIndex = 0; this.RefreshResults(); return; }

            this.viewIndex = ((this.viewIndex + direction) % count + count) % count;
            this.RefreshResults();
        }

        /// <summary>Keeps the cursor in range after areas are added or deleted on the map.</summary>
        private void ClampViewCursor(DroneDockObject dock)
        {
            var count = dock.SurveyAreas.Count;
            this.viewIndex = count == 0 ? 0 : Math.Clamp(this.viewIndex, 0, count - 1);
        }

        private SurveyAreaEntry ViewedArea(DroneDockObject dock)
        {
            this.ClampViewCursor(dock);
            return dock.SurveyAreas.Count == 0 ? null : dock.SurveyAreas[this.viewIndex];
        }

        // --- Refresh ---

        public void RefreshAll()
        {
            this.RefreshAreas();
            this.RefreshResults();
        }

        public void RefreshAreas()
        {
            this.AreasDisplay = this.BuildAreasText();
            this.Changed(nameof(this.AreasDisplay));

            // Without this push the client never re-evaluates button visibility, so a newly created
            // area gains no button and a deleted one keeps its own until the dock is reopened.
            this.Changed(nameof(this.AreaExists1));
            this.Changed(nameof(this.AreaExists2));
            this.Changed(nameof(this.AreaExists3));
            this.Changed(nameof(this.AreaExists4));
            this.Changed(nameof(this.AreaExists5));
            this.Changed(nameof(this.AreaExists6));
            this.Changed(nameof(this.AreaExists7));
            this.Changed(nameof(this.AreaExists8));
            this.Changed(nameof(this.AreaExists9));
            this.Changed(nameof(this.AreaExists10));
        }

        public void RefreshResults()
        {
            this.ViewingDisplay = this.BuildViewingText();
            this.Changed(nameof(this.ViewingDisplay));

            this.ResultsDisplay = this.BuildResultsText();
            this.Changed(nameof(this.ResultsDisplay));
        }

        // --- Text ---

        private string BuildAreasText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(this.BuildAssignmentLine(dock)).Append("\n\n");

            if (dock.SurveyAreas.Count == 0)
            {
                sb.Append("No survey areas yet. Use Manage Areas on Map to draw your first one.");
                return sb.ToString();
            }

            var position = 1;
            foreach (var area in dock.SurveyAreas)
            {
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned]" : string.Empty;
                sb.Append(position++).Append(". ").Append(area.Name)
                  .Append(" -- ").Append(area.PlotCount).Append(" plots, ")
                  .Append(FormatAreaSummary(area, dock))
                  .Append(assigned).Append('\n');
            }

            if (dock.SurveyAreas.Count > AssignButtonPool)
                sb.Append("\nAreas past ").Append(AssignButtonPool)
                  .Append(" have no button -- assign them with /drone assignarea <id>.\n");

            return sb.ToString();
        }

        /// <summary>
        /// The authoritative "what is the drone working on" line. Three states, not two: assignment
        /// does not require a drone to exist, so without the no-drone variant the tab would report
        /// success while nothing happens in the world.
        /// </summary>
        private string BuildAssignmentLine(DroneDockObject dock)
        {
            var area = dock.AssignedSurveyArea;
            if (area == null)
                return "Assigned: none -- pick an area below to start surveying.";

            var position = dock.SurveyAreas.IndexOf(area) + 1;
            var drone = dock.SpawnedDrone;
            var hasDrone = drone != null && !drone.IsDestroyed;

            return hasDrone
                ? $"Assigned: {position} -- {area.Name}"
                : $"Assigned: {position} -- {area.Name} (no drone -- build and dock one to start surveying)";
        }

        /// <summary>
        /// Compact "coverage%, top find" summary for an area's list line, from its snapshot. Honours the
        /// dock's material filter so the highlighted find is the one the player is targeting.
        /// </summary>
        private static string FormatAreaSummary(SurveyAreaEntry area, DroneDockObject dock)
        {
            var top = area.ReadFindings()
                .Where(f => f.Found && dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .FirstOrDefault();

            if (top.Found)
                return $"{area.CoveragePercent:F0}% surveyed, most {top.OreType} (~{top.Count} blocks)";
            if (area.CoveragePercent > 0f)
                return $"{area.CoveragePercent:F0}% surveyed, nothing matching";
            return "not surveyed yet";
        }

        private string BuildViewingText()
        {
            if (this.Parent is not DroneDockObject dock) return string.Empty;

            var area = this.ViewedArea(dock);
            if (area == null) return "Viewing: no areas yet.";

            var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned]" : string.Empty;
            return $"Viewing: {this.viewIndex + 1} of {dock.SurveyAreas.Count} -- {area.Name}{assigned}";
        }

        private string BuildResultsText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            this.ApplyPickerSelection(dock);

            var entry = this.ViewedArea(dock);
            var sb = new StringBuilder();

            if (entry == null)
            {
                sb.Append("Draw an area on the map, then assign the drone to survey it.");
                AppendDroneStatusFooter(sb, dock);
                return sb.ToString();
            }

            // Findings persist with the area (KTD11): these are entry's own, kept until it is edited or
            // deleted -- shown even while the drone is between areas or docked. The material filter
            // narrows what is DISPLAYED; everything stays recorded.
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

            // How far down the survey looked, and the area's terrain baseline.
            if (entry.SurveyDepth > 0)
                sb.Append("Scanned to ").Append(entry.SurveyDepth)
                  .Append(" blocks below surface; median surface level ").Append(entry.MedianSurface).Append(".\n");

            if (dock.MaterialFilter.Count > 0)
                sb.Append("Filtered to: ").Append(string.Join(", ", dock.MaterialFilter)).Append('\n');

            AppendDroneStatusFooter(sb, dock);
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
                return "Not surveyed yet. Assign the drone to this area to survey it.";
            if (entry.CoveragePercent >= 99.5f)
                return "Survey complete -- nothing found in this area.";
            return $"Surveyed {entry.CoveragePercent:F0}% so far -- nothing found yet.";
        }

        /// <summary>
        /// Appends the drone's live status as a separated FOOTER -- what the drone is DOING (its
        /// assigned area), kept out of the viewed area's survey data above it.
        /// </summary>
        private static void AppendDroneStatusFooter(StringBuilder sb, DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone != null && !drone.IsDestroyed && drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                sb.Append("\nDrone: ").Append(lifecycle.Status)
                  .Append(" (").Append(dock.AssignedSurveyArea?.Name ?? "no area assigned").Append(')');
        }

        // --- Material picker projection ---

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

            // Only rewrite when the selection actually differs, so the 1s refresh tick does not fight
            // a filter set from chat.
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
