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
    /// The dock's single "Survey" tab (U7 + U9): manage survey areas AND read the drone's
    /// findings in one place. Consolidated into one component because a SECOND mod component
    /// declaring its own tab did not register a tab on the client (the separate Survey Results
    /// tab never appeared); one component with two text sections renders reliably.
    ///
    /// Client-render surfaces used (all settled live): StringDisplay read-only text (settable +
    /// Changed), BigButton ConsumerAccess RPCs, and an editable [Eco] int field for select-by-id.
    /// The editable field's render is the one unproven piece — so Prev/Next cycle buttons (pure
    /// BigButtons, proven) also set the target id, guaranteeing a working selection either way.
    /// See docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
    public class SurveyAreasComponent : WorldObjectComponent
    {
        private const int MaxAreaPlots = 40; // v1 tier cap (R1b); drone-tier-owned later.

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>The dock's areas as read-only text, with the selected id marked.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        /// <summary>The drone's survey findings as read-only text (R7). Refreshed from the dock's tick.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ResultsDisplay { get; private set; } = string.Empty;

        // Material targets: pick which materials the survey results show, the same way items and tags
        // are picked in a recipe or a law. Both empty = show everything found.
        //
        /// <summary>
        /// Material targets: pick which materials the survey results show, the same way items and tags
        /// are picked in a recipe or a law. Empty shows everything found.
        ///
        /// Scoped by our OWN tag, applied at startup to exactly the items whose block the drone can
        /// detect (<see cref="SurveyMaterialTagger"/>). No stock tag expresses that set -- "Minable" is
        /// a block tag so an item picker scoped to it is empty, "Diggable" yields
        /// compost/dirt/garbage/tailings while missing clay and peat, crushed sits under "Excavatable",
        /// and plain BlockItem drags in crafted buildables (ashlar, brick, lumber) the drone can never
        /// find. Because the tag is applied from the same classifier the sensor uses, the picker offers
        /// exactly the detectable materials -- no junk, nothing missing, and the two cannot drift.
        ///
        /// Confirmed live: GamePickerList renders and filters from a WorldObjectComponent tab, even
        /// though every vanilla usage is inside a civics GameValue.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(SurveyMaterials.TargetTag)]
        [LocDescription("Materials to show in the survey results. Leave empty to show everything found.")]
        public GamePickerList<BlockItem> MaterialTargets { get; set; } = new();

        /// <summary>
        /// The area id the action buttons operate on. Set by the Prev/Next cycle buttons. (The
        /// tried [Eco] numeric-stepper field rendered but did not apply the typed value for a mod
        /// component -- it is a quantity input, not a selector -- so selection is cycle-driven; a
        /// proper dropdown of a viewable area type is the next UI iteration.)
        /// </summary>
        private int TargetAreaId;

        public override void Initialize()
        {
            base.Initialize();

            // Recovery for the material picker: if the startup pass ran before the item registry was
            // populated it tagged nothing, and the picker would be empty. Re-running here (idempotent)
            // catches that case, since items are certainly loaded by the time a dock exists.
            SurveyMaterialTagger.EnsureTagged();

            this.RefreshAll();
        }

        // --- Selection (cycle fallback for the editable id field) ---

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Prev — select the previous area")]
        public void SelectPrev(Player player) => this.Cycle(-1);

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Next — select the next area")]
        public void SelectNext(Player player) => this.Cycle(+1);

        private void Cycle(int direction)
        {
            if (this.Parent is not DroneDockObject dock) return;
            var ids = dock.SurveyAreas.Select(a => a.Id).ToList();
            if (ids.Count == 0) { this.TargetAreaId = 0; this.RefreshAll(); return; }

            var idx = ids.IndexOf(this.TargetAreaId);
            idx = idx < 0 ? 0 : (idx + direction + ids.Count) % ids.Count;
            this.TargetAreaId = ids[idx];
            this.RefreshAreas();
        }

        // --- Actions on the selected area / new area ---

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Create — draw a new survey area on the map")]
        public async Task CreateArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            var name = $"Survey Area {dock.SurveyAreas.Count + 1}";
            var created = await SurveyAreaPicker.PickAndCreate(player, dock, MaxAreaPlots, name);
            if (created != null) this.TargetAreaId = created.Id;
            this.RefreshAll();
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign — send the drone to survey the selected area")]
        public void AssignArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            dock.AssignSurveyArea(this.TargetAreaId);
            this.RefreshAll();
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Unassign — recall the drone; it stops surveying")]
        public void UnassignArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            dock.AssignSurveyArea(0);
            this.RefreshAll();
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Edit — redraw the selected area's plots")]
        public async Task EditArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.PickAndEdit(player, dock, this.Selected(dock), MaxAreaPlots);
            this.RefreshAll();
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("View — show the selected area on the map (read-only)")]
        public async Task ViewArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.PickView(player, dock, this.Selected(dock), MaxAreaPlots);
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Delete — remove the selected area")]
        public void DeleteArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            dock.DeleteSurveyArea(this.TargetAreaId);
            this.RefreshAll();
        }

        private SurveyAreaEntry Selected(DroneDockObject dock) =>
            dock.SurveyAreas.FirstOrDefault(a => a.Id == this.TargetAreaId);

        // --- Refresh ---

        public void RefreshAll()
        {
            this.RefreshAreas();
            this.RefreshResults();
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

        /// <summary>The material names currently selected in one picker (empty when it is null or unset).</summary>
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

        public void RefreshAreas()
        {
            this.AreasDisplay = this.BuildAreasText();
            this.Changed(nameof(this.AreasDisplay));
        }

        public void RefreshResults()
        {
            this.ResultsDisplay = this.BuildResultsText();
            this.Changed(nameof(this.ResultsDisplay));
        }

        private string BuildAreasText()
        {
            if (this.Parent is not DroneDockObject dock || dock.SurveyAreas.Count == 0)
                return "No survey areas yet. Use Create to draw one on the map.";

            var sb = new StringBuilder();
            sb.Append("Prev/Next selects an area; Assign/Edit/View/Delete act on the selected one.\n\n");

            // One compact line per area, decoupled from assignment: coverage and the strongest
            // find are read straight off each area's persisted snapshot, so every area's data shows
            // whether or not the drone is assigned to it.
            foreach (var area in dock.SurveyAreas)
            {
                var selected = area.Id == this.TargetAreaId ? "> " : "   ";
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned to drone]" : string.Empty;
                sb.Append(selected).Append(area.Name)
                  .Append(" -- ").Append(area.PlotCount).Append(" plots, ")
                  .Append(FormatAreaSummary(area, dock))
                  .Append(assigned).Append('\n');
            }
            return sb.ToString();
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

        private string BuildResultsText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            this.ApplyPickerSelection(dock);

            var sb = new StringBuilder("Survey results\n");
            var entry = this.Selected(dock);
            sb.Append("Selected area: ").Append(entry?.Name ?? "(none -- use Prev/Next)").Append("\n\n");

            if (entry == null)
            {
                sb.Append("No area selected. Use Create to draw one, or Prev/Next to pick one.");
                AppendDroneStatusFooter(sb, dock);
                return sb.ToString();
            }

            // Findings persist with the area (KTD11): these are entry's own, kept until it is
            // edited or deleted -- shown even while the drone is between areas or docked. The
            // material filter narrows what is DISPLAYED; everything stays recorded.
            var all = entry.ReadFindings().Where(f => f.Found).ToList();
            var findings = all
                .Where(f => dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .ToList();

            if (findings.Count == 0 && all.Count > 0)
            {
                sb.Append("No material in this area matches the current filter -- use Show All Materials.\n");
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
        /// Coverage-aware message when the selected area has no findings. Distinguishes "not started"
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
        /// assigned area), kept out of the selected area's survey data above it.
        /// </summary>
        private static void AppendDroneStatusFooter(StringBuilder sb, DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone != null && !drone.IsDestroyed && drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                sb.Append("\nDrone: ").Append(lifecycle.Status)
                  .Append(" (").Append(dock.AssignedSurveyArea?.Name ?? "no area assigned").Append(')');
        }
    }
}
