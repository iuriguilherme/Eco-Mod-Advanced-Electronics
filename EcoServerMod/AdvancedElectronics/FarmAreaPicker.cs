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
    ///
    /// <para>
    /// <b>One collection, two views (U10, KTD7).</b> Since U3 a farm is an ordinary
    /// <see cref="SurveyAreaEntry"/> in <c>DroneDockObject.SurveyAreas</c> carrying
    /// <see cref="AreaKind.Farming"/>, and this picker is the FARMING view of that one
    /// collection: it lists, creates, renames, redraws and deletes farming-kind areas only, and
    /// an area of any other kind is invisible here exactly as a farm is invisible to the survey
    /// picker. That filter is not cosmetic — this picker's reconcile treats "absent from the
    /// returned entries" as a deletion, so an unfiltered list would let confirming the farm map
    /// delete every mining area the dock owns.
    /// </para>
    /// <para>
    /// <b>The cap is the dock's, not the tab's (R19).</b> Both pickers count the WHOLE
    /// collection against <see cref="AreaCapacity.MaxAreasPerDock"/>, so ten is ten whichever
    /// tab a player is standing on. A dock the fold left above the limit keeps every area and
    /// may add none until deletions bring it back under.
    /// </para>
    /// <para>
    /// <b>Seam.</b> Eco-coupled UI with no unit test: this drives the client's map editor and
    /// mutates the dock's serialized state, and the test project references the navigation
    /// assembly alone. The one decidable part — the cap arithmetic — is
    /// <see cref="AreaCapacity"/>, unit-tested there. The rest is proven in U12's live session.
    /// </para>
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
            foreach (var area in dock.FarmingAreas)
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

            // Kind-filtered, and that is load-bearing rather than tidy: an id absent from the
            // returned entries is treated as a deletion, and this list is now a view onto the
            // dock's ONE area collection (U10). Walking it unfiltered would delete every mining
            // and survey area the dock owns the first time a player confirmed the farm map.
            foreach (var area in dock.FarmingAreas.ToList())
                if (!edited.MapEntries.ContainsKey(area.Id))
                    dock.DeleteFarmArea(area.Id);

            foreach (var pair in edited.MapEntries)
            {
                var entryId = pair.Key;
                var name = pair.Value.EntryDescription;
                var plots = plotsById.TryGetValue(entryId, out var p) ? p : new List<PlotCoord>();
                var area = dock.FarmingArea(entryId);

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

                    // Counted over the WHOLE collection, not over the farms alone (R19, KTD7):
                    // one dock, one limit, whichever tab the player is drawing from.
                    if (!AreaCapacity.MayAdd(dock.SurveyAreas.Count))
                    {
                        player.User?.MsgLocStr(
                            $"'{name}' was not created: a dock holds at most {AreaCapacity.MaxAreasPerDock} areas. Delete one first.");
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

            // Still scoped to the FARMS, not widened to the whole collection (U10). The two
            // tabs share one collection now, but this check only picks the next free
            // "Farm Area N" for an entry the player did not name, and the survey side's
            // placeholders are "Survey Area N" -- so the sets it would newly see cannot
            // collide with anything it mints. Widening it would change which number a new farm
            // gets for no defect it fixes.
            var taken = dock.FarmingAreas.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
