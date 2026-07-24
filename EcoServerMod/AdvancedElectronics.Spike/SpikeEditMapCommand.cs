using System;
using System.Threading.Tasks;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.Gameplay;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;

namespace AdvancedElectronics.Spike
{
    /// <summary>
    /// U1 SPIKE (picker half): can a server MOD open the map-editing interface
    /// and get the drawn plots back, WITHOUT being the district/deed system?
    ///
    /// The survey-area design needs exactly this: the dock opens the same plot
    /// editor district editing uses, the player draws an area, and the mod stores
    /// the returned plots in its own registrar — no civics object, no persistent
    /// overlay layer (the client's OverlayManager only tracks districts + influence
    /// maps, so a mod cannot add a passive layer, and per the design that display
    /// is out of scope anyway). This command exercises the editor via the deed
    /// pattern (one editable entry, a MaxArea cap, AllowNewEntries=false,
    /// RelatedRegistrar left unset) and reports how many plots came back.
    ///
    /// Run "/spike editmap" in game. Expected: the map editor opens, you draw an
    /// area and confirm, and the chat reports the plot count. A failure to open,
    /// or an empty/error return, tells us the picker needs a different wiring
    /// (e.g. RelatedRegistrar must be set) before U6 is built.
    /// </summary>
    [ChatCommandHandler]
    public static class SpikeEditMapCommand
    {
        // One editable map entry. Its id is written nowhere in the initial map
        // (nothing pre-selected); the client paints it as the player draws, and
        // the returned Map carries this id at every drawn plot.
        private const int EditableEntryId = 1;

        // Homestead-scale cap (R1b): a few dozen plots, enforced client-side via
        // EntryStatus.MaxArea. The real value becomes a drone-tier property later.
        private const int MaxPlots = 40;

        [ChatSubCommand("Spike", "U1: open the map editor as a mod and report the drawn plots.", ChatAuthorizationLevel.Admin)]
        public static async Task EditMap(User user)
        {
            try
            {
                var request = new MapEditRequest
                {
                    MapHintTitle   = "Survey Area (spike)",
                    MapHint        = Localizer.DoStr("Draw the survey area, then confirm. This is a spike — nothing is saved."),
                    AllowNewEntries = false,
                    Overlay = new EditableOverlay
                    {
                        Name       = "Survey Area (spike)",
                        Map        = new Array2D<int>(PlotUtil.WorldPlotDims),
                        MapEntries = new()
                        {
                            { EditableEntryId, new MapEntry { MapEntryId = EditableEntryId, Color = Color.Green, EntryDescription = Localizer.DoStr("Survey area") } },
                        },
                    },
                    EntryStatus = new()
                    {
                        { EditableEntryId, new EditableEntryStatus { AllowNameChange = false, AllowDelete = false, MaxArea = MaxPlots } },
                    },
                    // RelatedRegistrar deliberately left unset — the deed pattern. If the
                    // editor fails to open because of this, that is the finding.
                };

                user.MsgLocStr("[U1 editmap] Opening map editor (draw an area and confirm) ...");
                var edited = await user.Player.EditMap(request);

                if (edited?.Map == null)
                {
                    user.MsgLocStr("[U1 editmap] RESULT: editor returned null — cancelled, no client, or the picker did not open for a mod caller.");
                    return;
                }

                var drawn = 0;
                var sampleCoords = "";
                edited.Map.ForEach((pos, index) =>
                {
                    if (edited.Map[pos] != EditableEntryId) return;
                    drawn++;
                    if (drawn <= 5) sampleCoords += $" ({pos.X},{pos.Y})";
                });

                user.MsgLocStr($"[U1 editmap] RESULT: editor opened and returned {drawn} drawn plot(s).{(drawn > 0 ? " First few:" + sampleCoords : "")}");
                user.MsgLocStr("[U1 editmap] If this reported a plot count, the mod can drive the plot editor — U6 is viable via the deed pattern.");
            }
            catch (Exception e)
            {
                user.MsgLocStr($"[U1 editmap] FAIL: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
