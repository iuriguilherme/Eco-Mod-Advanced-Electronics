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
    /// (<see cref="DroneStatus"/>, <see cref="SurveyFinding"/>), mirroring the
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
        /// Formats one per-ore line (R7/R8) from a persisted-and-reprojected
        /// <see cref="SurveyFinding"/>: the precise dig block, plot concentration, and depth.
        /// Unlike the old density model this points at a block, not a cell — the survey's whole
        /// value is telling the player exactly where to dig.
        /// </summary>
        public static string FormatOreLine(SurveyFinding finding)
        {
            if (!finding.Found)
                return $"{finding.OreType}: no data yet";

            var pct = (finding.Concentration * 100f).ToString("0", CultureInfo.InvariantCulture);
            // Depth is half the answer to "is this worth mining": the same concentration is a
            // very different job 4 blocks down versus 40, so it is stated directly.
            return $"{finding.OreType}: dig at {finding.Position}, ~{pct}%, {finding.DepthBelowSurface} blocks deep";
        }

        /// <summary>
        /// Builds the full ordered line list: the status line first, then up to
        /// <see cref="MaxOreLines"/> per-ore finding lines. Ordered alphabetically by ore type
        /// (ordinal) for a stable line order across refreshes rather than dictionary-enumeration
        /// order. Findings are expected to already be the per-area <see cref="Found"/> set.
        /// </summary>
        public static IReadOnlyList<string> BuildStateLines(
            DroneStatus? status,
            IReadOnlyList<SurveyFinding> findings)
        {
            if (findings == null) throw new ArgumentNullException(nameof(findings));

            var lines = new List<string>(1 + MaxOreLines) { FormatStatusLine(status) };

            foreach (var finding in findings
                         .Where(f => f.Found)
                         .OrderBy(f => f.OreType, StringComparer.Ordinal)
                         .Take(MaxOreLines))
            {
                lines.Add(FormatOreLine(finding));
            }

            return lines;
        }
    }
}
