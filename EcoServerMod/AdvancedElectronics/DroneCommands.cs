using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.IoC;
using Eco.Shared.Items;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Chat commands for the survey drone dock (U4, R12). District assignment ships as
    /// a chat command in v1 per KTD4: the spike proved district *reads* work
    /// (docs/solutions/best-practices/eco-013-reading-district-civics-data.md) but left
    /// the on-object picker unproven (the 0.11-era UI attribute is gone in 0.13), so
    /// <c>/drone district &lt;name&gt;</c> is the guaranteed path here. An on-object
    /// picker is opportunistic future work (U6) and is not attempted in this unit.
    /// </summary>
    [ChatCommandHandler]
    public static class DroneCommands
    {
        [ChatCommand("Advanced Electronics drone commands.", ChatAuthorizationLevel.User)]
        public static void Drone(User user) { }

        /// <summary>
        /// Assigns (or, with an empty/omitted name, clears) the survey district on the
        /// target dock.
        ///
        /// DESIGN CALL (the one genuinely open call in this unit -- see class doc for
        /// why a chat command exists at all): a chat command has no client raycast or
        /// "targeted WorldObject" to read, so this unit must pick which dock the
        /// command applies to. The target is the *nearest DroneDockObject the invoking player
        /// has full access to*
        /// (<c>WorldObject.IsAuthorized(user, AccessType.FullAccess)</c>), not simply
        /// the nearest dock in the world. Reasoning: (1) it mirrors "owner/admin" auth
        /// per KTD4 -- an admin passes IsAuthorized regardless of ownership, an
        /// ordinary player only passes it on docks they own or were granted access to;
        /// (2) letting any player redirect a district assignment on someone else's dock
        /// just by standing near it would be a real griefing vector, so proximity alone
        /// (nearest-any-dock) was rejected in favor of proximity-plus-authorization.
        /// </summary>
        [ChatSubCommand("Drone", "Assign the survey district for your nearest accessible drone dock. Empty name clears the assignment. Usage: /drone district <name>", ChatAuthorizationLevel.User)]
        public static void District(User user, string name = "")
        {
            var dock = FindNearestAuthorizedDock(user);
            if (dock == null)
            {
                user.MsgLocStr("No drone dock you have access to was found nearby.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                dock.SetAssignedDistrict(null);
                user.MsgLocStr($"Cleared the survey district assignment for {dock.Name}.");
                return;
            }

            var district = DistrictAssignment.FindDistrictByName(name);
            if (district == null)
            {
                user.MsgLocStr($"No district named '{name}' was found. Draw it on the map first, then retry.");
                return;
            }

            dock.SetAssignedDistrict(district.Name);
            user.MsgLocStr($"Assigned survey district '{district.Name}' to {dock.Name}.");
        }

        /// <summary>
        /// Dumps the complete server-side state of the nearest accessible dock and its
        /// drone. Diagnostic surface so ONE live session yields full information about
        /// the pairing/dispatch/survey pipeline without depending on any client UI
        /// rendering (world text, tooltip, window) and without repeated
        /// restart-observe cycles — every layer reports its own truth in chat.
        /// </summary>
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
            user.MsgLocStr($"  District: {(string.IsNullOrEmpty(dock.AssignedDistrictName) ? "(none)" : dock.AssignedDistrictName)}");
            user.MsgLocStr($"  Paired drone item: {(dock.HasDrone ? "yes" : "no")}");
            user.MsgLocStr($"  Anim state Working: {FormatAnimState(dock, DroneDockObject.WorkingStateName)}");

            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)
            {
                user.MsgLocStr("  Spawned drone: none (insert a Survey Drone to spawn one).");
                return;
            }

            user.MsgLocStr($"  Spawned drone at {drone.Position3i} (owner: {(drone.HasOwner ? drone.OwnerName : "unstamped")})");
            user.MsgLocStr($"  Anim state MoveSpeed: {FormatAnimState(drone, DroneMoverComponent.MoveSpeedStateName)}");

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

            if (drone.TryGetComponent<OreSensorComponent>(out var sensor))
            {
                var any = false;
                foreach (var oreType in sensor.SampledOreTypes)
                {
                    any = true;
                    var cell = sensor.DensestCell(oreType);
                    user.MsgLocStr($"  Ore '{oreType}': densest {(cell.Found ? $"cell {cell.Cell}, {cell.OreCount}/{cell.SampledCount}" : "no data")}");
                }
                if (!any) user.MsgLocStr("  Ore data: none sampled yet.");
            }
            else
                user.MsgLocStr("  Sensor: component MISSING");
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
        /// Nearest-owned-or-authorized-dock lookup backing the design call documented
        /// on <see cref="District"/>. Eco exposes no "targeted WorldObject" helper
        /// reachable from a server-side chat command, so proximity plus per-object
        /// authorization is the most defensible stand-in available.
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
    }
}
