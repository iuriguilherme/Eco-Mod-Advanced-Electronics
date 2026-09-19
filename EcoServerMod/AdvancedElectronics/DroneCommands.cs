using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AdvancedElectronics.Navigation;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Services;
using Eco.Shared.SharedTypes;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Chat commands for the survey drone dock: diagnostics and the survey readout. Area
    /// assignment lives on the dock's Survey tab (the drawn-on-map area model, U4-U9,
    /// replaced the earlier named-district assignment); these commands read state and drive
    /// the same dock-owned areas for live testing.
    /// </summary>
    [ChatCommandHandler]
    public static class DroneCommands
    {
        [ChatCommand("Advanced Electronics drone commands.", ChatAuthorizationLevel.User)]
        public static void Drone(User user) { }

        /// <summary>
        /// Lists the nearest dock's linked storages, or toggles one on/off by its listed number.
        ///
        /// The Storage tab is meant to own this, and on a Store it does. The dock's panel has been
        /// rendering the target list without the Take From / Put Into controls, which left "link
        /// that stockpile yourself" impossible to actually do -- and that gap is what made
        /// auto-linking everything look like a fix instead of the workaround it was.
        ///
        /// This is the fallback, not the design: it guarantees manual control exists from the
        /// moment the vanilla default is restored, so testing is never blocked on a UI question.
        /// Delete it once the tab's own controls are confirmed working.
        /// </summary>
        [ChatSubCommand(nameof(Drone), "Lists linked storages, or toggles one by number: /drone link 3", "link", ChatAuthorizationLevel.User)]
        public static void Link(User user, int number = 0)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null)
            {
                user.MsgLocStr("No drone dock you have access to was found nearby.");
                return;
            }

            if (!dock.TryGetComponent<LinkComponent>(out var link))
            {
                user.MsgLocStr("That dock has no link component.", NotificationStyle.Error);
                return;
            }

            // Every reachable target, linked or not -- an unlinked one has to be listed or it could
            // never be turned on. GetLinkedStoragesWithSettings returns exactly that: the
            // authorized set, each with its current settings.
            var targets = link.GetLinkedStoragesWithSettings(user)
                              .Where(entry => entry.Storage?.Parent != null && entry.Storage.Parent is not DroneDockObject)
                              .ToList();

            if (targets.Count == 0)
            {
                user.MsgLocStr("No linkable storage in range of that dock.");
                return;
            }

            if (number < 1 || number > targets.Count)
            {
                user.MsgLocStr($"Linked storage for '{dock.Name}' -- /drone link <number> toggles one:");
                for (var i = 0; i < targets.Count; i++)
                {
                    var (storage, settings) = targets[i];
                    var state = settings.Output ? "LINKED" : "not linked";
                    user.MsgLocStr($"  {i + 1}. {storage.Parent.Name} -- {state}");
                }
                return;
            }

            var chosen = targets[number - 1];
            var turnOn = !chosen.Settings.Output;

            // Both directions together: the drone only ever pushes cargo out, but a target the
            // player has deliberately linked should behave like any other link, and leaving Input
            // untouched would make the tab disagree with this command.
            link.SetObjectInput(user, chosen.Storage, turnOn, userModified: true);
            link.SetObjectOutput(user, chosen.Storage, turnOn, userModified: true);

            user.MsgLocStr(
                $"{chosen.Storage.Parent.Name} is now {(turnOn ? "linked" : "unlinked")} for '{dock.Name}'.",
                NotificationStyle.Info);
        }

        /// <summary>
        /// Lists the dock's survey areas with their ids (diagnostic). Bridges the gap until the
        /// Survey Areas tab grows a per-area assign control (U7); pairs with <see cref="AssignArea"/>.
        /// </summary>
        [ChatSubCommand("Drone", "List the survey areas on your nearest accessible drone dock (diagnostic).", ChatAuthorizationLevel.User)]
        public static void Areas(User user)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            if (dock.SurveyAreas.Count == 0)
            {
                user.MsgLocStr($"{dock.Name}: no survey areas yet. Open the dock's Survey Areas tab and Create Area.");
                return;
            }

            user.MsgLocStr($"Survey areas on {dock.Name} (assigned id: {dock.AssignedSurveyAreaId}):");
            foreach (var a in dock.SurveyAreas)
                user.MsgLocStr($"  {a.Id}. {a.Name} -- {a.PlotCount} plots, for {KindWord(a.Kind)}{(a.Id == dock.AssignedSurveyAreaId ? " [assigned]" : string.Empty)}");
        }

        /// <summary>
        /// Assigns a survey area by id (0 clears) so the drone surveys it (U8). Temporary
        /// diagnostic: the real assign control is the Survey Areas tab once it has a safe
        /// targeting widget; this lets the area repoint be exercised live before then.
        /// </summary>
        [ChatSubCommand("Drone", "Assign a survey area by id to your nearest accessible dock. 0 clears. Usage: /drone assignarea <id>", ChatAuthorizationLevel.User)]
        public static void AssignArea(User user, int id)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            var assigned = dock.AssignSurveyArea(id, out var refusalReason, out var released);

            // R38's release rides here too. This command reaches the same state operation the tab
            // does, and a claim dropped through it is dropped just as silently otherwise.
            var release = MiningReadout.FormatClaimRelease(released, PlotUtil.PropertyPlotLength);

            if (id == 0)
                user.MsgLocStr(release.Length == 0
                    ? $"Cleared the survey area assignment on {dock.Name}."
                    : $"Cleared the survey area assignment on {dock.Name} -- {release}.");
            else if (assigned)
                user.MsgLocStr($"Assigned survey area {id} to {dock.Name}. The drone will head there.");
            else if (refusalReason != null)
                user.MsgLocStr($"Could not assign survey area {id} on {dock.Name} -- {refusalReason}.");
            else
                user.MsgLocStr($"No survey area with id {id} on {dock.Name}. Use /drone areas to list them.");
        }

        /// <summary>
        /// Reads or changes what a survey area is FOR (U11, R30, R31, R32).
        ///
        /// <para>
        /// <b>This command is the only way to invoke the change, and that is a decision rather
        /// than a gap.</b> An RPC on the Survey tab would render a fourth <c>BigButton</c> — ~3.2
        /// standard rows each, two-thirds of the width dead — on a tab already carrying three
        /// against a stated budget of one (KTD10). Repurposing an area is rare under R31: ground
        /// reaches <c>[empty]</c> once and is turned to farmland once. A command is the right home
        /// for an action of that shape, not a placeholder for a control that should exist.
        /// </para>
        /// <para>
        /// The refusal logic is <see cref="SurveyComponent.ChangeAreaKind"/>'s, not this method's:
        /// the gate belongs beside the areas, so a second caller cannot forget it. Everything the
        /// area recorded — findings, mined stamps, exclusions — survives a change (R32).
        /// </para>
        /// </summary>
        [ChatSubCommand("Drone", "Read or set what a survey area is for. Usage: /drone areakind <id> [mining|farming]", "areakind", ChatAuthorizationLevel.User)]
        public static void AreaPurpose(User user, int id, string kind = "")
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            var area = dock.SurveyAreas.FirstOrDefault(a => a.Id == id);
            if (area == null)
            {
                user.MsgLocStr($"No survey area with id {id} on {dock.Name}. Use /drone areas to list them.");
                return;
            }

            // No argument reads rather than writes, so a player can ask what an area is for
            // without risking changing it.
            if (string.IsNullOrWhiteSpace(kind))
            {
                user.MsgLocStr($"'{area.Name}' on {dock.Name} is for {KindWord(area.Kind)}. Change it with /drone areakind {id} mining|farming.");
                return;
            }

            if (!TryParseKind(kind, out var wanted))
            {
                user.MsgLocStr($"'{kind}' is not a kind of area. Use mining or farming.", NotificationStyle.Error);
                return;
            }

            if (area.Kind == wanted)
            {
                user.MsgLocStr($"'{area.Name}' is already for {KindWord(wanted)}. Nothing changed.");
                return;
            }

            if (!SurveyComponent.ChangeAreaKind(dock, area, wanted, user, out var refusalReason))
            {
                user.MsgLocStr($"Cannot change '{area.Name}' yet: {refusalReason}.", NotificationStyle.Error);
                return;
            }

            // Naming what survived is the point of saying anything at all: R31's whole promise is
            // that repurposing exhausted ground costs nothing it recorded.
            user.MsgLocStr(
                $"'{area.Name}' on {dock.Name} is now for {KindWord(wanted)}. Its survey findings, mined record and exclusions are untouched.",
                NotificationStyle.Info);
        }

        /// <summary>The player-facing word for a kind. Not the roster tag — that is the status slot's (R30).</summary>
        private static string KindWord(AreaKind kind) => kind == AreaKind.Farming ? "farming" : "mining";

        /// <summary>
        /// Parses the kind argument. Deliberately not <c>Enum.TryParse</c>: that would silently
        /// accept "0" and "1" as kinds, so a mistyped area id in the kind slot would repurpose an
        /// area instead of being refused.
        /// </summary>
        private static bool TryParseKind(string value, out AreaKind kind)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "mining":
                case "mine":
                    kind = AreaKind.Mining;
                    return true;
                case "farming":
                case "farm":
                    kind = AreaKind.Farming;
                    return true;
                default:
                    kind = AreaKind.Mining;
                    return false;
            }
        }

        /// <summary>
        /// The survey readout itself (R14): what the drone has actually found, in the
        /// terms a player acts on -- which ore, where, how concentrated, and how deep.
        ///
        /// Exists because a survey the player cannot read is a survey that did not
        /// happen. The dock's tooltip carries the same content, but this is a channel
        /// that is certain to render and does not require standing at the dock, so the
        /// data is never stranded server-side again.
        /// </summary>
        [ChatSubCommand("Drone", "Read the survey results from your nearest accessible drone dock.", ChatAuthorizationLevel.User)]
        public static void Survey(User user)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null)
            {
                user.MsgLocStr("No drone dock you have access to was found nearby.");
                return;
            }

            var area = dock.AssignedSurveyArea;
            user.MsgLocStr($"Survey results for {dock.Name}"
                + (area == null ? " (no area assigned)" : $" -- area '{area.Name}'"));

            if (area == null)
            {
                user.MsgLocStr("  Assign an area to the drone so it surveys one.");
                return;
            }

            // Findings persist with the area (KTD11): read them straight off the area, shown
            // whether or not a drone is currently out. A missing drone only means the data will
            // not grow, not that it disappears.
            var findings = area.ReadFindings()
                .Where(f => f.Found && dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .ToList();

            if (findings.Count == 0)
            {
                user.MsgLocStr(dock.MaterialFilter.Count > 0
                    ? "  Nothing matches the current material filter. Use /drone filter to clear it."
                    : "  Nothing found yet. The drone reports as it roams -- give it time to cover ground.");
                return;
            }

            foreach (var f in findings)
                user.MsgLocStr($"  {DockReadout.FormatOreLine(f)}");

            user.MsgLocStr($"  Coverage: {area.CoveragePercent:F0}%");
            if (area.SurveyDepth > 0)
                user.MsgLocStr($"  Scanned to {area.SurveyDepth} blocks below surface; median surface level {area.MedianSurface}.");
        }

        /// <summary>
        /// Lists the discovered materials with their filter state, or toggles one by name. With no
        /// argument and a filter set, clears it. The display-time filter narrows what the survey
        /// readout shows; nothing is ever un-recorded.
        /// </summary>
        [ChatSubCommand("Drone", "List or toggle survey material filters. No name clears the filter. Usage: /drone filter [material]", ChatAuthorizationLevel.User)]
        public static void Filter(User user, string material = "")
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            var known = dock.KnownMaterials;

            if (!string.IsNullOrWhiteSpace(material))
            {
                // Case-insensitive match against what has actually been found.
                var match = known.FirstOrDefault(m => m.Equals(material, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    user.MsgLocStr($"No surveyed material named '{material}'. Known: {(known.Count == 0 ? "(none yet)" : string.Join(", ", known))}");
                    return;
                }

                dock.ToggleMaterialFilter(match);
                user.MsgLocStr($"{match} is now {(dock.IsMaterialShown(match) ? "shown" : "hidden")} in the survey readout.");
            }
            else if (dock.MaterialFilter.Count > 0)
            {
                dock.ClearMaterialFilter();
                user.MsgLocStr($"Material filter cleared on {dock.Name}; showing all materials.");
            }

            if (known.Count == 0)
            {
                user.MsgLocStr("No materials found yet -- they appear here as the drone finds them.");
                return;
            }

            user.MsgLocStr(dock.MaterialFilter.Count == 0
                ? $"Showing all {known.Count} materials:"
                : $"Showing {dock.MaterialFilter.Count} of {known.Count} materials:");
            foreach (var m in known)
                user.MsgLocStr($"  {(dock.IsMaterialShown(m) ? "[x]" : "[ ]")} {m}");
        }

        /// <summary>
        /// Reads or sets one crop's harvest ceiling on the nearest dock (R25). The Crop Ceilings
        /// tab has a row for every vanilla crop, but its rows are fixed when the mod is built,
        /// so a crop another mod adds has none; this is its way in, and the tab names it when
        /// such a crop exists. Works for every crop, row or not. Matches the crop's display
        /// name or its species name, ignoring case and spaces. Zero removes the ceiling.
        /// </summary>
        [ChatSubCommand("Drone", "Read or set a crop's harvest ceiling. 0 removes it. Usage: /drone ceiling <crop>, [amount]", "ceiling", ChatAuthorizationLevel.User)]
        public static void Ceiling(User user, string crop, int amount = -1)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            // Only names that pick out one crop are accepted. Matching the produce name alone
            // would let "Plant Fibers" land on whichever of the fiber plants sorts first.
            static string Squash(string s) => (s ?? string.Empty).Replace(" ", string.Empty);
            var wanted = Squash(crop);
            var match = CropCatalog.All.FirstOrDefault(c =>
                Squash(c.UniqueName).Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || c.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                user.MsgLocStr($"No crop named '{crop}'. Crops: {string.Join(", ", CropCatalog.All.Select(c => c.UniqueName))}");
                return;
            }

            if (amount >= 0 && !dock.SetCropCeiling(match.Key, amount, user))
            {
                user.MsgLocStr("You need full access on this drone dock to set its ceilings.");
                return;
            }

            var ceiling = dock.CropCeilingFor(match.Key);
            user.MsgLocStr(ceiling == 0
                ? $"{match.UniqueName} has no ceiling on {dock.Name} and is harvested without limit."
                : $"{match.UniqueName} stops being harvested on {dock.Name} at {ceiling} in linked storage.");
        }

        /// <summary>
        /// Farming diagnostic (read-only). Written after a live pass where every assigned farm
        /// area read "nothing to do here" with seed in linked storage. That readout is the
        /// default, and it is what the tab shows whether the strategy never scanned or it
        /// scanned and judged every block unworkable. This names which, in one command: the
        /// drone's job and last dispatch note, the stamp the scan is gated on, and the very
        /// per-column decision the strategy makes, run over each assigned area.
        /// </summary>
        [ChatSubCommand("Drone", "Dump farming state for your nearest accessible dock (diagnostic).", "farm", ChatAuthorizationLevel.User)]
        public static void Farm(User user)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            var drone = dock.SpawnedDrone;
            var job = drone is IDroneToolbearer bearer ? bearer.Job.ToString() : "(no drone or no job)";
            var note = drone == null || drone.IsDestroyed ? "(no drone)" : drone.GetComponent<DroneLifecycle>()?.LastDispatchNote ?? "(no lifecycle)";
            user.MsgLocStr($"Dock '{dock.Name}': drone job {job}, last dispatch: {note}");

            var stamped = dock.StampedCitizen;
            user.MsgLocStr($"  Stamp: {(stamped?.Name ?? "(none)")} (id {dock.StampedCitizenId}), full access: {(stamped != null && dock.HasFullAccess(stamped))}, stamp valid: {dock.FarmStampIsValid()}");

            // "hold full -- returning to unload" means cargo is aboard that the last unload
            // could not place; this names it and says where it could have gone.
            var farmHold = (dock.GetComponent(typeof(PublicStorageComponent), DroneCargo.HoldName) as PublicStorageComponent)?.Storage;
            var holdText = farmHold == null
                ? "MISSING"
                : farmHold.IsEmpty ? "empty" : string.Join(", ", farmHold.NonEmptyStacks.Select(s => $"{s.Quantity} {s.Item.DisplayName}"));
            var destinations = dock.TryGetComponent<LinkComponent>(out var farmLink) && stamped != null
                ? farmLink.GetSortedLinkedEnabledStorages(stamped).Count(s => s.Parent is not DroneDockObject)
                : 0;
            user.MsgLocStr($"  Hold: {holdText}; linked storages it can unload into: {destinations}");

            var areas = dock.FarmAreas.ToList();
            user.MsgLocStr($"  Farm areas: {areas.Count}, assigned: {areas.Count(a => a.Assigned)}");

            var sampler = new EcoWorldSampler();
            var fitness = new EcoGroundFitness();
            var ledger = dock.ReadCropCeilings();

            foreach (var area in areas)
            {
                var crop = CropCatalog.ByKey(area.Crop);
                var stored = string.IsNullOrEmpty(area.Crop) ? 0 : dock.CountInLinkedStorage(area.Crop);
                user.MsgLocStr($"  Area {area.Id} '{area.Name}': assigned {area.Assigned}, crop key '{area.Crop ?? "(none)"}', catalog {(crop == null ? "NOT FOUND" : crop.UniqueName)}, seed {crop?.SeedType?.Name ?? "(none)"}, produce stored {stored}, may harvest {(string.IsNullOrEmpty(area.Crop) || ledger.MayHarvest(area.Crop, stored))}, stall {area.LastStallReason}, next {area.LastNextAction}");

                var plots = area.ToArea().EnumeratePlots().ToList();

                // Where the drone aims for each plot: the pathfinder refuses a solid or
                // occupied goal column, which is how a farm plot reads "unreachable".
                foreach (var plot in plots.Take(4))
                {
                    var cx = plot.X * PlotUtil.PropertyPlotLength + PlotUtil.PropertyPlotLength / 2;
                    var cz = plot.Z * PlotUtil.PropertyPlotLength + PlotUtil.PropertyPlotLength / 2;
                    var open = PlotApproach.FirstOpenColumn(plot, PlotUtil.PropertyPlotLength,
                        (x, z) => sampler.IsSolidAt(x, z) || sampler.IsObstacleAt(x, z));
                    user.MsgLocStr($"    plot {plot.X},{plot.Z}: centre ({cx},{cz}) solid {sampler.IsSolidAt(cx, cz)}, occupied {sampler.IsObstacleAt(cx, cz)}; aims at {(open.HasValue ? $"({open.Value.X},{open.Value.Z})" : "NOTHING OPEN")}");
                }

                var tally = new Dictionary<string, int>();
                var samples = 0;
                foreach (var plot in plots.Take(16))
                foreach (var column in FarmingStrategy.ColumnsIn(plot))
                {
                    var outcome = FarmingStrategy.EvaluateColumn(sampler, fitness, area, column, ledger, stored);
                    var key = outcome.WasRefusedForFitness ? $"{outcome.Action} (unfit: {outcome.UnfitCondition})" : outcome.Action.ToString();
                    tally[key] = tally.TryGetValue(key, out var n) ? n + 1 : 1;

                    if (samples++ >= 3) continue;
                    var y = outcome.SurfaceY;
                    var surface = Eco.World.World.GetBlock(new Eco.Shared.Math.Vector3i(column.X, y, column.Z));
                    var above = Eco.World.World.GetBlock(new Eco.Shared.Math.Vector3i(column.X, y + 1, column.Z));
                    var rating = string.IsNullOrEmpty(area.Crop) ? "-" : fitness.Rate(area.Crop, column.X, y + 1, column.Z).Rating.ToString("F2");
                    user.MsgLocStr($"    ({column.X},{y},{column.Z}) surface {surface?.GetType().Name ?? "null"}, above {above?.GetType().Name ?? "null"}, fitness {rating} -> {outcome.Action}");
                }

                user.MsgLocStr($"    {plots.Count} plots; decisions over the first {Math.Min(plots.Count, 16)}: {string.Join(", ", tally.Select(kv => $"{kv.Key} x{kv.Value}"))}");
            }
        }

        /// <summary>
        /// Dumps the ITEM TAGS of every material the drone has actually found. Diagnostic: the
        /// material pickers scope their candidate list by a single item tag each, and which tag a
        /// given material carries is not reliably inferable from the game source (block tags and item
        /// tags differ -- "Minable" is a block tag, so an item picker scoped to it is empty). This
        /// reports the ground truth from the live server so picker scoping is evidence-based instead
        /// of guessed, in one pass rather than a restart per guess.
        /// </summary>
        [ChatSubCommand("Drone", "Dump the item tags of every surveyed material (diagnostic).", ChatAuthorizationLevel.User)]
        public static void Tags(User user)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null) { user.MsgLocStr("No drone dock you have access to was found nearby."); return; }

            var materials = dock.KnownMaterials;
            user.MsgLocStr(materials.Count == 0
                ? "No materials surveyed yet (tag diagnostics below still apply)."
                : $"Item tags for {materials.Count} surveyed materials on {dock.Name}:");

            // Candidate tags a material picker could be scoped by. Inverted lookup (tag -> its types,
            // then match our material name) because it uses only TagManager.TagToTypes/Tag, the same
            // pair vanilla code uses, rather than guessing at a type-to-tags accessor.
            var candidateTags = new[] { BlockTags.Excavatable, "Rock", "Ore", "Diggable", "Minable", "MinableRubble", "Fuel", "Metal", "Block" };

            foreach (var material in materials)
            {
                var hits = new List<string>();
                foreach (var tag in candidateTags)
                {
                    try
                    {
                        var types = TagManager.TagToTypes[TagManager.Tag(tag)];
                        if (types != null && types.Any(t => IsItemTypeFor(t.Name, material)))
                            hits.Add(tag);
                    }
                    catch { /* unknown tag on this build -- just report it as absent */ }
                }

                // The picker is scoped to Excavatable, so a material without it cannot be selected.
                var pickable = hits.Contains(BlockTags.Excavatable) ? string.Empty : "  <- NOT PICKABLE";
                user.MsgLocStr($"  {material}: {(hits.Count == 0 ? "(no tags)" : string.Join(", ", hits))}{pickable}");
            }
        }

        /// <summary>True when item type <paramref name="typeName"/> is the item for material <paramref name="material"/>.</summary>
        private static bool IsItemTypeFor(string typeName, string material) =>
            typeName.Equals(material + "Item", StringComparison.OrdinalIgnoreCase) ||
            typeName.Equals(material + "BlockItem", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Dumps the complete server-side state of the nearest accessible dock and its
        /// drone. Diagnostic surface so ONE live session yields full information about
        /// the pairing/dispatch/survey pipeline without depending on any client UI
        /// rendering (world text, tooltip, window) and without repeated
        /// restart-observe cycles — every layer reports its own truth in chat.
        /// </summary>
        /// <summary>
        /// TEMPORARY DIAGNOSTIC. Delete alongside
        /// <see cref="DroneLifecycle.AnnounceAnimationStateChanges"/> once the animation
        /// contract is settled.
        /// </summary>
        [ChatSubCommand("Drone", "Toggle live chat announcements of drone animation state changes (diagnostic).", ChatAuthorizationLevel.User)]
        public static void AnimWatch(User user)
        {
            DroneLifecycle.AnnounceAnimationStateChanges = !DroneLifecycle.AnnounceAnimationStateChanges;
            user.MsgLocStr(DroneLifecycle.AnnounceAnimationStateChanges
                ? "Animation state watch ON. Every change to a drone's animation booleans will be announced, to everyone online, until this is toggled off."
                : "Animation state watch OFF.");
        }

        [ChatSubCommand("Drone", "Dump full drone/dock state for your nearest accessible dock (diagnostic).", ChatAuthorizationLevel.User)]
        public static void Status(User user)
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null)
            {
                user.MsgLocStr("No drone dock you have access to was found nearby.");
                return;
            }

            user.MsgLocStr($"Dock '{dock.Name}' at {dock.Position3i}:");
            user.MsgLocStr($"  Survey areas: {dock.SurveyAreas.Count}, assigned area: {(dock.AssignedSurveyArea?.Name ?? "(none)")} (id {dock.AssignedSurveyAreaId})");
            user.MsgLocStr($"  Paired drone item: {(dock.HasDrone ? "yes" : "no")}");

            // Everything from here to the anim states was added after the first live pass, where a
            // mining drone sat parked and the readout could not say why. DroneLifecycle.Tick returns
            // at the serviceability gate BEFORE it ever reaches dispatch, so a stopped dock leaves
            // LastDispatchNote reading "no dispatch yet" -- indistinguishable from an assignment that
            // never landed. These lines separate the candidates, so one command names the cause
            // instead of costing a restart per guess.
            user.MsgLocStr($"  Serviceable: {dock.IsServiceable} (stop reason: {dock.StopReason})");

            if (dock.TryGetComponent<FuelSupplyComponent>(out var fuel))
                // Enabled is `energy > 0` -- the live burned charge, NOT the tank contents. A full
                // tank that never loaded a unit reads Enabled=false with EnergyInSupply>0, which is
                // a different fault from a genuinely empty tank (both read "out of fuel" in the UI).
                user.MsgLocStr($"  Fuel: enabled={fuel.Enabled}, burning={fuel.CurrentFuel?.DisplayName.ToString() ?? "(none)"}, energy={fuel.Energy:F0}, inSupply={fuel.EnergyInSupply:F0}");
            else
                user.MsgLocStr("  Fuel: no FuelSupplyComponent installed (no drone slotted?)");

            // The mining assignment is a separate token from the survey one above, and
            // BuildStrategy returns null -- no dispatch, no movement -- if the mining area, the
            // named cargo hold, or the link component is missing. All three are reported.
            user.MsgLocStr($"  Mining assignment: {dock.AssignedMiningAreaToken ?? "(none)"}");
            var hold = dock.GetComponent(typeof(PublicStorageComponent), DroneCargo.HoldName);
            // Contents, not just presence. A finished job holds its assignment until the hold is
            // empty -- deliberately, since unassigning with cargo aboard strands it -- so "still
            // assigned after completing" and "cannot empty the hold" are the same observation from
            // two ends, and only this line tells them apart.
            var holdInventory = (hold as PublicStorageComponent)?.Storage;
            var holdSummary = holdInventory == null
                ? "MISSING (blocks mining dispatch)"
                : holdInventory.IsEmpty
                    ? "present, empty"
                    : $"present, holding {holdInventory.GroupedStacks.Sum(s => s.Quantity)} items";

            user.MsgLocStr($"  Mining hold '{DroneCargo.HoldName}': {holdSummary}");
            // The TYPE, not just presence. A dock saved before the requirement changed still
            // carries the old component, and it renders "(SHARED)" exactly like the stock one --
            // so the header cannot tell them apart and neither can a screenshot. Two rounds of
            // this were spent testing a change on an object that did not have it.
            var links = dock.GetComponents<LinkComponent>().Select(c => c.GetType().Name).ToList();
            user.MsgLocStr(links.Count == 0
                ? "  Link component: MISSING (blocks mining dispatch)"
                : $"  Link component: {string.Join(" + ", links)}");
            if (dock.MiningJob is { } miningJob)
            {
                user.MsgLocStr($"  Mining job: {miningJob.Status}, worked {miningJob.WorkedCount}, skipped {miningJob.SkippedCount}{(miningJob.EndReason.HasValue ? $", ended: {miningJob.EndReason}" : string.Empty)}");

                // This is now the ONLY place these are shown -- the Mining tab dropped its row per
                // fact, which had made a player-facing panel into a debugging surface. "Obstructed"
                // is the removal service's catch-all for everything that was neither law nor
                // property, so the category names the bucket and the refusal text names the cause.
                //
                // Through MiningReadout rather than hand-rolled here, so the wording has one
                // definition now that the panel is no longer the other caller.
                var skipLine = MiningReadout.FormatSkipLine(miningJob.SkipCountsByCategory(), miningJob.SkippedCount);
                if (!string.IsNullOrWhiteSpace(skipLine))
                    user.MsgLocStr($"  Skips: {skipLine}");

                var refusal = MiningReadout.FormatRefusalDetail(miningJob.LastRefusalDetail);
                if (!string.IsNullOrWhiteSpace(refusal))
                    user.MsgLocStr($"  {refusal}");

                var shaft = MiningReadout.FormatShaftProgress(
                    miningJob.ShaftLayersDone, miningJob.ShaftLayersTotal,
                    miningJob.TargetSurveyedStamp, miningJob.TargetMinedStamp);
                if (!string.IsNullOrWhiteSpace(shaft))
                    user.MsgLocStr($"  {shaft}");
            }
            else
                user.MsgLocStr("  Mining job: (none)");

            // R27: the refusal reason has to outlive the job that hit it, or an area that reads
            // `[cleared]` because one plot was refused looks the same as one that is genuinely
            // spent. Printed beside the skip rows above -- the place this mod already answers
            // "why was that plot skipped" -- rather than on the roster line, which stays at its
            // budgeted length, or on a new panel row, which KTD10 rules out.
            if (dock.AssignedMiningArea is { } exclusionAreaRef
                && exclusionAreaRef.Resolve(out _, out var exclusionArea) == AreaLookupSignal.Found)
            {
                var ledger = dock.ReadMiningExclusions(exclusionAreaRef.OwningDockId, exclusionArea);
                var exclusionLine = MiningReadout.FormatExclusionLine(ledger.AttemptFacts);
                if (!string.IsNullOrWhiteSpace(exclusionLine))
                    user.MsgLocStr($"  {exclusionLine}");
            }

            // R36: the panel says an overlap EXISTS; this says exactly where. The centre block of
            // every shared plot, so the player can fly there and look, and whether the other area
            // holds those plots right now -- the one thing that answers "will this block ever
            // lift", which is the same obligation R27 puts on a blocked area.
            //
            // It names nothing else about the other area, and cannot: the overlap path only ever
            // received that area's geometry, its identity and its claim (R41, R42). Not its name,
            // not its owner, and deliberately not what it is FOR -- R39's claim test is
            // kind-blind, so the kind would disclose something about another player's ground
            // while answering nothing about the player's own block.
            //
            // Here rather than on a roster line or a new panel row: this is where the mod already
            // answers "why was that plot skipped", the roster stays at its budgeted length, and
            // KTD10 adds no control to either tab.
            ReportOverlaps(user, dock);

            user.MsgLocStr($"  Mining halted server-wide: {MiningHalt.IsHalted}");
            user.MsgLocStr($"  Anim state Working: {FormatAnimState(dock, DroneDockObject.WorkingStateName)}");

            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)
            {
                user.MsgLocStr("  Spawned drone: none (insert a Survey Drone to spawn one).");
                return;
            }

            var owner = drone is IDroneOwnable ownable && ownable.HasOwner ? ownable.OwnerName : "unstamped";
            user.MsgLocStr($"  Spawned drone at {drone.Position3i} (owner: {owner})");
            user.MsgLocStr($"  Anim state MoveSpeed: {FormatAnimState(drone, DroneMoverComponent.MoveSpeedStateName)}");

            // Every animation boolean, read back from the object's own synced dictionary rather
            // than recomputed. This is what the client actually receives, so a state reading
            // "not yet pushed" here means the animator was never going to see it -- which is a
            // different bug from a state that arrives holding the wrong value.
            var tool = drone is IDroneToolbearer bearer ? bearer.Tool.ToString() : "NO IDroneToolbearer";
            user.MsgLocStr($"  Declared tool: {tool}");
            foreach (var name in new[]
                     {
                         DroneAnimationStateNames.IsAtHomeDock,
                         DroneAnimationStateNames.IsWorking,
                         DroneAnimationStateNames.ModeMining,
                         DroneAnimationStateNames.ModeHarvest,
                     })
                user.MsgLocStr($"  Anim state {name}: {FormatAnimState(drone, name)}");

            if (drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
            {
                user.MsgLocStr($"  Lifecycle: {lifecycle.Status}, sampling={(lifecycle.ShouldSample ? "yes" : "no")}, homeDock={(lifecycle.HomeDock != null ? "set" : "NOT SET (dispatch wiring gap)")}");
                user.MsgLocStr($"  Last dispatch: {lifecycle.LastDispatchNote}");
            }
            else
                user.MsgLocStr("  Lifecycle: component MISSING");

            if (drone.TryGetComponent<DroneMoverComponent>(out var mover))
                user.MsgLocStr($"  Mover: {(mover.IsMoving ? "moving" : "stationary")}");
            else
                user.MsgLocStr("  Mover: component MISSING");

            user.MsgLocStr($"  Sensor: {(drone.TryGetComponent<OreSensorComponent>(out var sensor) ? $"present, survey depth {sensor.SurveyReach} blocks" : "component MISSING")}");

            // Findings live on the assigned area now (KTD11), not the sensor -- report that area's
            // persisted snapshot.
            var area = dock.AssignedSurveyArea;
            if (area == null)
                user.MsgLocStr("  Findings: (no area assigned)");
            else
            {
                var findings = area.ReadFindings().Where(f => f.Found).OrderByDescending(f => f.Count).ToList();
                if (findings.Count == 0)
                    user.MsgLocStr($"  Findings for '{area.Name}': none yet (coverage {area.CoveragePercent:F0}%).");
                else
                {
                    user.MsgLocStr($"  Findings for '{area.Name}' (coverage {area.CoveragePercent:F0}%):");
                    foreach (var f in findings)
                        user.MsgLocStr($"    {DockReadout.FormatOreLine(f)}");
                }
                if (area.SurveyDepth > 0)
                    user.MsgLocStr($"    Scanned to {area.SurveyDepth} blocks below surface; median surface level {area.MedianSurface}.");
            }
        }

        /// <summary>
        /// The per-plot overlap detail R36 sends here rather than to the panel: one line per
        /// collision, per area this dock is working with.
        ///
        /// <para>
        /// The areas reported are the dock's OWN published ones plus the mining area it is
        /// assigned to -- the two the player operating this dock is already entitled to see, so
        /// naming them discloses nothing new. The other side of every collision is named only as
        /// ground and a claim, which is all the projection carries (R41).
        /// </para>
        /// <para>
        /// The projections are collected ONCE and reused across every area, rather than per area:
        /// this command can be run against a dock holding many areas, and a world walk apiece
        /// would be an O(areas x world objects) sweep for one printout.
        /// </para>
        /// </summary>
        private static void ReportOverlaps(User user, DroneDockObject dock)
        {
            var published = MiningComponent.AllAreaProjections();
            var reported = new HashSet<(Guid Dock, int Area)>();
            var headerShown = false;

            void Report(DroneDockObject owner, SurveyAreaEntry area)
            {
                // An assigned area this dock also owns would otherwise print twice.
                if (owner == null || area == null || !reported.Add((owner.ObjectID, area.Id))) return;

                var lines = AreaOverlap.FormatOverlapDetail(
                    MiningComponent.OverlapsOf(owner, area, published), PlotUtil.PropertyPlotLength);
                if (lines.Count == 0) return;

                if (!headerShown)
                {
                    user.MsgLocStr("  Overlaps:");
                    headerShown = true;
                }

                foreach (var line in lines)
                    user.MsgLocStr($"    '{area.Name}': {line}");
            }

            foreach (var own in dock.SurveyAreas)
                Report(dock, own);

            if (dock.AssignedMiningArea is { } assigned
                && assigned.Resolve(out var owningDock, out var minedArea) == AreaLookupSignal.Found)
                Report(owningDock, minedArea);

            if (!headerShown)
                user.MsgLocStr("  Overlaps: none");
        }

        /// <summary>
        /// Reads a pushed animation-state value for the diagnostic readback (v1 closure
        /// plan R3). States are pushed on change, so a just-placed object may not carry
        /// the key yet — TryGetValue instead of the throwing indexer, reporting
        /// "not yet pushed" for that normal transient case.
        /// </summary>
        private static string FormatAnimState(WorldObject obj, string name) =>
            obj.AnimatedStates.TryGetValue(name, out var value) ? (value?.ToString() ?? "null") : "not yet pushed";

        /// <summary>
        /// Nearest-owned-or-authorized-dock lookup for the chat commands. A chat command has
        /// no client raycast or "targeted WorldObject" to read, so the target is the nearest
        /// DroneDockObject the invoking player has full access to
        /// (<c>WorldObject.IsAuthorized(user, AccessType.FullAccess)</c>) -- proximity plus
        /// per-object authorization, so standing near someone else's dock can't redirect it.
        /// </summary>
        private static DroneDockObject FindNearestAuthorizedDock(User user)
        {
            DroneDockObject nearest = null;
            var nearestDistSq = float.MaxValue;

            foreach (var obj in ServiceHolder<IWorldObjectManager>.Obj.All)
            {
                if (!(obj is DroneDockObject dock)) continue;
                if (!dock.IsAuthorized(user, AccessType.FullAccess)) continue;

                var distSq = Vector3.DistanceSquared(user.Position, dock.Position);
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = dock;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Cross-checks every survey drone in the world against every dock's recorded claim, then
        /// optionally destroys the ones nothing claims.
        ///
        /// Exists because a drone was found orphaned and three plausible explanations were each
        /// falsified by reading code: DespawnDrone is reached on dock pickup (removing the item fires
        /// first), the claim id is [Serialized], and WorldObject.ObjectID persists. Rather than guess
        /// a fourth time, this reports the actual linkage — which dock claims which id, and whether
        /// that id resolves — so the next orphan is diagnosed from evidence instead of inference.
        ///
        /// Admin-gated because 'destroy' removes world objects permanently.
        /// </summary>
        [ChatSubCommand("Drone", "Cross-check survey drones against dock claims. Pass 'destroy' to remove unclaimed ones. Usage: /drone orphans [destroy]", ChatAuthorizationLevel.Admin)]
        public static void Orphans(User user, string action = "")
        {
            var all    = ServiceHolder<IWorldObjectManager>.Obj.All.ToList();
            var drones = all.OfType<SurveyDroneObject>().Where(d => !d.IsDestroyed).ToList();
            var docks  = all.OfType<DroneDockObject>().Where(d => !d.IsDestroyed).ToList();

            user.MsgLocStr($"Survey drones in world: {drones.Count}. Drone docks: {docks.Count}.");

            // What each dock believes it owns. Reported even when the id resolves to nothing, because
            // a dangling claim and a missing claim are different bugs.
            var claims = new Dictionary<Guid, DroneDockObject>();
            foreach (var dock in docks)
            {
                var id = dock.ClaimedDroneObjectId;
                var live = dock.SpawnedDrone != null && !dock.SpawnedDrone.IsDestroyed;
                user.MsgLocStr(
                    $"  dock at {dock.Position3i}: claims {(id == Guid.Empty ? "nothing" : id.ToString())}, "
                    + $"live reference {(live ? "yes" : "no")}");

                if (id != Guid.Empty) claims[id] = dock;
            }

            var orphans = new List<SurveyDroneObject>();
            foreach (var drone in drones)
            {
                var claimed = claims.TryGetValue(drone.ObjectID, out var owner);
                user.MsgLocStr(
                    $"  drone {drone.ObjectID} at {drone.Position3i}: "
                    + (claimed ? $"claimed by dock at {owner.Position3i}" : "ORPHAN -- no dock claims it"));

                if (!claimed) orphans.Add(drone);
            }

            if (orphans.Count == 0) { user.MsgLocStr("No orphans."); return; }

            if (!string.Equals(action, "destroy", StringComparison.OrdinalIgnoreCase))
            {
                user.MsgLocStr($"{orphans.Count} orphan(s). Run '/drone orphans destroy' to remove them.");
                return;
            }

            foreach (var drone in orphans)
                WorldObjectManager.DestroyPermanently(drone);

            user.MsgLocStr($"Destroyed {orphans.Count} orphaned drone(s).");
        }
    }
}
