using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Networking;
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

            var selectedEntry = dock.SurveyAreas.FirstOrDefault(a => a.Id == this.TargetAreaId);
            var sb = new StringBuilder();
            sb.Append("Selected: ")
              .Append(selectedEntry != null ? $"{selectedEntry.Name} ({selectedEntry.PlotCount} plots)" : "(none -- use Prev/Next)")
              .Append("   <- Assign/Edit/View/Delete act on this\n\n");

            foreach (var area in dock.SurveyAreas)
            {
                var selected = area.Id == this.TargetAreaId ? "> " : "   ";
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned to drone]" : string.Empty;
                sb.Append(selected).Append(area.Name)
                  .Append(" -- ").Append(area.PlotCount).Append(" plots").Append(assigned).Append('\n');
            }
            return sb.ToString();
        }

        private string BuildResultsText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            var sb = new StringBuilder("Survey results\n");
            var entry = dock.AssignedSurveyArea;
            sb.Append("Assigned area: ").Append(entry?.Name ?? "(none)").Append('\n');

            var drone = dock.SpawnedDrone;
            if (drone != null && !drone.IsDestroyed && drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                sb.Append("Drone: ").Append(lifecycle.Status).Append('\n');

            if (entry == null)
            {
                sb.Append("No area assigned. Select an area and Assign it so the drone surveys it.");
                return sb.ToString();
            }

            // Findings persist with the area (KTD11): these are entry's own, kept until it is
            // edited or deleted -- shown even while the drone is between areas or docked.
            var findings = entry.ReadFindings()
                .Where(f => f.Found)
                .OrderByDescending(f => f.Concentration)
                .ToList();

            if (findings.Count == 0)
            {
                sb.Append("Nothing found yet. The drone reports as it roams -- give it time to cover ground.");
                return sb.ToString();
            }

            foreach (var f in findings)
                sb.Append(DockReadout.FormatOreLine(f)).Append('\n');

            sb.Append("Coverage: ").Append(entry.CoveragePercent.ToString("F0")).Append('%');
            return sb.ToString();
        }
    }
}
