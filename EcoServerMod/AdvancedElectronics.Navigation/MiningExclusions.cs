using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Whose fact an exclusion is, and therefore whom it binds (R18).
    /// </summary>
    public enum ExclusionReach
    {
        /// <summary>
        /// A fact about ONE DOCK'S ATTEMPT. It suppresses the plot for the dock that recorded
        /// it and for nobody else, so one dock's missing permit or failed route never deletes
        /// ore another dock could take (R19).
        /// </summary>
        Attempt,

        /// <summary>
        /// A fact about the GROUND. It suppresses the plot for every dock, because what it
        /// says is true whoever asks. The mod records exactly one: the at-bedrock observation
        /// a survey pass writes per column onto the area (U4, KTD4).
        /// </summary>
        Ground
    }

    /// <summary>
    /// One plot a mining pass could not take, recorded against the dock that hit the refusal
    /// (R18, KTD5): the plot, the category the pass already filed it under, the engine's own
    /// refusal wording, and the plot's surveyed stamp at the moment of the refusal.
    ///
    /// The vocabulary is <see cref="SkipCategory"/>'s, unchanged — the exclusion IS the mining
    /// job's skipped ledger entry, persisted past the job's end, and no second refusal
    /// vocabulary exists (KTD5).
    ///
    /// The stamp is what makes the exclusion a record of the LAST ATTEMPT rather than a
    /// permanent verdict: a survey pass that postdates it and still finds material at the plot
    /// lifts it (R19, AE15). See <see cref="MiningExclusionLedger.LiftWhereSurveyObservedMaterial"/>.
    /// </summary>
    public readonly struct MiningExclusion : IEquatable<MiningExclusion>
    {
        /// <summary>The dock that hit the refusal — the only dock this exclusion binds.</summary>
        public string DockId { get; }

        public PlotCoord Plot { get; }

        public SkipCategory Category { get; }

        /// <summary>The engine's own words for the refusal (R27), or null when the pass had none — an unreachable plot is refused by nobody.</summary>
        public string Detail { get; }

        /// <summary>The plot's surveyed stamp when the refusal happened. A later stamp is what a lift is measured against.</summary>
        public long SurveyedStamp { get; }

        public MiningExclusion(string dockId, PlotCoord plot, SkipCategory category, string detail, long surveyedStamp)
        {
            this.DockId = dockId;
            this.Plot = plot;
            this.Category = category;
            this.Detail = detail;
            this.SurveyedStamp = surveyedStamp;
        }

        /// <summary>
        /// Every <see cref="SkipCategory"/> a mining pass can record is an ATTEMPT fact — a
        /// mining pass records no ground facts at all (R18).
        ///
        /// This is deliberately total rather than a switch with cases, and the test that pins
        /// it walks the whole enum, because the tempting mistake is to sort the categories by
        /// reach and file <see cref="SkipCategory.Obstructed"/> as a fact about the ground.
        /// It is not. Obstructed maps from <see cref="RemovalRefusalStage.Pretest"/>, the
        /// classifier's catch-all for a refusal that was neither law nor property, which is
        /// exactly R7's single-column obstruction — a built block or a tree's dirt — and R7
        /// says such an obstruction never stops a plot from reaching bedrock. Bedrock itself
        /// never reaches this ledger: <c>MiningStrategy</c> filters
        /// <c>BlockClassification.NotRemovable</c> positions out BEFORE submitting a layer and
        /// advances past the layer without recording a skip.
        ///
        /// The one ground fact in the system is the area's at-bedrock observation (U4), which
        /// this ledger holds as <see cref="MiningExclusionLedger.GroundFacts"/> — a plain plot
        /// set with no holder and no category, because no dock's attempt produced it.
        /// </summary>
        public static ExclusionReach ReachOf(SkipCategory category) => ExclusionReach.Attempt;

        /// <inheritdoc cref="ReachOf(SkipCategory)"/>
        public ExclusionReach Reach => ReachOf(this.Category);

        public bool Equals(MiningExclusion other) =>
            this.DockId == other.DockId && this.Plot.Equals(other.Plot)
            && this.Category == other.Category && this.Detail == other.Detail
            && this.SurveyedStamp == other.SurveyedStamp;

        public override bool Equals(object obj) => obj is MiningExclusion other && this.Equals(other);

        public override int GetHashCode() => (this.DockId, this.Plot, (int)this.Category, this.SurveyedStamp).GetHashCode();

        public override string ToString() =>
            $"dock {this.DockId} plot ({this.Plot.X},{this.Plot.Z}) {this.Category}";
    }

    /// <summary>
    /// What is excluded from one area, and whom each exclusion binds (R18, R19, R27, KTD12).
    ///
    /// Holds two different kinds of record and keeps them apart on purpose:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="GroundFacts"/> — the plots the area's last survey observed down at bedrock
    /// (U4). Holderless, and they suppress for every dock.
    /// </description></item>
    /// <item><description>
    /// <see cref="AttemptFacts"/> — what a mining pass was refused, per dock. They suppress
    /// only for the dock that recorded them.
    /// </description></item>
    /// </list>
    ///
    /// That split governs what a dock is OFFERED (<see cref="SuppressesFor"/>), never what the
    /// area reads: the status derivation (U5) reads <see cref="AttemptFacts"/> whole, with no
    /// filter on holder, so one area has one status every dock agrees on (R26). This type
    /// deliberately derives no status — it answers the two questions U5's derivation asks.
    ///
    /// Eco-free by construction, so the whole reach rule is unit-testable (KTD12): the Eco-side
    /// dock builds one of these from its persisted entries plus the area's bedrock plots.
    /// </summary>
    public sealed class MiningExclusionLedger
    {
        private readonly List<MiningExclusion> attempts = new List<MiningExclusion>();
        private readonly HashSet<PlotCoord> ground = new HashSet<PlotCoord>();

        /// <summary>Records the area's at-bedrock observation for <paramref name="plot"/> (U4). Idempotent.</summary>
        public void RecordGroundFact(PlotCoord plot) => this.ground.Add(plot);

        /// <inheritdoc cref="RecordGroundFact(PlotCoord)"/>
        public void RecordGroundFacts(IEnumerable<PlotCoord> plots)
        {
            if (plots == null) return;
            foreach (var plot in plots)
                this.ground.Add(plot);
        }

        /// <summary>
        /// Records one dock's refusal. The latest record for a (dock, plot) pair replaces any
        /// earlier one — an exclusion describes the LAST attempt, so a second refusal at the
        /// same plot restates it with a fresh stamp rather than stacking a duplicate.
        /// </summary>
        public void Record(MiningExclusion exclusion)
        {
            this.attempts.RemoveAll(e => e.DockId == exclusion.DockId && e.Plot.Equals(exclusion.Plot));
            this.attempts.Add(exclusion);
        }

        /// <summary>
        /// Every attempt fact, whoever recorded it — the read U5's status derivation makes
        /// (R26) and the one R27's reason line is worded from. Never filtered by holder.
        /// </summary>
        public IReadOnlyList<MiningExclusion> AttemptFacts => this.attempts;

        /// <summary>The plots the ground itself excludes: this area's at-bedrock observations (U4).</summary>
        public IReadOnlyCollection<PlotCoord> GroundFacts => this.ground;

        /// <summary>The attempt facts <paramref name="dockId"/> recorded — its own knowledge, binding nobody else.</summary>
        public IEnumerable<MiningExclusion> RecordedBy(string dockId) =>
            this.attempts.Where(e => e.DockId == dockId);

        /// <summary>
        /// True when some dock's refusal accounts for material still standing at
        /// <paramref name="plot"/>. This is the question that separates R7's `[cleared]` from
        /// R26's `[empty]` — blocked ground never reads as spent ground — and it is asked of
        /// the whole ledger, not of one dock's share of it.
        /// </summary>
        public bool IsAccountedForByAttempt(PlotCoord plot) =>
            this.attempts.Any(e => e.Plot.Equals(plot));

        /// <summary>
        /// Whether <paramref name="plot"/> is withheld from <paramref name="dockId"/>'s offers:
        /// the union of the area's ground facts and THIS dock's own attempt facts (R19).
        /// </summary>
        public bool SuppressesFor(string dockId, PlotCoord plot) =>
            this.ground.Contains(plot) || this.attempts.Any(e => e.DockId == dockId && e.Plot.Equals(plot));

        /// <summary>The plots <paramref name="dockId"/> is not offered, out of <paramref name="areaPlots"/>.</summary>
        public IEnumerable<PlotCoord> SuppressedFor(string dockId, IEnumerable<PlotCoord> areaPlots) =>
            areaPlots.Where(p => this.SuppressesFor(dockId, p));

        /// <summary>
        /// Drops every attempt fact a later survey pass has contradicted, and returns the ones
        /// dropped (R19, AE15).
        ///
        /// An exclusion is a record of the last attempt, not a permanent verdict, and the
        /// survey is the only thing that can ever lift one: a `[cleared]` area is offered to no
        /// mining dock (R44), so a mining pass could never be the lifter. Two conditions, both
        /// required:
        /// <list type="number">
        /// <item><description>
        /// The pass POSTDATES the refusal — <paramref name="surveyedStampFor"/> returns a stamp
        /// later than the one the exclusion carries. Without this an exclusion would be lifted
        /// by the very pass that preceded it and would suppress nothing.
        /// </description></item>
        /// <item><description>
        /// The pass observed MINEABLE MATERIAL there — the plot is not down at bedrock. A plot
        /// at bedrock has nothing left to take, so there is nothing for the exclusion to be
        /// wrong about, and the ground fact answers for it anyway.
        /// </description></item>
        /// </list>
        ///
        /// Lifting is not scoped to a holder, because the observation is not: the survey sees
        /// ground, not attempts, and cannot tell whose refusal it contradicts. What it drops,
        /// it drops for every dock that recorded it.
        /// </summary>
        public IReadOnlyList<MiningExclusion> LiftWhereSurveyObservedMaterial(
            Func<PlotCoord, long> surveyedStampFor, Func<PlotCoord, bool> restsOnBedrock)
        {
            if (surveyedStampFor == null) throw new ArgumentNullException(nameof(surveyedStampFor));
            if (restsOnBedrock == null) throw new ArgumentNullException(nameof(restsOnBedrock));

            var lifted = this.attempts
                .Where(e => surveyedStampFor(e.Plot) > e.SurveyedStamp && !restsOnBedrock(e.Plot))
                .ToList();

            foreach (var exclusion in lifted)
                this.attempts.Remove(exclusion);

            return lifted;
        }

        /// <summary>True when this area excludes nothing at all, from anyone.</summary>
        public bool IsEmpty => this.attempts.Count == 0 && this.ground.Count == 0;
    }
}
