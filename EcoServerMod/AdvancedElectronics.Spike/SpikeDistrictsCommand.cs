using System;
using System.Linq;
using Eco.Core.Systems;
using Eco.Gameplay.Civics.Districts;
using Eco.Gameplay.LegislationSystem;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Gameplay.Settlements;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.Math;

namespace AdvancedElectronics.Spike
{
    /// <summary>
    /// Q3 probe (data half): what map-drawn area data can a server mod read?
    /// Enumerates EVERY area-shaped registry — district maps (the law/map-drawing UI's
    /// backing store, Eco.Gameplay.Civics.Districts), settlements, and deeds — so absence
    /// in one registry cannot masquerade as "no area exists" (spike plan, KTD6).
    /// The picker (UI) half of Q3 is a documented finding in EcoServerMod/README.md.
    /// </summary>
    [ChatCommandHandler]
    public static class SpikeDistrictsCommand
    {
        [ChatSubCommand("Spike", "Q3: enumerate district maps, settlements, and deeds visible to a server mod at your position.", ChatAuthorizationLevel.Admin)]
        public static void Districts(User user)
        {
            var pos = user.Position;
            var pos2i = new WorldPosition2i((int)pos.X, (int)pos.Z);
            user.MsgLocStr($"[Q3] Enumerating area registries at {(int)pos.X},{(int)pos.Z} ...");

            // --- District maps (law/map-drawing UI backing store) ---
            Probe(user, "districts", () =>
            {
                var maps = Registrars.Get<DistrictMap>().ToArray();
                user.MsgLocStr($"[Q3 districts] {maps.Length} district map(s) registered.");
                foreach (var map in maps)
                {
                    var districts = map.Districts?.Values?.OfType<District>().ToArray() ?? Array.Empty<District>();
                    user.MsgLocStr($"[Q3 districts] map '{map.Name}': {districts.Length} district(s): {string.Join(", ", districts.Select(d => d.Name))}");
                    var here = map.GetDistrictAtWorldPos(pos2i);
                    user.MsgLocStr(here != null
                        ? $"[Q3 districts] you are inside '{here.Name}' on map '{map.Name}'"
                        : $"[Q3 districts] you are in no district on map '{map.Name}'");
                }
            });

            // --- Settlements ---
            Probe(user, "settlements", () =>
            {
                var settlements = Registrars.Get<Settlement>().ToArray();
                user.MsgLocStr($"[Q3 settlements] {settlements.Length} settlement(s): {string.Join(", ", settlements.Select(s => s.Name))}");
            });

            // --- Deeds (claim areas) ---
            Probe(user, "deeds", () =>
            {
                var deeds = Registrars.Get<Deed>().ToArray();
                user.MsgLocStr($"[Q3 deeds] {deeds.Length} deed(s) total; first 5: {string.Join(", ", deeds.Take(5).Select(d => d.Name))}");
            });
        }

        /// <summary>Each registry probes independently so one failure cannot mask another (spike plan, KTD6).</summary>
        private static void Probe(User user, string label, Action body)
        {
            try { body(); }
            catch (Exception e) { user.MsgLocStr($"[Q3 {label}] FAIL: {e.GetType().Name}: {e.Message}"); }
        }
    }
}
