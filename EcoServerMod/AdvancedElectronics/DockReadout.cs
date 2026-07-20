using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AdvancedElectronics.Navigation;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Pure formatting logic (U6, R14/R15/R8) behind the dock's server-synced readout.
    /// Deliberately has ZERO dependency on any Eco.* namespace -- only
    /// <c>System.*</c> and <see cref="AdvancedElectronics.Navigation"/> types
    /// (<see cref="DroneStatus"/>, <see cref="DensestCellResult"/>), mirroring the
    /// zero-Eco-dependency convention KTD2/U3/U8 already established for the pure
    /// navigation/state-machine libraries -- even though this file physically lives in
    /// the Eco-referencing <c>AdvancedElectronics</c> project (per this unit's Files:
    /// list), it never uses an Eco type, so nothing here actually requires a running
    /// Eco server or the reference assemblies to exercise or reason about.
    ///
    /// <see cref="DroneDockObject"/> (the Eco-side caller) is responsible ONLY for gathering
    /// the live status/ore-result inputs and pushing this class's output through the
    /// real <c>WorldObject.SetAnimatedState</c> sync API -- see that class's
    /// <c>RefreshReadout</c> method.
    /// </summary>
    public static class DockReadout
    {
        /// <summary>
        /// Upper bound on how many per-ore lines the readout will ever produce.
        /// <see cref="DroneDockObject"/> maps each line to a FIXED, ahead-of-time-named
        /// server-synced state slot (the client-side Unity WorldObject's
        /// <c>StringStates</c> array declares a fixed set of names, not a dynamic
        /// list -- see WorldObject.cs in the Unity project) -- an unbounded number of
        /// ore-type lines has no fixed slot to land in. 6 was picked as a generous
        /// round number comfortably covering Eco's vanilla ore roster without the
        /// dock's UI panel growing unreasonably tall; unconfirmed against how many
        /// distinct ore types typically co-occur in one district, revisit once
        /// in-game verification (out of this unit's scope) is run.
        /// </summary>
        public const int MaxOreLines = 6;

        /// <summary>
        /// Formats the readout's first line: current drone status (R15), or a
        /// "no drone docked" line when <paramref name="status"/> is null (the dock has
        /// no paired/spawned drone to report on).
        /// </summary>
        public static string FormatStatusLine(DroneStatus? status) =>
            status.HasValue ? $"Status: {status.Value}" : "Status: no drone docked";

        /// <summary>
        /// Formats one per-ore line (R8): <c>"&lt;ore&gt;: densest at &lt;cell&gt;, ~&lt;density&gt;%"</c>
        /// per KTD6, or a "no data yet" line when <paramref name="result"/> has no
        /// finding for this ore type yet.
        /// </summary>
        public static string FormatOreLine(string oreType, DensestCellResult result)
        {
            if (!result.Found)
                return $"{oreType}: no data yet";

            var pct = (result.Ratio * 100f).ToString("0", CultureInfo.InvariantCulture);
            // Depth is half the answer to "is this worth mining": the same density is a
            // very different job 4 blocks down versus 40. A rock drill conveys this by
            // listing each block's position in the column; a district-wide survey states
            // it directly.
            return $"{oreType}: densest at {result.Cell}, ~{pct}%, shallowest {result.ShallowestDepth} blocks deep";
        }

        /// <summary>
        /// Builds the full ordered line list: the status line first, then up to
        /// <see cref="MaxOreLines"/> per-ore lines (entries with no finding yet are
        /// dropped rather than shown as a "no data" line here, since
        /// <paramref name="oreResults"/> is expected to already be scoped to ore types
        /// with at least one sample -- see <see cref="OreSensorComponent.SampledOreTypes"/>
        /// -- so a not-found result at this layer would mean the caller passed a type
        /// with no data at all, which is not useful to surface on the readout). Ordered
        /// alphabetically by ore type (ordinal) for a stable, predictable line order
        /// across refreshes rather than whatever order the underlying grid's dictionary
        /// happens to enumerate in.
        /// </summary>
        public static IReadOnlyList<string> BuildStateLines(
            DroneStatus? status,
            IReadOnlyList<(string OreType, DensestCellResult Result)> oreResults)
        {
            if (oreResults == null) throw new ArgumentNullException(nameof(oreResults));

            var lines = new List<string>(1 + MaxOreLines) { FormatStatusLine(status) };

            foreach (var entry in oreResults
                         .Where(e => e.Result.Found)
                         .OrderBy(e => e.OreType, StringComparer.Ordinal)
                         .Take(MaxOreLines))
            {
                lines.Add(FormatOreLine(entry.OreType, entry.Result));
            }

            return lines;
        }

        /// <summary>
        /// The single coverage gauge (KTD6's <c>FloatState</c>), 0-100.
        ///
        /// DESIGN NOTE / ASSUMPTION: KTD5 defines per-cell "coverage" as sampled-block
        /// count, and KTD6 asks for "coverage percentage" without pinning an exact
        /// formula. A true "% of the assigned district's area surveyed" gauge would
        /// need a district area/bounds accessor, which is NOT confirmed to exist
        /// server-side (see docs/solutions/best-practices/eco-013-reading-district-civics-data.md --
        /// only point-membership testing is proven) and is out of this unit's bounded
        /// scope to go find. Absent that, this computes an aggregate ore-concentration
        /// figure purely from the given densest-cell results -- (sum of ore-count) /
        /// (sum of sampled-count) across every currently-tracked ore's densest cell --
        /// i.e. "of everything sampled at our best-known spots, how much of it was
        /// ore". This is a genuine, bounded [0, 100] percentage fully derivable from
        /// data U5 already produces, but it is NOT literally "fraction of the district
        /// surveyed" -- flagged here for revisit if/when a district-area accessor
        /// becomes available.
        /// </summary>
        public static float ComputeCoveragePercent(IReadOnlyList<(string OreType, DensestCellResult Result)> oreResults)
        {
            if (oreResults == null) throw new ArgumentNullException(nameof(oreResults));

            long totalOreCount = 0;
            long totalSampledCount = 0;

            foreach (var entry in oreResults)
            {
                if (!entry.Result.Found) continue;
                totalOreCount += entry.Result.OreCount;
                totalSampledCount += entry.Result.SampledCount;
            }

            return totalSampledCount == 0 ? 0f : (float)totalOreCount / totalSampledCount * 100f;
        }
    }
}
