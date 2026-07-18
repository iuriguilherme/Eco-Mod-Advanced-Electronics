using System;
using System.Linq;
using System.Numerics;
using Eco.Core.Systems;
using Eco.Gameplay.Civics.Districts;
using Eco.Gameplay.LegislationSystem;
using Eco.Shared.Math;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// District resolution and membership-test logic backing R12 (survey district
    /// assignment). Deliberately independent of <see cref="DroneCommands"/> / chat
    /// plumbing so any future consumer -- e.g. a later unit's dispatch/lifecycle code
    /// that needs to know whether a roaming drone's current position is still inside
    /// its assigned survey area -- can call these directly.
    ///
    /// Follows the read pattern proven in
    /// docs/solutions/best-practices/eco-013-reading-district-civics-data.md and
    /// EcoServerMod/AdvancedElectronics.Spike/SpikeDistrictsCommand.cs: enumerate
    /// district maps via <c>Registrars.Get&lt;DistrictMap&gt;()</c>, test point
    /// membership via <c>DistrictMap.GetDistrictAtWorldPos</c>.
    /// </summary>
    public static class DistrictAssignment
    {
        /// <summary>
        /// Finds a district by name (case-insensitive) across every registered
        /// <see cref="DistrictMap"/>. Districts are technically per-map, but v1 (KTD4)
        /// does not ask for map-scoped disambiguation, so the first match across all
        /// maps wins -- a reasonable simplification given a server typically has one
        /// law/district map in active use. Returns null if no district with that name
        /// exists on any map, or if <paramref name="name"/> is null/blank.
        /// </summary>
        public static District FindDistrictByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            foreach (var map in Registrars.Get<DistrictMap>())
            {
                var districts = map.Districts?.Values?.OfType<District>();
                if (districts == null) continue;

                var match = districts.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return null;
        }

        /// <summary>
        /// True if <paramref name="worldPos"/> currently falls inside
        /// <paramref name="district"/>. Re-queries
        /// <c>district.ContainingMap.GetDistrictAtWorldPos</c> rather than trusting any
        /// cached membership result, so a district that was resized or redrawn after
        /// assignment is reflected immediately on the next check. False for a null
        /// district (nothing assigned) or a district whose map can no longer be
        /// resolved.
        /// </summary>
        public static bool IsPositionInDistrict(Vector3 worldPos, District district)
        {
            if (district?.ContainingMap == null) return false;

            var pos2i = new WorldPosition2i((int)worldPos.X, (int)worldPos.Z);
            var here = district.ContainingMap.GetDistrictAtWorldPos(pos2i);
            return here != null && here.Id == district.Id;
        }

        /// <summary>
        /// Convenience for the common case: is <paramref name="worldPos"/> inside the
        /// district currently assigned to <paramref name="dock"/>? Re-resolves
        /// <see cref="DroneDockObject.AssignedDistrictName"/> by name on every call (rather
        /// than the dock caching a live <see cref="District"/> reference) -- see that
        /// property's doc comment for why the dock stores a name, not the object.
        /// Returns false when the dock has no district assigned, or when its assigned
        /// name no longer resolves to a real district (e.g. it was deleted).
        /// </summary>
        public static bool IsPositionInAssignedDistrict(this DroneDockObject dock, Vector3 worldPos)
        {
            if (dock == null) return false;
            var district = FindDistrictByName(dock.AssignedDistrictName);
            return IsPositionInDistrict(worldPos, district);
        }
    }
}
