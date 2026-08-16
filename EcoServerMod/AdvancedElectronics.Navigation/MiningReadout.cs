using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Pure formatting for the Mining tab (U9). Zero dependency on any Eco.* namespace,
    /// matching <see cref="DockReadout"/>'s precedent -- the component holds members and
    /// pushes strings; this decides what they say.
    /// </summary>
    public static class MiningReadout
    {
        /// <summary>
        /// Job status wording, distinguishing complete-but-zero-worked (AE4) from a run
        /// still under way -- a bare status name would read the same for both.
        /// </summary>
        public static string FormatJobStatus(MiningJobStatus status, int workedCount)
        {
            switch (status)
            {
                case MiningJobStatus.Idle: return "idle -- no area assigned";
                case MiningJobStatus.Working: return "working";
                case MiningJobStatus.WaitingToUnload: return "waiting to unload";
                case MiningJobStatus.Complete:
                    return workedCount == 0
                        ? "complete -- finished, nothing was mineable"
                        : "complete";
                case MiningJobStatus.Ended: return "ended";
                default: return status.ToString();
            }
        }

        /// <summary>The stop-reason row: each end reason distinctly worded, or empty when there is none.</summary>
        public static string FormatStopReason(MiningEndReason? reason)
        {
            switch (reason)
            {
                case null: return string.Empty;
                case MiningEndReason.AreaGone: return "the source survey dock's area is gone";
                case MiningEndReason.Unassigned: return "the area was unassigned";
                case MiningEndReason.StampInvalid: return "the stamped citizen no longer has access";
                case MiningEndReason.DevToolSelected: return "the stamped citizen has a permission-ignoring tool selected";
                case MiningEndReason.Halted: return "an administrator halted mining";
                default: return reason.ToString();
            }
        }

        /// <summary>
        /// What is stopping this dock mining right now, which is NOT the same question as why its
        /// last job ended (R42).
        ///
        /// The server-wide halt is checked first and reported without a job, because the halt
        /// refuses dispatch before a job is ever created -- so the state it produces is "no job,
        /// nothing happening, nothing said". Live pass #1 spent three rounds and two restarts
        /// chasing a parked drone whose dock was simply halted: the halt persisted across restarts
        /// exactly as required, and the panel had no way to mention it because it only ever
        /// rendered a finished job's end reason. A control that works but cannot be seen working is
        /// indistinguishable from a broken mod.
        /// </summary>
        public static string FormatBlockedReason(bool haltedServerWide, MiningEndReason? jobEndReason) =>
            haltedServerWide
                ? "an administrator has halted mining server-wide"
                : FormatStopReason(jobEndReason);

        /// <summary>
        /// The composed skip line (R31): the all-zero case, a single category, and
        /// multiple categories each render distinctly, and every rendered count sums to
        /// <paramref name="skippedTotal"/>.
        /// </summary>
        public static string FormatSkipLine(IReadOnlyDictionary<SkipCategory, int> counts, int skippedTotal)
        {
            if (skippedTotal == 0)
                return "none skipped";

            var parts = counts
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Value} {Label(kv.Key)}");

            return string.Join(", ", parts);
        }

        private static string Label(SkipCategory category) => category switch
        {
            SkipCategory.Unreachable => "unreachable",
            SkipCategory.Property => "not authorized (property)",
            SkipCategory.SettlementLaw => "not authorized (settlement law)",
            SkipCategory.Obstructed => "obstructed",
            SkipCategory.Other => "other",
            _ => category.ToString()
        };

        /// <summary>
        /// The headroom row (R30): empty (nothing would fit), full (everything sampled
        /// would fit), and partial (some room, but not everything) render distinctly.
        /// <paramref name="sampleQuantity"/> is what headroom is being measured against --
        /// the hold's current contents, or its full capacity when the hold is empty.
        /// </summary>
        public static string FormatHeadroom(int headroom, int sampleQuantity)
        {
            if (headroom <= 0)
                return "no linked storage headroom -- link a container or free space";
            if (sampleQuantity > 0 && headroom >= sampleQuantity)
                return $"~{headroom} items of headroom -- enough for the current hold";
            return $"~{headroom} items of headroom -- not enough for the current hold";
        }
    }
}
