using System.Collections.Generic;
using System.Linq;
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
    /// Opens the game's map editor as the dock's AREA MANAGER (U1): every area the dock owns appears
    /// at once as a named entry, and creating, renaming, redrawing and deleting all happen there —
    /// the same way districts are managed (<c>DistrictMap.EditAsync</c> / <c>OnMapEdited</c> in the Eco
    /// source). This replaces the old one-area-at-a-time Create/Edit/View/Delete buttons.
    ///
    /// The dock is mutated only after a confirm, so a cancel or disconnect (null return) leaves
    /// everything untouched.
    /// </summary>
    public static class SurveyAreaPicker
    {
        // Entry id handed to the seeded placeholder when the dock owns no areas yet. Any positive id
        // is safe there precisely because no area exists to collide with.
        private const int PlaceholderEntryId = 1;

        // Distinct colours so areas are told apart on the map; cycled by position.
        private static readonly Color[] EntryColors =
        {
            Color.Green, Color.Blue, Color.Yellow, Color.Orange, Color.Red, Color.Cyan,
        };

        /// <summary>
        /// Opens the map editor on all of the dock's areas and applies whatever the player confirms.
        /// </summary>
        public static async Task ManageAreas(Player player, DroneDockObject dock, int maxPlots)
        {
            if (player == null || dock == null) return;

            var edited = await player.EditMap(BuildRequest(dock, maxPlots));

            // Cancelled, disconnected, or a malformed round-trip. MapEntries is checked as well as Map
            // because the reconcile below treats "absent from MapEntries" as a deletion -- without this
            // guard a partial return would wipe every area and its findings, and the dock has no undo.
            if (edited?.Map == null || edited.MapEntries == null) return;

            Reconcile(player, dock, edited, maxPlots);
        }

        /// <summary>
        /// Builds the multi-entry request: one entry per area with its plots painted, rename/delete
        /// enabled per entry, and the plot cap attached.
        /// </summary>
        private static MapEditRequest BuildRequest(DroneDockObject dock, int maxPlots)
        {
            var map        = new Array2D<int>(PlotUtil.WorldPlotDims);
            var mapEntries = new Dictionary<int, MapEntry>();
            var entryStatus = new Dictionary<int, EditableEntryStatus>();

            var index = 0;
            foreach (var area in dock.SurveyAreas)
            {
                foreach (var plot in area.Plots())
                    map[new Vector2i(plot.X, plot.Z)] = area.Id;

                mapEntries[area.Id] = new MapEntry
                {
                    MapEntryId       = area.Id,
                    Color            = EntryColors[index++ % EntryColors.Length],
                    EntryDescription = area.Name,
                };

                // Per-entry status is what actually enables the rename field and delete button on the
                // client -- DefaultEntryStatus is consulted only for ids ABSENT from this dictionary,
                // and every existing area is present here because this is also where the cap lives.
                entryStatus[area.Id] = new EditableEntryStatus
                {
                    AllowNameChange = true,
                    AllowDelete     = true,
                    Readonly        = false,
                    MaxArea         = maxPlots,
                };
            }

            // A dock with no areas would otherwise open an editor with nothing to draw into, and the
            // map is the ONLY creation path now -- so seed a placeholder the player can draw straight
            // into. Confirming with nothing drawn simply creates nothing.
            if (mapEntries.Count == 0)
            {
                mapEntries[PlaceholderEntryId] = new MapEntry
                {
                    MapEntryId       = PlaceholderEntryId,
                    Color            = EntryColors[0],
                    EntryDescription = "Survey Area 1",
                };
                entryStatus[PlaceholderEntryId] = new EditableEntryStatus
                {
                    AllowNameChange = true,
                    AllowDelete     = true,
                    Readonly        = false,
                    MaxArea         = maxPlots,
                };
            }

            return new MapEditRequest
            {
                MapHintTitle    = DroneDockObject.DroneAreaLabel + "s",
                MapHint         = Localizer.DoStr(
                    "Draw the areas for the drone to survey. Add, rename, redraw or delete areas here, then confirm."),
                AllowNewEntries = true,
                AllowNameChange = true, // the overlay's own title -- entry renaming comes from EntryStatus
                Readonly        = false,
                Overlay = new EditableOverlay
                {
                    Name       = DroneDockObject.DroneAreaLabel + "s",
                    Map        = map,
                    MapEntries = mapEntries,
                },
                EntryStatus        = entryStatus,
                DefaultEntryStatus = new EditableEntryStatus
                {
                    AllowNameChange = true,
                    AllowDelete     = true,
                    Readonly        = false,
                    MaxArea         = maxPlots,
                },
            };
        }

        /// <summary>
        /// Applies the confirmed map to the dock: entries the player removed are deleted, ids the dock
        /// does not know are created, and known ids get their name refreshed and their plots replaced
        /// ONLY when the geometry actually changed.
        ///
        /// Unlike districts this needs no id re-keying: the dock never stores the map array, so plots
        /// are read out against the ids the client returned and handed straight to
        /// <see cref="DroneDockObject.CreateSurveyArea"/>, which assigns the dock's own id.
        /// </summary>
        private static void Reconcile(Player player, DroneDockObject dock, IMapEntryOverlay edited, int maxPlots)
        {
            var plotsById = PlotsByEntryId(edited);

            // Deletions first: an area whose entry the player removed is gone, along with its findings.
            // DeleteSurveyArea also unassigns the drone when it was working that area.
            foreach (var area in dock.SurveyAreas.ToList())
                if (!edited.MapEntries.ContainsKey(area.Id))
                    dock.DeleteSurveyArea(area.Id);

            foreach (var pair in edited.MapEntries)
            {
                var entryId = pair.Key;
                var name    = pair.Value.EntryDescription;
                var plots   = plotsById.TryGetValue(entryId, out var p) ? p : new List<PlotCoord>();
                var area    = dock.SurveyAreas.FirstOrDefault(a => a.Id == entryId);

                // The client's MaxArea is a hint, not a guarantee -- re-check server-side, exactly as
                // the single-area picker did, so an over-cap area never reaches the drone's sweep.
                if (plots.Count > maxPlots)
                {
                    player.User?.MsgLocStr(
                        $"Survey area '{name}' is too large: {plots.Count} plots, limit {maxPlots}. That area was left unchanged.");
                    continue;
                }

                if (area == null)
                {
                    // A new entry. An entry drawn with no plots is not an area; skip it silently so a
                    // confirmed-but-untouched placeholder does not create an empty area.
                    if (plots.Count == 0) continue;

                    // Hard cap at the control pool. The map editor will happily accept any number of
                    // entries, and an area with no control is one a player cannot assign from the
                    // panel -- so refusing here is kinder than creating something half-usable.
                    if (dock.SurveyAreas.Count >= SurveyComponent.MaxSurveyAreas)
                    {
                        player.User?.MsgLocStr(
                            $"'{name}' was not created: a dock holds at most {SurveyComponent.MaxSurveyAreas} survey areas. Delete one first.");
                        continue;
                    }

                    dock.CreateSurveyArea(ResolveNewAreaName(dock, name), plots);
                    continue;
                }

                // Renaming must NOT clear findings -- it is not a redraw.
                if (!string.IsNullOrWhiteSpace(name) && name != area.Name)
                    dock.RenameSurveyArea(area.Id, name);

                // Replace plots only on a real geometry change: SetPlots clears the area's findings and
                // OnAreaEdited bumps the re-dispatch epoch, so replacing unconditionally would wipe
                // every area's survey data on every confirm, including areas the player never touched.
                if (plots.Count > 0 && !SamePlots(area.Plots(), plots))
                {
                    area.SetPlots(plots);
                    dock.OnAreaEdited(area.Id);
                }
            }
        }

        /// <summary>
        /// The name a newly drawn area should carry. The map editor is a shared civics surface, so an
        /// entry the player did not name comes back with the client's own default ("New District") --
        /// meaningless on a drone dock. Any such placeholder becomes the next free "Survey Area N",
        /// numbered by what the dock already owns rather than by area count, so deleting area 2 of 3
        /// does not make the next one collide with area 3.
        /// </summary>
        private static string ResolveNewAreaName(DroneDockObject dock, string returnedName)
        {
            if (!IsPlaceholderName(returnedName)) return returnedName;

            var taken = dock.SurveyAreas.Select(a => a.Name).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            for (var n = 1; ; n++)
            {
                var candidate = $"Survey Area {n}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>True when the name is blank or one of the client's own defaults for a new entry.</summary>
        private static bool IsPlaceholderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;

            var trimmed = name.Trim();
            return trimmed.Equals("New District", System.StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("New Entry", System.StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("District", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Plots drawn for each entry id, read out of the returned map array.</summary>
        private static Dictionary<int, List<PlotCoord>> PlotsByEntryId(IMapEntryOverlay edited)
        {
            var plotsById = new Dictionary<int, List<PlotCoord>>();
            edited.Map.ForEach((pos, index) =>
            {
                var id = edited.Map[pos];
                if (id == 0) return; // unpainted

                if (!plotsById.TryGetValue(id, out var list))
                {
                    list = new List<PlotCoord>();
                    plotsById[id] = list;
                }
                list.Add(new PlotCoord(pos.X, pos.Y));
            });
            return plotsById;
        }

        /// <summary>Order-insensitive plot-set comparison -- the test for "did the geometry change".</summary>
        private static bool SamePlots(IEnumerable<PlotCoord> a, IEnumerable<PlotCoord> b)
        {
            var setA = new HashSet<PlotCoord>(a);
            var setB = new HashSet<PlotCoord>(b);
            return setA.SetEquals(setB);
        }
    }
}
