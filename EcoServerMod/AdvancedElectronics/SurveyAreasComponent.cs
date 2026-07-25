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
    /// The dock's "Survey Areas" tab (U7, R1/R1a/R1c/R2a/R7): create, list, and (later)
    /// assign/rename/delete the dock's own survey areas.
    ///
    /// Render note (U1 + L2 findings): the tab frame and a
    /// `[RPC(AccessType.ConsumerAccess), UITypeName("BigButton")]` button render for a mod
    /// component, but a NEVER-ASSIGNED computed `LocString` getter with
    /// `UITypeName("StringDisplay")` did NOT (L2). The stock members that DO render read-only
    /// text (`ForSaleComponent.Note`, `ConstitutionComponent.DisplayText`) are SETTABLE string
    /// properties that are explicitly assigned. This revision follows that shape: the text is a
    /// settable property, set in <see cref="Initialize"/> and after every mutation with a
    /// <c>Changed</c> notification.
    ///
    /// This deploy carries THREE display-member variants at once (StringDisplay, StringTitle,
    /// and a plain no-UITypeName property) so a single restart reveals which shape a mod
    /// component actually renders — the batched-variant rule, not one guess per restart. Once
    /// the winner is known, the losers are removed and U9's results tab uses the same member.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey Areas", true), HasIcon]
    public class SurveyAreasComponent : WorldObjectComponent
    {
        // v1 tier cap (R1b). Drone-tier-owned later; a constant here so one value ships.
        private const int MaxAreaPlots = 40;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // --- Display-member variants under test (one restart discriminates) ---

        /// <summary>Variant A: settable string via StringDisplay (the ForSaleComponent.Note shape).</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = "A(StringDisplay): loading...";

        /// <summary>Variant B: settable string via StringTitle.</summary>
        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string AreasTitle { get; private set; } = "B(StringTitle): loading...";

        /// <summary>Variant C: plain synced string, no UITypeName (default rendering).</summary>
        [SyncToView]
        public string AreasPlain { get; private set; } = "C(plain): loading...";

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

        /// <summary>Recomputes the area text into all display variants and notifies the client.</summary>
        private void RefreshAreaText()
        {
            var text = this.BuildAreasText();
            this.AreasDisplay = "A(StringDisplay):\n" + text;
            this.AreasTitle   = "B(StringTitle): " + text;
            this.AreasPlain   = "C(plain):\n" + text;
            this.Changed(nameof(this.AreasDisplay));
            this.Changed(nameof(this.AreasTitle));
            this.Changed(nameof(this.AreasPlain));
        }

        private string BuildAreasText()
        {
            if (this.Parent is not DroneDockObject dock || dock.SurveyAreas.Count == 0)
                return "No survey areas yet. Use Create Area to draw one on the map.";

            var sb = new StringBuilder();
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
