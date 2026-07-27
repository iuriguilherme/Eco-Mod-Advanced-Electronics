using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
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
    /// The dock's "Results" tab (U3): read one survey area's findings at a time. Members render in
    /// declaration order — material picker, the area being viewed, Prev/Next, then that area's
    /// findings. Prev/Next sit ABOVE the findings because findings are variable-length, so
    /// bottom-anchored controls would drift off-screen exactly when an area has many materials.
    ///
    /// Selecting is NOT assigning: this tab's cursor is independent of the drone's assignment, so any
    /// area's findings can be read without dispatching the drone there (the decoupling shipped in
    /// c9d5f12). Managing and assigning areas lives on the sibling "Areas" tab.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Results", true), HasIcon]
    public class SurveyResultsComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

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

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Previous Area")]
        public void ViewPrev(Player player) => this.CycleView(-1);

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Next Area")]
        public void ViewNext(Player player) => this.CycleView(+1);

        /// <summary>The viewed area's findings (R7). Refreshed from the dock's tick.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ResultsDisplay { get; private set; } = string.Empty;

        /// <summary>
        /// Index into the dock's area list of the area whose findings are shown. Purely a VIEW cursor:
        /// it never changes what the drone is assigned to.
        /// </summary>
        private int viewIndex;

        public override void Initialize()
        {
            base.Initialize();
            this.RefreshAll();
        }

        private void CycleView(int direction)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var count = dock.SurveyAreas.Count;
            if (count == 0) { this.viewIndex = 0; this.RefreshAll(); return; }

            this.viewIndex = ((this.viewIndex + direction) % count + count) % count;
            this.RefreshAll();
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

        public void RefreshAll()
        {
            this.ViewingDisplay = this.BuildViewingText();
            this.Changed(nameof(this.ViewingDisplay));

            this.ResultsDisplay = this.BuildResultsText();
            this.Changed(nameof(this.ResultsDisplay));
        }

        private string BuildViewingText()
        {
            if (this.Parent is not DroneDockObject dock) return string.Empty;

            var area = this.ViewedArea(dock);
            if (area == null) return "Viewing: no areas yet -- draw one from the Areas tab.";

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
