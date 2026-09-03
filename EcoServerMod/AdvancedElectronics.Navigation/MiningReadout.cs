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
        public static string FormatJobStatus(
            MiningJobStatus status, int workedCount, bool hasAssignment = false, string travel = null)
        {
            var job = JobWord(status, workedCount, hasAssignment);

            if (string.IsNullOrEmpty(travel)) return job;

            // With no assignment the job word says nothing worth reading -- the drone is between
            // jobs, and where it is IS the status. Unassigning used to leave "working" on screen
            // while the drone flew home.
            if (!hasAssignment) return travel;

            // "working -- at the area" is noise: being at the area is what working means.
            if (travel == DockReadout.AtAreaPhrase
                && (status == MiningJobStatus.Working || status == MiningJobStatus.WaitingToUnload))
                return job;

            // Likewise for a job that has not set out -- its word already says it is at the dock.
            if (travel == DockReadout.DockedPhrase && status == MiningJobStatus.Idle) return job;

            return $"{job} -- {travel}";
        }

        private static string JobWord(MiningJobStatus status, int workedCount, bool hasAssignment)
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
                case MiningEndReason.AreaOutOfRange: return OutOfRangeAssignmentReason;
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
        /// <param name="assignmentOutOfRange">
        /// R23, R24: the assignment still resolves, but its survey dock now sits outside this
        /// dock's network radius. Ranked above the job's end reason because the job ends on the
        /// vanished-area path (R24) and would otherwise report "the area is gone" about an area
        /// that is plainly still on the map -- which is the guess R23 exists to remove.
        /// </param>
        public static string FormatBlockedReason(
            bool haltedServerWide, MiningEndReason? jobEndReason, bool assignmentOutOfRange = false)
        {
            if (haltedServerWide) return "an administrator has halted mining server-wide";
            if (assignmentOutOfRange) return OutOfRangeAssignmentReason;
            return FormatStopReason(jobEndReason);
        }

        private const string OutOfRangeAssignmentReason =
            "the assigned area's survey dock is out of range of this dock's network";

        /// <summary>
        /// Whether a survey dock <paramref name="distance"/> metres away lies inside a mining
        /// dock's network radius (R14, KTD9).
        ///
        /// <para>
        /// The radius itself is NOT declared here: it is a dock constant, deliberately separate
        /// from the storage link radius, because R14 expects it to become an upgrade-module
        /// effect and a shared constant could not carry that. This function only decides which
        /// side of a given radius a given distance falls on.
        /// </para>
        /// <para>
        /// Inclusive at the boundary and with no tolerance band, so a pair sitting exactly at the
        /// radius is in range and stays there. A hysteresis band would make the same pair read
        /// differently depending on which way it last crossed, which is the one thing a placement
        /// rule must not do -- the player is holding the dock and watching the list.
        /// </para>
        /// </summary>
        public static bool IsWithinDockNetwork(float distance, float radius) =>
            !float.IsNaN(distance) && distance <= radius;

        /// <summary>
        /// The out-of-range notice on the offered-areas list (R23). Empty when every survey dock
        /// the owner test admits is also in range.
        ///
        /// <para>
        /// It counts DOCKS rather than areas: what the player has to move is a dock, and a
        /// distant dock holding nine areas is one problem, not nine.
        /// </para>
        /// </summary>
        public static string FormatOutOfRangeDocks(int dockCount, float radius)
        {
            if (dockCount <= 0) return string.Empty;

            var subject = dockCount == 1 ? "1 survey dock is" : $"{dockCount} survey docks are";
            return $"{subject} out of range -- beyond this dock's {radius:F0} m network. "
                 + "Move a dock closer to work its areas.";
        }

        /// <summary>
        /// The whole offered-areas body (R23): the numbered roster, the out-of-range notice, or
        /// both.
        ///
        /// <para>
        /// The empty-and-out-of-range case is the reason this exists. Filtering the roster by
        /// radius without saying so leaves a dock reporting "no survey docks with an area were
        /// found" while the player is looking at one thirty metres away -- and nothing on the
        /// panel distinguishes "too far" from "has no areas". Those are different problems with
        /// different fixes, so they get different words.
        /// </para>
        /// </summary>
        public static string FormatAvailableAreas(
            IReadOnlyList<string> offeredLines, int outOfRangeDockCount, float radius)
        {
            var notice = FormatOutOfRangeDocks(outOfRangeDockCount, radius);

            if (offeredLines == null || offeredLines.Count == 0)
                return string.IsNullOrEmpty(notice)
                    ? "No survey docks with an area were found."
                    : notice;

            var roster = string.Join("\n", offeredLines);
            return string.IsNullOrEmpty(notice) ? roster : $"{roster}\n{notice}";
        }

        /// <summary>
        /// The assigned-area row (R24). A world upgrading into the radius can separate a pair
        /// that was legally assigned before it existed; the assignment is REPORTED out of range
        /// rather than cleared, because silently dropping a player's assignment on load is
        /// indistinguishable from the mod losing it.
        /// </summary>
        public static string FormatAssignedArea(string owningDockName, string areaName, bool withinDockNetwork)
        {
            var line = $"{owningDockName} -- {areaName}";
            return withinDockNetwork ? line : $"{line} -- out of range";
        }

        /// <summary>
        /// One line of the Mining tab's offered-areas list. Not a second formatter: it is
        /// <see cref="DockReadout.FormatRosterLine"/> with the owning survey dock's name
        /// prefixed, which is the only difference R29 permits between the two tabs.
        ///
        /// <para>
        /// The vocabulary this replaces was this tab's own -- a green <c>[mined]</c> and a yellow
        /// <c>[assigned]</c>, appended in an order argued for here rather than fixed anywhere
        /// shared, on a line whose fields did not even match the Survey tab's. That is the drift
        /// one builder removes. Ordering and the two-tag cap now belong to the shared channel,
        /// and the colour to the area's own lifecycle status.
        /// </para>
        /// <para>
        /// The prefix earns its place: this tab lists areas from several docks, area names are
        /// not unique, and the selector commits by position -- without it a player can assign the
        /// wrong area.
        /// </para>
        /// </summary>
        public static string FormatOfferedAreaLine(AreaSnapshot area, string owningDockName) =>
            DockReadout.FormatRosterLine(area, owningDockName);

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
        /// What an area still excludes after the job that produced it has gone (R27, U3), or
        /// empty when it excludes nothing.
        ///
        /// R27's obligation is that an area whose `[cleared]` rests on an exclusion NAMES the
        /// refusal where the mining tab already reports reasons -- the stop-reason and skip
        /// rows -- so the player can see what they would have to change to unblock it, and
        /// blocked ground never reads as spent ground. The skip line beside this one can only
        /// speak while the job lives; this one reads the persisted record, which is the only
        /// thing left to answer once the job is over.
        ///
        /// Uses the same category wording as <see cref="FormatSkipLine"/> -- one vocabulary
        /// (KTD5) -- and appends the engine's own words for the refusal when the record carries
        /// them, because "obstructed" names the bucket and not the cause.
        /// </summary>
        public static string FormatExclusionLine(IReadOnlyList<MiningExclusion> exclusions)
        {
            if (exclusions == null || exclusions.Count == 0)
                return string.Empty;

            var parts = exclusions
                .GroupBy(e => e.Category)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => (int)g.Key)
                .Select(g => $"{g.Count()} {Label(g.Key)}");

            // Names what would actually clear it (R45): assigning this area to this dock again.
            // A resurvey will not -- a survey cannot test settlement law or property -- and
            // telling the player to resurvey would send them to do the one thing that cannot
            // work.
            var line = "excluded until reassigned: " + string.Join(", ", parts);

            var detail = exclusions
                .Select(e => e.Detail)
                .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));

            return string.IsNullOrWhiteSpace(detail) ? line : $"{line} -- {detail.Trim()}";
        }

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
