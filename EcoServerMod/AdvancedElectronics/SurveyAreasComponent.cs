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
        /// The area id the action buttons operate on (R7 select-by-id). Editable by the player
        /// (the user's chosen selection method); also set by the Prev/Next cycle buttons in case
        /// the editable field does not render for a mod component.
        /// </summary>
        [Eco(RequiredAccess = AccessType.ConsumerAccess, Serialized = false)]
        public int TargetAreaId { get; set; }

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
            this.Changed(nameof(this.TargetAreaId));
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

            var sb = new StringBuilder("Areas (set the id, then Assign/Edit/View/Delete):\n");
            foreach (var area in dock.SurveyAreas)
            {
                var selected = area.Id == this.TargetAreaId ? "> " : "  ";
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned]" : string.Empty;
                sb.Append(selected).Append(area.Id).Append(". ").Append(area.Name)
                  .Append(" -- ").Append(area.PlotCount).Append(" plots").Append(assigned).Append('\n');
            }
            return sb.ToString();
        }

        private string BuildResultsText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            var sb = new StringBuilder("Survey results\n");
            sb.Append("Assigned area: ").Append(dock.AssignedSurveyArea?.Name ?? "(none)").Append('\n');

            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed || !drone.TryGetComponent<OreSensorComponent>(out var sensor))
            {
                sb.Append("No drone is out surveying. Insert a Survey Drone and assign an area.");
                return sb.ToString();
            }

            if (drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                sb.Append("Drone: ").Append(lifecycle.Status).Append('\n');

            var results = sensor.SampledOreTypes
                .Select(oreType => (OreType: oreType, Result: sensor.DensestCell(oreType)))
                .Where(entry => entry.Result.Found)
                .OrderByDescending(entry => entry.Result.Ratio)
                .ToList();

            if (results.Count == 0)
            {
                sb.Append("Nothing found yet. The drone reports as it roams -- give it time to cover ground.");
                return sb.ToString();
            }

            foreach (var entry in results)
                sb.Append(DockReadout.FormatOreLine(entry.OreType, entry.Result)).Append('\n');

            var coverage = DockReadout.ComputeCoveragePercent(results.Select(e => (e.OreType, e.Result)).ToList());
            sb.Append("Coverage: ").Append(coverage.ToString("F0")).Append('%');
            return sb.ToString();
        }
    }
}
