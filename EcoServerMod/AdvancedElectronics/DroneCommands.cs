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
using Eco.Shared.SharedTypes;

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
                user.MsgLocStr($"  {a.Id}. {a.Name} -- {a.PlotCount} plots{(a.Id == dock.AssignedSurveyAreaId ? " [assigned]" : string.Empty)}");
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

            dock.AssignSurveyArea(id);
            if (id == 0)
                user.MsgLocStr($"Cleared the survey area assignment on {dock.Name}.");
            else if (dock.AssignedSurveyAreaId == id)
                user.MsgLocStr($"Assigned survey area {id} to {dock.Name}. The drone will head there.");
            else
                user.MsgLocStr($"No survey area with id {id} on {dock.Name}. Use /drone areas to list them.");
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
            user.MsgLocStr($"  Mining hold '{DroneCargo.HoldName}': {(hold != null ? "present" : "MISSING (blocks mining dispatch)")}");
            user.MsgLocStr($"  Link component: {(dock.TryGetComponent<LinkComponent>(out _) ? "present" : "MISSING (blocks mining dispatch)")}");
            user.MsgLocStr($"  Mining job: {(dock.MiningJob == null ? "(none)" : dock.MiningJob.Status.ToString())}");
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
