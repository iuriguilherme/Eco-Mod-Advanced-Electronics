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
        /// <param name="hasAssignment">
        /// Whether the dock currently holds an area assignment. Idle means "this job has not
        /// dispatched yet", which happens both before an area is chosen AND in the gap between
        /// assigning one and the drone leaving -- two situations that read identically in the
        /// status alone. Reporting "no area assigned" during the second was simply false, and live
        /// pass #6 caught it saying so with mining:5:9 assigned on the same screen.
        /// </param>
        public static string FormatJobStatus(MiningJobStatus status, int workedCount, bool hasAssignment = false)
        {
            switch (status)
            {
                case MiningJobStatus.Idle: return hasAssignment ? "idle -- waiting to set out" : "idle -- no area assigned";
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
                case MiningEndReason.AreaRedrawn: return "the source area was redrawn -- reassign it to mine the new shape";
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
        /// One line of the Mining tab's offered-areas list, with the same vocabulary the survey
        /// tab's roster uses: yellow [assigned] for the one being worked, green [mined] for one
        /// with nothing left to do until it is re-surveyed.
        ///
        /// The two are not exclusive and the order matters when both apply. Assigned comes first
        /// because it answers "what is the drone doing", which a player is looking for; mined
        /// answers "is there anything here", which they are scanning for. A mined area that is
        /// still assigned is a real state -- the pass finished and nobody has unassigned it -- and
        /// showing only one of the two markers would hide it.
        /// </summary>
        public static string FormatOfferedAreaLine(
            int position, string dockName, string areaName, int plotCount, bool isAssigned, bool isMined)
        {
            var line = $"{position}. {dockName} -- {areaName} ({plotCount} plots)";
            if (isMined) line = DockReadout.AsComplete(line);

            // Appended outside the colour wrap, so each marker keeps its own.
            if (isAssigned) line += DockReadout.AssignedMarker;
            if (isMined) line += DockReadout.MinedMarker;

            return line;
        }

        /// <summary>
        /// The one progress line the Mining tab keeps: how much of the area is done, and how far
        /// into the current plot's shaft the drone has got.
        ///
        /// Folds what used to be two rows into one. The panel carried a row per fact — progress,
        /// skips by category, last refusal, shaft depth, stamps, headroom — which is a debugging
        /// surface, not a player one. All of it still exists in `/drone state`, which is where a
        /// question like "why was that plot skipped" belongs.
        ///
        /// The shaft half is omitted when no shaft is open, rather than shown as 0/0: between
        /// plots there is no current shaft, and a zero there reads as a stalled one.
        /// </summary>
        public static string FormatProgress(int totalPlots, int worked, int skipped, int shaftLayersDone, int shaftLayersTotal)
        {
            var head = $"total: {totalPlots} plots, worked: {worked}, skipped: {skipped}";
            return shaftLayersTotal > 0
                ? $"{head}, current: {shaftLayersDone}/{shaftLayersTotal} layers"
                : head;
        }

        /// <summary>
        /// Shaft depth reached and the two stamps behind the mineable decision (KTD12).
        ///
        /// The stamps are shown raw rather than as a verdict: the whole reason this row exists is
        /// that the verdict ("nothing was mineable") disagreed with the ground (five layers dug),
        /// and only the inputs can settle which one is wrong. IsMineable wants surveyed newer than
        /// mined, so surveyed &lt;= mined is the failing case and reads as such at a glance.
        /// </summary>
        public static string FormatShaftProgress(int layersDone, int layersTotal, long surveyedStamp, long minedStamp)
        {
            if (layersTotal <= 0 && surveyedStamp == 0 && minedStamp == 0) return string.Empty;

            var mineable = surveyedStamp > minedStamp ? "mineable" : "NOT mineable";
            return $"shaft {layersDone}/{layersTotal} layers; surveyed={surveyedStamp}, mined={minedStamp} ({mineable})";
        }

        /// <summary>
        /// The engine's own words for the last refusal, or empty when nothing has been refused.
        ///
        /// A row of its own rather than folded into the skip line, because the two answer
        /// different questions: the skip line counts what happened across the area, this says why
        /// the most recent one failed. Prefixed so a raw engine string does not read as the mod's
        /// own prose.
        /// </summary>
        public static string FormatRefusalDetail(string detail) =>
            string.IsNullOrWhiteSpace(detail) ? string.Empty : "last refusal: " + detail.Trim();

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
