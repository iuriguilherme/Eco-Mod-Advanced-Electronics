using System.ComponentModel;
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
    /// assign/rename/delete the dock's own survey areas. Built on the exact client-render
    /// surfaces U1 proved:
    ///   - a mod component tab renders (`[Serialized, CreateComponentTabLoc, HasIcon]`,
    ///     `Availability = UI`);
    ///   - a `[RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton")]` button
    ///     renders and fires with auth engine-enforced;
    ///   - a `[SyncToView] UITypeName("StringDisplay")` `LocString` is the vanilla read-only
    ///     text member (`PartsComponent.Description`, `ForSaleComponent.Note`) — used here for
    ///     the area list, because a synced *collection* of mod values crashes the client.
    ///
    /// v1 scope: the area list (text) plus the Create action (the hard part — drawing via the
    /// map editor). Per-area assign/rename/delete need a targeting widget the engine does not
    /// obviously give a mod (Selector wants a GamePickerList of a viewed type); those land once
    /// the text-render loop below is confirmed live and a safe targeting control is chosen.
    /// See `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey Areas", true), HasIcon]
    public class SurveyAreasComponent : WorldObjectComponent
    {
        // v1 tier cap (R1b). Drone-tier-owned later; a constant here so one value ships.
        private const int MaxAreaPlots = 40;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>The dock's survey areas rendered as read-only text (StringDisplay). Refreshed after every mutation.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public LocString AreasDisplay => this.BuildAreasText();

        /// <summary>
        /// Opens the map editor for the player to draw a new survey area, then stores it on the
        /// dock (U6). Async because <c>EditMap</c> is an awaited client round-trip — the same
        /// shape as the engine's own <c>Deed.EditInMap</c> RPC. Auth is engine-enforced at
        /// <see cref="AccessType.ConsumerAccess"/>.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"),
         Description("Create Area — draw a new survey area on the map.")]
        public async Task CreateArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock)
                return;

            var name = $"Survey Area {dock.SurveyAreas.Count + 1}";
            await SurveyAreaPicker.PickAndCreate(player, dock, MaxAreaPlots, name);
            this.Changed(nameof(this.AreasDisplay));
        }

        private LocString BuildAreasText()
        {
            if (this.Parent is not DroneDockObject dock || dock.SurveyAreas.Count == 0)
                return Localizer.DoStr("No survey areas yet. Use Create Area to draw one on the map.");

            var sb = new LocStringBuilder();
            foreach (var area in dock.SurveyAreas)
            {
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned]" : string.Empty;
                sb.AppendLine(Localizer.DoStr($"{area.Id}. {area.Name} — {area.PlotCount} plots{assigned}"));
            }

            return sb.ToLocString();
        }
    }
}
