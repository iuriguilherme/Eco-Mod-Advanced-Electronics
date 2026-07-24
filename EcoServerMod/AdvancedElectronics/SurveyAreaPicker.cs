using System.Collections.Generic;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Players;
using Eco.Shared.Gameplay;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Opens the game's map-editing interface for a player to draw a new survey area's
    /// plots, then stores the drawn plots on the dock (U6, R1/R1b/R9). Uses the deed
    /// pattern proven live by <c>AdvancedElectronics.Spike/SpikeEditMapCommand.cs</c>:
    /// <c>AllowNewEntries = false</c>, one fixed editable entry, <c>EntryStatus.MaxArea</c>
    /// for the tier plot cap, <c>RelatedRegistrar</c> left unset, and the returned
    /// world-sized overlay diffed server-side rather than trusted (KTD8).
    ///
    /// Cancellation safety (R9): the dock is mutated ONLY after a valid, non-empty return,
    /// so a null return (no client, cancelled, or disconnected mid-edit) leaves the dock's
    /// areas and assignment exactly as they were — there is no pending state to unwind.
    /// </summary>
    public static class SurveyAreaPicker
    {
        // Single editable entry id; the client paints it onto every drawn plot and the
        // returned Map carries it at those positions. Fixed (not client-chosen).
        private const int EditableEntryId = 1;

        /// <summary>
        /// Shows <paramref name="player"/> the map editor, and on a confirmed non-empty
        /// selection within <paramref name="maxPlots"/> creates a survey area on
        /// <paramref name="dock"/> and returns it. Returns null on cancel/disconnect, an
        /// empty selection, or a selection the server-side re-validation rejects as over cap.
        /// </summary>
        public static async Task<SurveyAreaEntry> PickAndCreate(Player player, DroneDockObject dock, int maxPlots, string name)
        {
            if (player == null || dock == null)
                return null;

            var request = new MapEditRequest
            {
                MapHintTitle    = "Survey Area",
                MapHint         = Localizer.DoStr("Draw the area for the drone to survey, then confirm."),
                AllowNewEntries = false,
                Overlay = new EditableOverlay
                {
                    Name       = string.IsNullOrWhiteSpace(name) ? "Survey Area" : name,
                    Map        = new Array2D<int>(PlotUtil.WorldPlotDims),
                    MapEntries = new()
                    {
                        { EditableEntryId, new MapEntry { MapEntryId = EditableEntryId, Color = Color.Green, EntryDescription = Localizer.DoStr("Survey area") } },
                    },
                },
                EntryStatus = new()
                {
                    { EditableEntryId, new EditableEntryStatus { AllowNameChange = false, AllowDelete = false, MaxArea = maxPlots } },
                },
                // RelatedRegistrar deliberately unset — the deed pattern, confirmed working for a mod caller in U1.
            };

            var edited = await player.EditMap(request);
            if (edited?.Map == null)
                return null; // cancelled, no client, or disconnected — dock untouched.

            // Diff the returned world-sized map for plots painted with our entry id.
            // The editor works in plot-index space, which matches SurveyArea's plot
            // coordinates (world floor-div by plot length); v1 ignores world-wrap at the
            // map edges, acceptable for homestead-scale areas.
            var plots = new List<PlotCoord>();
            edited.Map.ForEach((pos, index) =>
            {
                if (edited.Map[pos] == EditableEntryId)
                    plots.Add(new PlotCoord(pos.X, pos.Y));
            });

            if (plots.Count == 0)
                return null; // nothing drawn — create nothing.

            // Server-side re-validation of the cap; the client is untrusted.
            if (plots.Count > maxPlots)
            {
                player.User?.MsgLocStr($"Survey area too large: {plots.Count} plots, limit {maxPlots}. Nothing was created.");
                return null;
            }

            return dock.CreateSurveyArea(name, plots);
        }
    }
}
