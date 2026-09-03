using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What one edit did to an area's plots (U9, R20): the plots it RETAINED, the plots it
    /// REMOVED, and the plots it ADDED.
    ///
    /// <para>
    /// The three sets are the whole of the decision. Everything the area records is per plot --
    /// findings rows, surveyed stamps, mined stamps, at-bedrock observations, the running pass's
    /// columns -- so "what does an edit preserve" reduces to "which plots survived it", and the
    /// answer is the same for every one of those records. Only what the edit removed is dropped;
    /// what it added is unsurveyed and needs a pass.
    /// </para>
    /// </summary>
    public sealed class AreaEditPlan
    {
        public AreaEditPlan(
            IReadOnlyList<PlotCoord> retained, IReadOnlyList<PlotCoord> removed, IReadOnlyList<PlotCoord> added)
        {
            this.Retained = retained ?? throw new ArgumentNullException(nameof(retained));
            this.Removed = removed ?? throw new ArgumentNullException(nameof(removed));
            this.Added = added ?? throw new ArgumentNullException(nameof(added));
        }

        /// <summary>Plots the area held before the edit and still holds. Everything they recorded survives (R20).</summary>
        public IReadOnlyList<PlotCoord> Retained { get; }

        /// <summary>Plots the edit took out of the area. The only plots anything is dropped for.</summary>
        public IReadOnlyList<PlotCoord> Removed { get; }

        /// <summary>Plots the edit brought in. Unsurveyed by definition -- nothing has ever looked at them (R3).</summary>
        public IReadOnlyList<PlotCoord> Added { get; }

        /// <summary>True when the edit actually changed the geometry rather than merely restating it.</summary>
        public bool ChangesGeometry => this.Removed.Count > 0 || this.Added.Count > 0;

        /// <summary>
        /// True when the edit took nothing away. This is the case R21 turns on: an in-flight
        /// mining job keeps running, because every plot it was built against is still there.
        /// </summary>
        public bool AddsOnly => this.Removed.Count == 0;

        /// <summary>
        /// The plots that leave a claim with them (U9 step 5, KTD6, R38).
        ///
        /// <para>
        /// A claim is a record on the AREA naming the dock that holds it, keyed to the
        /// assignment rather than to the geometry -- so an edit never invalidates it. The claim
        /// simply stands over whatever plots the area holds now, which makes added plots join it
        /// with no further ceremony and makes exactly the removed plots the ones that fall out of
        /// it. They are what a release message has to name.
        /// </para>
        /// </summary>
        public IReadOnlyList<PlotCoord> ReleasedFromClaim => this.Removed;
    }

    /// <summary>
    /// The decidable half of U9: what an edit to an area's plots preserves, what it drops, what
    /// it means for a mining job in flight, and where it leaves a survey pass mid-sweep.
    ///
    /// <para>
    /// Eco-free and unit-tested, per KTD12. The Eco side (<c>SurveyAreaEntry.SetPlots</c>,
    /// <c>MiningStrategy</c>, <c>DroneLifecycle</c>) reads world state, calls these, and persists
    /// the result -- none of it re-decides anything here.
    /// </para>
    /// </summary>
    public static class AreaEdit
    {
        /// <summary>
        /// Sorts the plots of an edit into retained, removed and added. Duplicates on either side
        /// collapse, matching <see cref="SurveyArea"/>, which is a set -- a plot listed twice is
        /// one plot and must not be counted twice by anything downstream.
        /// </summary>
        public static AreaEditPlan Plan(IEnumerable<PlotCoord> before, IEnumerable<PlotCoord> after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var beforeSet = new HashSet<PlotCoord>(before);
            var afterSet = new HashSet<PlotCoord>(after);

            // Raster-ordered, not hash-ordered: these lists are read back by a player-facing
            // release message (step 5) and by tests, and a set's enumeration order is not
            // something either should be made to depend on.
            return new AreaEditPlan(
                SweepOrder.RasterOrder(beforeSet.Where(afterSet.Contains)),
                SweepOrder.RasterOrder(beforeSet.Where(p => !afterSet.Contains(p))),
                SweepOrder.RasterOrder(afterSet.Where(p => !beforeSet.Contains(p))));
        }

        /// <summary>
        /// R21. Whether an edit that left the area holding <paramref name="plotsAfterEdit"/>
        /// removed ground a job with <paramref name="pendingPlots"/> still has to work -- the one
        /// question that decides whether a redraw ends that job.
        ///
        /// <para>
        /// The job's own ledger is the record of the pre-edit geometry: it is built from the
        /// area's plots at dispatch and never re-keyed, so nothing has to remember the old plot
        /// list separately. A pending plot the area no longer holds is work the job can never
        /// finish and never account for, and that -- not the epoch changing -- is what makes the
        /// job unfinishable. A plot the job has already worked or skipped leaving the area takes
        /// nothing with it: its outcome is recorded and the job is done with it.
        /// </para>
        /// </summary>
        public static bool RemovesPendingWork(
            IEnumerable<PlotCoord> pendingPlots, IEnumerable<PlotCoord> plotsAfterEdit)
        {
            if (pendingPlots == null) return false;
            if (plotsAfterEdit == null) throw new ArgumentNullException(nameof(plotsAfterEdit));

            var held = new HashSet<PlotCoord>(plotsAfterEdit);
            return pendingPlots.Any(p => !held.Contains(p));
        }

        /// <summary>
        /// R22. Where a sweep standing at (<paramref name="plotIndex"/>,
        /// <paramref name="columnCursor"/>) over <paramref name="before"/> must carry on from
        /// once the area holds <paramref name="after"/> instead -- so a pass landing mid-edit
        /// stays alive rather than being restarted.
        ///
        /// <para>
        /// The cursor is an index into the raster order (<see cref="SweepOrder.RasterOrder"/>),
        /// and an edit rewrites that order: the same index names a different plot afterwards, so
        /// leaving it alone is not "keeping the pass", it is aiming it somewhere arbitrary. It is
        /// carried across by NAME instead. The plot the sweep was on keeps its place if the edit
        /// retained it; if the edit removed it, the sweep resumes at the first plot after it that
        /// survived, because everything before that has already been flown.
        /// </para>
        /// <para>
        /// Then the added plots. They are unsurveyed and need a pass, and one that sorts BEHIND
        /// the carried cursor would never be reached -- the same hazard an outside change's reset
        /// creates, so it takes the same cure: <see cref="SweepOrder.RewindIndex"/> pulls the
        /// cursor back to the earliest of them. Re-flying the retained plots in between costs
        /// travel and nothing else; their samples are still in the record, so they dedupe on
        /// arrival and their coverage never moves.
        /// </para>
        /// <para>
        /// The column cursor survives only when the sweep is still standing on the very plot it
        /// was standing on. Any other outcome is a different plot, whose columns have their own
        /// numbering, so it starts from the first.
        /// </para>
        /// </summary>
        public static SweepCursor RemapSweep(
            IEnumerable<PlotCoord> before, IEnumerable<PlotCoord> after, int plotIndex, int columnCursor)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var beforeOrder = SweepOrder.RasterOrder(new HashSet<PlotCoord>(before));
            var afterOrder = SweepOrder.RasterOrder(new HashSet<PlotCoord>(after));
            var beforeSet = new HashSet<PlotCoord>(beforeOrder);
            var afterSet = new HashSet<PlotCoord>(afterOrder);

            // Past the end means the sweep had finished its old plot list, and stays finished
            // unless an added plot pulls it back below -- which the rewind below does.
            var mapped = afterOrder.Count;
            var standingOnSamePlot = false;

            for (var i = Math.Max(0, plotIndex); i < beforeOrder.Count; i++)
            {
                if (!afterSet.Contains(beforeOrder[i])) continue;

                mapped = IndexIn(afterOrder, beforeOrder[i]);
                standingOnSamePlot = i == plotIndex;
                break;
            }

            var added = afterOrder.Where(p => !beforeSet.Contains(p));
            var rewound = SweepOrder.RewindIndex(afterOrder, added, mapped);

            return new SweepCursor(
                rewound,
                rewound == mapped && standingOnSamePlot ? Math.Max(0, columnCursor) : 0);
        }

        /// <summary>
        /// R20/R22. The coverage figure an area of <paramref name="plotsBefore"/> plots reading
        /// <paramref name="coveragePercent"/> carries once it holds <paramref name="plotsAfter"/>
        /// plots instead.
        ///
        /// <para>
        /// Coverage is a share of plots, so an edit moves it by moving the denominator: ten
        /// surveyed plots out of ten read 100%, and the same ten out of twelve read 83%. It falls
        /// by the share the new plots represent and by nothing else -- which is the whole visible
        /// difference between preserving an edit's retained plots and discarding them.
        /// </para>
        /// <para>
        /// The removed plots' own share must already be out of <paramref name="coveragePercent"/>
        /// -- on the Eco side that is what the per-plot drop does before this is called, over the
        /// pre-edit plot count. This only restates what is left against the new count.
        /// </para>
        /// </summary>
        public static float RescaleCoverage(float coveragePercent, int plotsBefore, int plotsAfter)
        {
            if (plotsAfter <= 0 || plotsBefore <= 0 || coveragePercent <= 0f)
                return 0f;

            return Math.Clamp(coveragePercent * plotsBefore / plotsAfter, 0f, 100f);
        }

        private static int IndexIn(IReadOnlyList<PlotCoord> plots, PlotCoord plot)
        {
            for (var i = 0; i < plots.Count; i++)
                if (plots[i].Equals(plot))
                    return i;
            return plots.Count;
        }
    }
}
