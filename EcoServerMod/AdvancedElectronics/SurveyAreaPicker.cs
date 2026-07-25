using System.Collections.Generic;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Players;
using Eco.Shared.Gameplay;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Opens the game's map-editing interface for a survey area (U6/U7): create a new area,
    /// edit an existing one's plots, or view one read-only. Uses the deed pattern proven live
    /// (AllowNewEntries=false, one fixed editable entry, EntryStatus.MaxArea cap,
    /// RelatedRegistrar unset; KTD8). The dock is mutated only after a valid confirm, so a
    /// cancel/disconnect (null return) leaves everything untouched (R9).
    /// </summary>
    public static class SurveyAreaPicker
    {
        private const int EditableEntryId = 1;

        /// <summary>Create: draw a new area from an empty map and store it on the dock. Returns the new entry, or null on cancel/empty/over-cap.</summary>
        public static async Task<SurveyAreaEntry> PickAndCreate(Player player, DroneDockObject dock, int maxPlots, string name)
        {
            if (player == null || dock == null) return null;

            var plots = await RunPicker(player, BuildRequest(name, maxPlots, null, readOnly: false));
            if (plots == null || plots.Count == 0) return null;
            if (plots.Count > maxPlots) { player.User?.MsgLocStr($"Survey area too large: {plots.Count} plots, limit {maxPlots}. Nothing was created."); return null; }

            return dock.CreateSurveyArea(name, plots);
        }

        /// <summary>Edit: open the editor pre-painted with the area's current plots, and on confirm replace them. No-op on cancel or empty draw (an area is never edited down to nothing).</summary>
        public static async Task PickAndEdit(Player player, DroneDockObject dock, SurveyAreaEntry entry, int maxPlots)
        {
            if (player == null || dock == null || entry == null) return;

            var plots = await RunPicker(player, BuildRequest(entry.Name, maxPlots, entry.Plots(), readOnly: false));
            if (plots == null || plots.Count == 0) return;
            if (plots.Count > maxPlots) { player.User?.MsgLocStr($"Survey area too large: {plots.Count} plots, limit {maxPlots}. No change made."); return; }

            entry.SetPlots(plots);
        }

        /// <summary>View: open the editor read-only, pre-painted with the area's plots, so the player can see it on the map without changing it.</summary>
        public static async Task PickView(Player player, DroneDockObject dock, SurveyAreaEntry entry, int maxPlots)
        {
            if (player == null || dock == null || entry == null) return;
            await RunPicker(player, BuildRequest(entry.Name, maxPlots, entry.Plots(), readOnly: true));
        }

        private static MapEditRequest BuildRequest(string name, int maxPlots, IEnumerable<PlotCoord> existing, bool readOnly)
        {
            var map = new Array2D<int>(PlotUtil.WorldPlotDims);
            if (existing != null)
                foreach (var plot in existing)
                    map[new Vector2i(plot.X, plot.Z)] = EditableEntryId;

            return new MapEditRequest
            {
                MapHintTitle    = readOnly ? "Survey Area (view)" : "Survey Area",
                MapHint         = Localizer.DoStr(readOnly
                    ? "Viewing the survey area. Close when done."
                    : "Draw the area for the drone to survey, then confirm."),
                AllowNewEntries = false,
                Readonly        = readOnly,
                Overlay = new EditableOverlay
                {
                    Name       = string.IsNullOrWhiteSpace(name) ? "Survey Area" : name,
                    Map        = map,
                    MapEntries = new()
                    {
                        { EditableEntryId, new MapEntry { MapEntryId = EditableEntryId, Color = Color.Green, EntryDescription = Localizer.DoStr("Survey area") } },
                    },
                },
                EntryStatus = new()
                {
                    { EditableEntryId, new EditableEntryStatus { AllowNameChange = false, AllowDelete = false, Readonly = readOnly, MaxArea = maxPlots } },
                },
            };
        }

        private static async Task<List<PlotCoord>> RunPicker(Player player, MapEditRequest request)
        {
            var edited = await player.EditMap(request);
            if (edited?.Map == null)
                return null; // cancelled, no client, or disconnected.

            var plots = new List<PlotCoord>();
            edited.Map.ForEach((pos, index) =>
            {
                if (edited.Map[pos] == EditableEntryId)
                    plots.Add(new PlotCoord(pos.X, pos.Y));
            });
            return plots;
        }
    }
}
