using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Players;
using Eco.Shared.Gameplay;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Opens the game's map editor as the farm dock's area manager (U9, R5), mirroring
    /// <see cref="SurveyAreaPicker"/>: every farm area appears at once as a named entry,
    /// and creating, renaming, redrawing and deleting all happen there.
    ///
    /// The one behavioural difference from the survey picker is what a redraw costs. A
    /// survey redraw wipes the area's findings, because the old survey no longer describes
    /// the new shape. A farm redraw wipes nothing (R12): the crop, the toggle and the
    /// markers are settings rather than observations, and the drone reads the ground fresh
    /// on every visit anyway.
    /// </summary>
    public static class FarmAreaPicker
    {
        // Handed to the seeded placeholder when the dock owns no farm areas yet. Any
        // positive id is safe there precisely because no area exists to collide with.
        private const int PlaceholderEntryId = 1;

        private static readonly Color[] EntryColors =
        {
            Color.Green, Color.Yellow, Color.Cyan, Color.Orange, Color.Blue, Color.Red,
        };

        /// <summary>Opens the map editor on all of the dock's farm areas and applies whatever the player confirms.</summary>
        public static async Task ManageAreas(Player player, DroneDockObject dock, int maxPlots)
        {
            if (player == null || dock == null) return;

            var edited = await player.EditMap(BuildRequest(dock, maxPlots));

            // Cancelled, disconnected, or a malformed round-trip. MapEntries is checked as
            // well as Map because the reconcile treats "absent from MapEntries" as a
            // deletion -- without this guard a partial return would delete every farm area,
            // and the dock has no undo.
            if (edited?.Map == null || edited.MapEntries == null) return;

            Reconcile(player, dock, edited, maxPlots);
        }

        private static MapEditRequest BuildRequest(DroneDockObject dock, int maxPlots)
        {
            var map = new Array2D<int>(PlotUtil.WorldPlotDims);
            var mapEntries = new Dictionary<int, MapEntry>();
            var entryStatus = new Dictionary<int, EditableEntryStatus>();

            var index = 0;
            foreach (var area in dock.FarmAreas)
            {
                foreach (var plot in area.Plots())
                    map[new Vector2i(plot.X, plot.Z)] = area.Id;

                mapEntries[area.Id] = new MapEntry
                {
                    MapEntryId = area.Id,
                    Color = EntryColors[index++ % EntryColors.Length],

                    // The crop rides along in the description so a player editing geometry
                    // can still tell the fields apart. A field with no crop says so, since
                    // that is the state that silently does nothing (R26).
                    EntryDescription = area.Name,
                };

                // Per-entry status is what enables the rename field and delete button on
                // the client -- DefaultEntryStatus is consulted only for ids ABSENT here.
                entryStatus[area.Id] = new EditableEntryStatus
                {
                    AllowNameChange = true,
                    AllowDelete = true,
                    Readonly = false,
                    MaxArea = maxPlots,
                };
            }

            // A dock with no farm areas would otherwise open an editor with nothing to draw
            // into, and the map is the only creation path. Confirming with nothing drawn
            // creates nothing.
            if (mapEntries.Count == 0)
            {
                mapEntries[PlaceholderEntryId] = new MapEntry
                {
                    MapEntryId = PlaceholderEntryId,
                    Color = EntryColors[0],
                    EntryDescription = "Farm Area 1",
                };
                entryStatus[PlaceholderEntryId] = new EditableEntryStatus
                {
                    AllowNameChange = true,
                    AllowDelete = true,
                    Readonly = false,
                    MaxArea = maxPlots,
                };
            }

            return new MapEditRequest
            {
                MapHintTitle = "Farm Areas",
                MapHint = Localizer.DoStr(
                    "Draw the areas for the drone to farm. Add, rename, redraw or delete areas here, then confirm. Each area grows one crop, chosen on the Farming tab."),
                AllowNewEntries = true,
                AllowNameChange = true,
                Readonly = false,
                Overlay = new EditableOverlay
                {
                    Name = "Farm Areas",
                    Map = map,
                    MapEntries = mapEntries,
                },
                EntryStatus = entryStatus,
                DefaultEntryStatus = new EditableEntryStatus
                {
                    AllowNameChange = true,
                    AllowDelete = true,
                    Readonly = false,
                    MaxArea = maxPlots,
                },
            };
        }

        /// <summary>
        /// Applies the confirmed map: entries the player removed are deleted, ids the dock
        /// does not know are created, and known ids get their name refreshed and their
        /// plots replaced only when the geometry actually changed.
        /// </summary>
        private static void Reconcile(Player player, DroneDockObject dock, IMapEntryOverlay edited, int maxPlots)
        {
            var plotsById = PlotsByEntryId(edited);

            foreach (var area in dock.FarmAreas.ToList())
                if (!edited.MapEntries.ContainsKey(area.Id))
                    dock.DeleteFarmArea(area.Id);

            foreach (var pair in edited.MapEntries)
            {
                var entryId = pair.Key;
                var name = pair.Value.EntryDescription;
                var plots = plotsById.TryGetValue(entryId, out var p) ? p : new List<PlotCoord>();
                var area = dock.FarmArea(entryId);

                // The client's MaxArea is a hint, not a guarantee -- re-check server-side so
                // an over-cap area never reaches the drone.
                if (plots.Count > maxPlots)
                {
                    player.User?.MsgLocStr(
                        $"Farm area '{name}' is too large: {plots.Count} plots, limit {maxPlots}. That area was left unchanged.");
                    continue;
                }

                if (area == null)
                {
                    // An entry drawn with no plots is not an area; skip it silently so a
                    // confirmed-but-untouched placeholder creates nothing.
                    if (plots.Count == 0) continue;

                    if (dock.FarmAreas.Count >= FarmingComponent.MaxFarmAreas)
                    {
                        player.User?.MsgLocStr(
                            $"'{name}' was not created: a dock holds at most {FarmingComponent.MaxFarmAreas} farm areas. Delete one first.");
                        continue;
                    }

                    dock.CreateFarmArea(ResolveNewAreaName(dock, name), plots);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(name) && name != area.Name)
                    dock.RenameFarmArea(area.Id, name);

                // Replace plots only on a real geometry change. Unconditional replacement
                // would bump every area's epoch on every confirm and re-dispatch the drone
                // off areas the player never touched.
                if (plots.Count > 0 && !SamePlots(area.Plots(), plots))
                {
                    area.SetPlots(plots);
                    dock.OnFarmAreaEdited(area.Id);
                }
            }
        }

        /// <summary>
        /// The map editor is a shared civics surface, so an entry the player did not name
        /// comes back with the client's own default ("New District") -- meaningless on a
        /// drone dock. Numbered by what the dock already owns rather than by count, so
        /// deleting area 2 of 3 does not collide the next one with area 3.
        /// </summary>
        private static string ResolveNewAreaName(DroneDockObject dock, string returnedName)
        {
            if (!IsPlaceholderName(returnedName)) return returnedName;

            var taken = dock.FarmAreas.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var n = 1; ; n++)
            {
                var candidate = $"Farm Area {n}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        private static bool IsPlaceholderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;

            var trimmed = name.Trim();
            return trimmed.Equals("New District", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("New Entry", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("District", StringComparison.OrdinalIgnoreCase);
        }

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
