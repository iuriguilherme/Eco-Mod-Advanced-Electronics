using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// The dock's per-crop harvest ceiling (R25): the quantity at or above which a crop
    /// stops being harvested, answered against what the dock's linked storage holds (R27).
    ///
    /// Two states, not three. A crop either carries a ceiling or it does not, and not
    /// carrying one means harvest without limit -- the default every crop starts at, and
    /// not an unconfigured state for the tab to report (R26). Zero is how a citizen
    /// removes a ceiling, so it is stored as absence rather than as a limit of nothing.
    /// </summary>
    /// <remarks>
    /// The ceiling belongs to the crop rather than to an area, so every area growing that
    /// crop answers to the same one (R25). Crop keys are the same opaque, ordinally
    /// compared keys <see cref="FarmBlockDecision"/> uses -- this project stays Eco-free
    /// (KTD6) and the adapter owns what identifies a crop.
    /// </remarks>
    public sealed class CropCeilingLedger
    {
        // Only crops a citizen has actually limited live here. Every other crop in the
        // game answers "harvest it" without occupying a row, which is what lets the
        // Crop Ceilings grid render all 37 of them from a ledger holding two.
        private readonly Dictionary<string, int> ceilings = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The crops carrying a ceiling, in insertion order. Empty is the normal starting state.</summary>
        public IReadOnlyList<string> ConfiguredCrops => this.ceilings.Keys.ToList();

        /// <summary>
        /// Sets <paramref name="crop"/>'s ceiling. Zero removes it (R25), so the grid's
        /// untouched default value means "no limit" rather than "harvest nothing".
        /// </summary>
        public void Set(string crop, int ceiling)
        {
            RequireCrop(crop);
            if (ceiling < 0)
                throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "A harvest ceiling is never negative.");

            if (ceiling == 0) this.ceilings.Remove(crop);
            else this.ceilings[crop] = ceiling;
        }

        /// <summary>Whether <paramref name="crop"/> carries a ceiling at all.</summary>
        public bool HasCeiling(string crop)
        {
            RequireCrop(crop);
            return this.ceilings.ContainsKey(crop);
        }

        /// <summary>
        /// The configured ceiling, or zero when there is none -- the same value the grid
        /// shows in an untouched row, so reading a row and writing it back is a no-op.
        /// </summary>
        public int CeilingFor(string crop)
        {
            RequireCrop(crop);
            return this.ceilings.TryGetValue(crop, out var ceiling) ? ceiling : 0;
        }

        /// <summary>
        /// The one question the drone asks: given what linked storage holds, may this crop
        /// be harvested? Answers for any crop, including one holding no row, so callers
        /// never null-check a missing ceiling.
        /// </summary>
        public bool MayHarvest(string crop, int storedQuantity)
        {
            RequireCrop(crop);
            if (storedQuantity < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(storedQuantity), storedQuantity, "A stored count is never negative.");

            // No row means no limit. "At or above" stops the harvest (R25), so equality
            // does not allow one last one through.
            return !this.ceilings.TryGetValue(crop, out var ceiling) || storedQuantity < ceiling;
        }

        private static void RequireCrop(string crop)
        {
            if (string.IsNullOrEmpty(crop))
                throw new ArgumentException("A ceiling always belongs to a named crop.", nameof(crop));
        }
    }
}
