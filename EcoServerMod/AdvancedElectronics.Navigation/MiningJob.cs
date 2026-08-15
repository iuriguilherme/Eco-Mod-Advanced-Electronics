using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What the Mining tab reports (KTD10). Deliberately carries no travelling or
    /// returning state -- the drone's travel state machine (Eco-side) owns those, and
    /// the panel composes the two. Mutated only through <see cref="MiningJob"/>'s named
    /// methods.
    /// </summary>
    public enum MiningJobStatus
    {
        Idle,
        Working,
        WaitingToUnload,
        Complete,
        Ended
    }

    /// <summary>Why a job in progress stopped short of completion (R6, R7, R33, R37, R42).</summary>
    public enum MiningEndReason
    {
        /// <summary>The area was confirmed gone -- its owning survey dock was picked up (R6).</summary>
        AreaGone,

        /// <summary>The area assignment was cleared (R7).</summary>
        Unassigned,

        /// <summary>The stamped citizen no longer holds the required access (R33) or was never validly stamped.</summary>
        StampInvalid,

        /// <summary>The stamped citizen has a permission-ignoring tool selected (R37).</summary>
        DevToolSelected,

        /// <summary>An administrator halted mining server-wide (R42).</summary>
        Halted
    }

    /// <summary>What became of one plot: still to do, worked, or abandoned with a reason (R16, R22).</summary>
    public enum PlotOutcome
    {
        Unworked,
        Worked,
        Skipped
    }

    /// <summary>
    /// The fixed skip-reason set the panel composes into one line (R31). "Obstructed"
    /// covers both a pretest failure and a block the classifier rejected reaching the
    /// removal service; "Other" is the defined fallback for a refusal that matches
    /// none of the named categories, so the category counts always sum to the
    /// skipped total.
    /// </summary>
    public enum SkipCategory
    {
        /// <summary>The drone could not reach the plot (arrival-attempt cap exceeded).</summary>
        Unreachable,

        /// <summary>Refused under private-property authorization.</summary>
        Property,

        /// <summary>Refused under settlement law.</summary>
        SettlementLaw,

        /// <summary>Reached the plot, but a pretest or classification failure stopped the removal.</summary>
        Obstructed,

        /// <summary>A refusal that matches none of the above -- the defined fallback (R22).</summary>
        Other
    }

    /// <summary>
    /// Where in the engine's pipeline a removal was refused (R21's evaluation order:
    /// laws, then per-position authorization, then pretests) -- the Eco-side removal
    /// service classifies the engine's raw refusal into one of these before handing it
    /// to <see cref="RefusalMapping.ToSkipCategory"/>, which is what keeps that mapping
    /// itself pure and Eco-free (KTD6).
    /// </summary>
    public enum RemovalRefusalStage
    {
        Property,
        SettlementLaw,
        Pretest,

        /// <summary>A refusal reason the removal service did not recognise.</summary>
        Unrecognised
    }

    /// <summary>Pure mapping from a removal refusal to the skip category it counts under (R22, R31).</summary>
    public static class RefusalMapping
    {
        public static SkipCategory ToSkipCategory(RemovalRefusalStage stage) => stage switch
        {
            RemovalRefusalStage.Property => SkipCategory.Property,
            RemovalRefusalStage.SettlementLaw => SkipCategory.SettlementLaw,
            RemovalRefusalStage.Pretest => SkipCategory.Obstructed,
            _ => SkipCategory.Other
        };
    }

    /// <summary>
    /// Owns what a mining job has accomplished and why it stopped (KTD6, KTD10): the
    /// per-plot worked/skipped ledger, plot selection, and status -- one source the
    /// panel (U9) and the strategy (U13) both read rather than two. Zero dependency on
    /// any Eco.* namespace.
    /// </summary>
    /// <remarks>
    /// Carries no travelling or returning state -- see <see cref="MiningJobStatus"/>.
    /// Completion (R17) is judged against which of this job's plots are currently
    /// surveyed, not against the fixed plot set alone: an area can have plots the
    /// survey drone has never reached, and those must not block completion or offer
    /// themselves as work (R8, AE6). Every method that decides "is there anything left
    /// to do" therefore takes an <c>isSurveyed</c> predicate rather than baking survey
    /// state into the ledger -- that state lives on the area entry (U8/KTD12), and this
    /// type stays a pure consumer of it.
    /// </remarks>
    public sealed class MiningJob
    {
        private readonly Dictionary<PlotCoord, PlotOutcome> _ledger;
        private readonly Dictionary<PlotCoord, SkipCategory> _skipCategories = new Dictionary<PlotCoord, SkipCategory>();

        public MiningJobStatus Status { get; private set; } = MiningJobStatus.Idle;

        public MiningEndReason? EndReason { get; private set; }

        public MiningJob(IEnumerable<PlotCoord> areaPlots)
        {
            if (areaPlots == null)
                throw new ArgumentNullException(nameof(areaPlots));

            _ledger = areaPlots.Distinct().ToDictionary(p => p, _ => PlotOutcome.Unworked);
        }

        /// <summary>Idle -> Working. A stray call while not Idle is a no-op.</summary>
        public void Dispatch()
        {
            if (Status == MiningJobStatus.Idle)
                Status = MiningJobStatus.Working;
        }

        /// <summary>
        /// The next surveyed, unworked, unskipped plot to work, in raster order
        /// (matching the survey drone's own sweep order), or null when none remain.
        /// A plot recorded worked or skipped is never re-offered (R16), and an
        /// unsurveyed plot is never offered (R8).
        /// </summary>
        public PlotCoord? NextPlot(Func<PlotCoord, bool> isSurveyed)
        {
            if (isSurveyed == null)
                throw new ArgumentNullException(nameof(isSurveyed));

            return _ledger
                .Where(kv => kv.Value == PlotOutcome.Unworked && isSurveyed(kv.Key))
                .Select(kv => kv.Key)
                .OrderBy(p => p.Z).ThenBy(p => p.X)
                .Cast<PlotCoord?>()
                .FirstOrDefault();
        }

        /// <summary>Records <paramref name="plot"/> worked. Idempotent -- re-marking an already-worked plot does not inflate any count.</summary>
        public void MarkWorked(PlotCoord plot)
        {
            RequirePlot(plot);
            _ledger[plot] = PlotOutcome.Worked;
        }

        /// <summary>Records <paramref name="plot"/> abandoned under <paramref name="category"/> (R22).</summary>
        public void MarkSkipped(PlotCoord plot, SkipCategory category)
        {
            RequirePlot(plot);
            _ledger[plot] = PlotOutcome.Skipped;
            _skipCategories[plot] = category;
        }

        private void RequirePlot(PlotCoord plot)
        {
            if (!_ledger.ContainsKey(plot))
                throw new ArgumentOutOfRangeException(nameof(plot), "Plot is not part of this job's area.");
        }

        /// <summary>
        /// If every surveyed plot has been worked or skipped, transitions Working ->
        /// Complete and returns true (R17) -- including when every plot was skipped
        /// (AE4), which reads complete rather than still running. A no-op returning
        /// false while not Working or while surveyed work remains.
        /// </summary>
        public bool TryComplete(Func<PlotCoord, bool> isSurveyed)
        {
            if (isSurveyed == null)
                throw new ArgumentNullException(nameof(isSurveyed));
            if (Status != MiningJobStatus.Working)
                return false;

            bool anySurveyedUnworked = _ledger.Any(kv => kv.Value == PlotOutcome.Unworked && isSurveyed(kv.Key));
            if (anySurveyedUnworked)
                return false;

            Status = MiningJobStatus.Complete;
            return true;
        }

        /// <summary>Working -> WaitingToUnload: arrived home, linked storage refused the load (R27).</summary>
        public void OnUnloadRefused()
        {
            if (Status == MiningJobStatus.Working)
                Status = MiningJobStatus.WaitingToUnload;
        }

        /// <summary>WaitingToUnload -> Working: storage freed, the hold emptied, and the drone is dispatched back out (R24).</summary>
        public void OnUnloadSucceeded()
        {
            if (Status == MiningJobStatus.WaitingToUnload)
                Status = MiningJobStatus.Working;
        }

        /// <summary>
        /// Working or WaitingToUnload -> Ended, carrying <paramref name="reason"/>. The
        /// ledger is preserved untouched -- ending is not completion, and the worked/
        /// skipped record stays legible after the fact.
        /// </summary>
        public void End(MiningEndReason reason)
        {
            if (Status == MiningJobStatus.Working || Status == MiningJobStatus.WaitingToUnload)
            {
                Status = MiningJobStatus.Ended;
                EndReason = reason;
            }
        }

        public PlotOutcome OutcomeOf(PlotCoord plot) =>
            _ledger.TryGetValue(plot, out var outcome) ? outcome : PlotOutcome.Unworked;

        public int WorkedCount => _ledger.Count(kv => kv.Value == PlotOutcome.Worked);

        public int SkippedCount => _ledger.Count(kv => kv.Value == PlotOutcome.Skipped);

        /// <summary>Skipped-plot count per category (R31). Every category not yet hit reads zero, never absent.</summary>
        public IReadOnlyDictionary<SkipCategory, int> SkipCountsByCategory()
        {
            var counts = Enum.GetValues(typeof(SkipCategory)).Cast<SkipCategory>().ToDictionary(c => c, _ => 0);
            foreach (var category in _skipCategories.Values)
                counts[category]++;
            return counts;
        }
    }
}
