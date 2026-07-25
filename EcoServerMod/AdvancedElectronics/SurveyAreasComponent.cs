using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's "Survey Areas" tab (U7, R1/R1a/R1c/R2a/R7): create and list the dock's own
    /// survey areas. (Assign/rename/delete follow once a safe targeting control is chosen.)
    ///
    /// Render pattern (settled live, L2 batch 10): a mod component tab renders read-only text
    /// through a SETTABLE string property with <c>UITypeName("StringDisplay")</c> that is
    /// explicitly assigned and <c>Changed</c>-notified — NOT a never-assigned computed
    /// <c>LocString</c> getter (that was the earlier failure). A
    /// <c>[RPC(AccessType.ConsumerAccess), UITypeName("BigButton")]</c> button renders and fires
    /// with auth engine-enforced. A synced *collection* of non-View values still crashes the
    /// client, so the list is composed as one text block.
    /// See <c>docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md</c>.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey Areas", true), HasIcon]
    public class SurveyAreasComponent : WorldObjectComponent
    {
        // v1 tier cap (R1b). Drone-tier-owned later; a constant here so one value ships.
        private const int MaxAreaPlots = 40;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>
        /// The dock's survey areas as read-only text. A settable string set in
        /// <see cref="Initialize"/> and after every mutation (the render pattern above) — a
        /// computed getter here does not render.
        /// </summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        public override void Initialize()
        {
            base.Initialize();
            this.RefreshAreaText();
        }

        /// <summary>
        /// Opens the map editor for the player to draw a new survey area, then stores it on the
        /// dock (U6). Async because <c>EditMap</c> is an awaited client round-trip. Auth is
        /// engine-enforced at <see cref="AccessType.ConsumerAccess"/>.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"),
         Description("Create Area — draw a new survey area on the map.")]
        public async Task CreateArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock)
                return;

            var name = $"Survey Area {dock.SurveyAreas.Count + 1}";
            await SurveyAreaPicker.PickAndCreate(player, dock, MaxAreaPlots, name);
            this.RefreshAreaText();
        }

        /// <summary>Recomputes the area list text and notifies the client. Call after any change to the dock's areas.</summary>
        public void RefreshAreaText()
        {
            this.AreasDisplay = this.BuildAreasText();
            this.Changed(nameof(this.AreasDisplay));
        }

        private string BuildAreasText()
        {
            if (this.Parent is not DroneDockObject dock || dock.SurveyAreas.Count == 0)
                return "No survey areas yet. Use Create Area to draw one on the map.";

            var sb = new StringBuilder("Survey areas:\n");
            foreach (var area in dock.SurveyAreas)
            {
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned]" : string.Empty;
                sb.Append(area.Id).Append(". ").Append(area.Name)
                  .Append(" — ").Append(area.PlotCount).Append(" plots").Append(assigned).Append('\n');
            }

            return sb.ToString();
        }
    }
}
