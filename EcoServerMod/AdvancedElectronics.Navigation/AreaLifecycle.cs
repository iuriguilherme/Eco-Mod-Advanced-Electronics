using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// The one lifecycle status an area shows (R3, R4). Exactly one holds at a time, and it
    /// belongs to the area rather than to whichever dock is looking at it.
    ///
    /// <para>
    /// The order of the members is the ramp's own order, from "nothing known" to "nothing
    /// left". <c>[landfill]</c> and <c>[filled]</c> are reserved names in this vocabulary
    /// (R4) and are deliberately absent: nothing here builds them.
    /// </para>
    /// <para>
    /// Colours belong to the render (R4), not to this enum, and the overlays <c>[assigned]</c>,
    /// <c>[unreachable]</c> and <c>[overlap]</c> are not statuses at all (R9) -- they describe
    /// a drone's current trip or two areas' geometry, and any of them may appear beside any
    /// status.
    /// </para>
    /// </summary>
    public enum AreaLifecycleStatus
    {
        /// <summary>No survey describes this area's ground. Rendered uncoloured (R4).</summary>
        Unsurveyed,

        /// <summary>Every plot the survey found is still untouched. Green.</summary>
        Surveyed,

        /// <summary>Some plots have been mined since their survey and others are still mineable. Magenta.</summary>
        Digging,

        /// <summary>Nothing is mineable and at least one plot has been mined since its survey: resurvey it. Yellow.</summary>
        Mined,

        /// <summary>The drone is finished, but a recorded refusal accounts for material still standing (R7, R27). Red.</summary>
        Cleared,

        /// <summary>Every plot is down at bedrock and no exclusion accounts for anything left: the ground is spent (R26). Grey.</summary>
        Empty,

        /// <summary>
        /// <b>Farmland. Not a rung of the ramp above</b> — it is what the area IS, and it occupies
        /// the same exclusive slot the rungs do (R30). An area shows the status belonging to what
        /// it is for: a mining area runs the ladder and reads its result, a farming area reads
        /// this and the ladder is never consulted.
        ///
        /// <para>
        /// It sits in this enum rather than beside it precisely so the two can never co-occur:
        /// the slot holds one value of one type, so "a mining status AND [farm]" is not a state
        /// the render can be handed. <see cref="AreaLifecycle.StatusFor"/> is where the kind
        /// chooses, and <see cref="AreaLifecycle.DeriveStatus"/> can never return this.
        /// </para>
        /// </summary>
        Farm
    }

    /// <summary>
    /// What an area is for (R30) — a property of the area itself, not of the dock looking at it.
    ///
    /// <para>
    /// Kind is what selects the status slot's value, which is why it belongs in this assembly
    /// rather than only on the persisted entry. The serialized member and the change action are
    /// a later unit's; this is the pure half the derivation needs.
    /// </para>
    /// </summary>
    public enum AreaKind
    {
        /// <summary>The default, and what every area in an existing save is.</summary>
        Mining = 0,

        /// <summary>Ground a farming drone works. Reads <see cref="AreaLifecycleStatus.Farm"/>.</summary>
        Farming = 1
    }

    /// <summary>
    /// Derives an area's one lifecycle status from plain values (R3, KTD3, KTD12).
    ///
    /// <para>
    /// <b>Computed on read, never stored.</b> Storing the status would add a value every writer
    /// has to invalidate, and <c>[cleared]</c> is derived by definition under R8 -- a survey
    /// that finds ground standing above bedrock restates the ramp on its own, with nothing to
    /// clear. Both the roster render and the assignment path call
    /// <see cref="DeriveStatus"/>, which is what keeps R44's offer test and R3's display from
    /// drifting apart; <see cref="IsOfferableToMiningDock"/> is the offer half, and it reads
    /// this same enum rather than re-deriving anything.
    /// </para>
    /// <para>
    /// Eco-free by construction (KTD12): no Eco type crosses this boundary, so the whole ladder
    /// is unit-testable without a server. The Eco side reads world state, calls this, and
    /// renders or offers the result.
    /// </para>
    /// </summary>
    public static class AreaLifecycle
    {
        /// <summary>
        /// <b>The one status an area shows, chosen by its KIND before anything is derived (R30).</b>
        ///
        /// <para>
        /// This is an invariant, not a precedence rule: a mining status and <c>[farm]</c> never
        /// co-occur because they are never both produced. The ladder arrives as a DELEGATE and is
        /// simply not invoked for farmland — there is no moment at which both values exist and
        /// only the render order keeps them apart. Computing the mining status first and then
        /// overwriting it would leave exactly that moment, and a later reader would be free to
        /// render the wrong half of it.
        /// </para>
        /// <para>
        /// It reads as belt-and-braces today, because <c>FarmAreaEntry</c> and
        /// <c>SurveyAreaEntry</c> are separate types and nothing can carry both sets of inputs.
        /// That separation goes away when kind moves onto the survey area and an exhausted mining
        /// area becomes farmland: such an area keeps its mined stamps and its bedrock
        /// observations, all still truthful about its past, so every input the ladder reads is
        /// still sitting there waiting to be read. The gate is what stops it being asked.
        /// </para>
        /// </summary>
        /// <param name="miningLadder">
        /// How to derive the mining status, evaluated only for <see cref="AreaKind.Mining"/>.
        /// Null is a caller bug for a mining area and is refused; for farmland it is the ordinary
        /// case, because farmland has no ladder to run.
        /// </param>
        public static AreaLifecycleStatus StatusFor(AreaKind kind, Func<AreaLifecycleStatus> miningLadder)
        {
            if (kind == AreaKind.Farming) return AreaLifecycleStatus.Farm;

            if (miningLadder == null) throw new ArgumentNullException(nameof(miningLadder));
            return miningLadder();
        }

        /// <summary>
        /// Whether a status is a rung of the mining ladder — something
        /// <see cref="DeriveStatus"/> can actually return.
        ///
        /// <para>
        /// A status added later counts as a rung by default, which is the direction that fails
        /// loudly: the ladder's totality test then reports it as unreachable and whoever added it
        /// has to say which slot it belongs to, rather than quietly escaping the invariant.
        /// </para>
        /// </summary>
        public static bool IsMiningRung(AreaLifecycleStatus status) => status != AreaLifecycleStatus.Farm;

        /// <summary>The ladder's own vocabulary, so callers and tests need no hand-written list.</summary>
        public static IReadOnlyList<AreaLifecycleStatus> MiningRamp { get; } =
            Enum.GetValues(typeof(AreaLifecycleStatus))
                .Cast<AreaLifecycleStatus>()
                .Where(IsMiningRung)
                .ToList();

        /// <summary>
        /// The one status for an area (R3). The ladder runs once and returns the first status
        /// that holds, in this order:
        /// <list type="number">
        /// <item><description>any plot unsurveyed -> <see cref="AreaLifecycleStatus.Unsurveyed"/>;</description></item>
        /// <item><description>every plot finished -> <see cref="AreaLifecycleStatus.Cleared"/> when an
        /// exclusion accounts for any of it, otherwise <see cref="AreaLifecycleStatus.Empty"/>;</description></item>
        /// <item><description>otherwise the dominant status of the plots that remain:
        /// <see cref="AreaLifecycleStatus.Surveyed"/>, <see cref="AreaLifecycleStatus.Digging"/>,
        /// or <see cref="AreaLifecycleStatus.Mined"/>.</description></item>
        /// </list>
        ///
        /// <para>
        /// <b>The unsurveyed guard comes first, and the order is load-bearing.</b> An area
        /// holding ANY unsurveyed plot reads unsurveyed whatever its other plots say (R3) --
        /// a plot an edit added, or one an outside change reset, and a resurvey is what
        /// returns the whole area to <c>[surveyed]</c>. Test it later and the ladder answers
        /// <c>[mined]</c> for a partly-edited area: an unsurveyed plot has both stamps at 0 so
        /// it is not mineable, and <c>[mined]</c>'s own test is that no plot is mineable --
        /// the very thing that makes the added plot need a pass is what would satisfy the
        /// finished status. Nine mined-out plots plus one freshly added plot read UNSURVEYED.
        /// The guard outranks the bedrock branch too: an unobserved plot proves nothing about
        /// the floor.
        /// </para>
        /// <para>
        /// <b>A plot is finished when it is at bedrock OR an exclusion accounts for it</b>
        /// (R7): the drone is done with it either way. That is why a plot refused under
        /// settlement law, with its material still standing, does not hold the area out of
        /// <c>[cleared]</c> -- and why <c>[cleared]</c> and <c>[empty]</c> are then told apart
        /// by whether any exclusion was involved at all. <c>[cleared]</c> says only that the
        /// drone is finished, which may be because it was refused; <c>[empty]</c> says the
        /// ground itself is exhausted (R26). Blocked ground never reads as spent ground.
        /// </para>
        /// </summary>
        /// <param name="plots">Every plot the area covers. An area with no plots has no surveyed plot, so it reads <see cref="AreaLifecycleStatus.Unsurveyed"/>.</param>
        /// <param name="surveyedStamp">The area's surveyed stamp for a plot; 0 means "never surveyed", which is what makes the plot unsurveyed.</param>
        /// <param name="minedStamp">The area's mined stamp for a plot; 0 means "never mined". Mineability is <see cref="PlotFreshness.IsMineable"/>, unchanged.</param>
        /// <param name="assembledExclusions">
        /// <b>The union of every exclusion recorded against this area, from every source</b> --
        /// the area's own at-bedrock observations as
        /// <see cref="MiningExclusionLedger.GroundFacts"/> (U4, KTD4) AND the attempt facts of
        /// EVERY mining dock, not only the dock doing the reading (R26). The caller assembles
        /// it over the dock enumeration KTD8 defines; <see cref="AssembleExclusions"/> is that
        /// union in one call.
        /// <para>
        /// <b>Passing only the reading dock's exclusions is the bug this parameter exists to
        /// prevent.</b> Do it and the same area reads <c>[mined]</c> to one dock and
        /// <c>[cleared]</c> to another, which is exactly the cross-dock disagreement R26
        /// removes. An exclusion is shared information even though the OFFER it drives is not:
        /// <see cref="MiningExclusionLedger.SuppressesFor"/> is the per-dock offer read and is
        /// deliberately NOT what this ladder uses. What varies per dock is which plots it is
        /// offered, never what the area says it is.
        /// </para>
        /// <para>
        /// Never null: reading a missing ledger as "nothing excluded" would silently turn a
        /// <c>[cleared]</c> area into <c>[empty]</c>, and that is precisely the distinction R26
        /// asks for.
        /// </para>
        /// </param>
        /// <param name="needsReReading">
        /// Whether a plot's ground changed after the survey read it, so what is recorded for it can
        /// no longer be trusted (R6). Optional: null means nothing is marked, which is what lets
        /// every caller written before this input existed behave exactly as it did.
        ///
        /// <para>
        /// This is the DRONES' answer and only theirs. A marked plot counts as not yet read, so a
        /// survey drone reads it again and a mining drone does not work it. What the PLAYER sees
        /// does not change: the area goes on displaying the status tag it last earned, and the
        /// readout says its figures are out of date rather than the tag saying so. Both answers
        /// hold at once, and neither one is the whole rule.
        /// </para>
        /// </param>
        public static AreaLifecycleStatus DeriveStatus(
            IEnumerable<PlotCoord> plots,
            Func<PlotCoord, long> surveyedStamp,
            Func<PlotCoord, long> minedStamp,
            MiningExclusionLedger assembledExclusions,
            Func<PlotCoord, bool> needsReReading = null)
        {
            if (plots == null) throw new ArgumentNullException(nameof(plots));
            if (surveyedStamp == null) throw new ArgumentNullException(nameof(surveyedStamp));
            if (minedStamp == null) throw new ArgumentNullException(nameof(minedStamp));
            if (assembledExclusions == null) throw new ArgumentNullException(nameof(assembledExclusions));

            // Snapshotted into a set so the per-plot floor test is a hash lookup: GroundFacts is
            // exposed as a read-only collection, whose Contains would otherwise be a linear scan.
            var bedrock = new HashSet<PlotCoord>(assembledExclusions.GroundFacts);

            var anyPlot = false;
            var allFinished = true;
            var anyExclusionAccountsForWhatIsLeft = false;
            var anyMineable = false;
            var anyMinedSinceSurvey = false;

            foreach (var plot in plots)
            {
                anyPlot = true;

                // 1. The unsurveyed guard (R3, R6). Whatever the rest of the area says, this wins.
                //
                //    Two ways a plot fails it, and they mean the same thing to a drone. Either no
                //    survey ever read it, or its ground changed after a survey read it so what is
                //    recorded can no longer be trusted. In both cases the drones must treat the
                //    plot as not yet read, so both belong in this one guard rather than as a new
                //    rung further down the ladder -- a rung would be a status the player could see,
                //    and being marked for re-reading is deliberately not one.
                if (surveyedStamp(plot) <= 0 || needsReReading?.Invoke(plot) == true)
                    return AreaLifecycleStatus.Unsurveyed;

                // 2. Is the drone finished with this plot -- at bedrock, or excluded (R7)?
                var excluded = assembledExclusions.IsAccountedForByAttempt(plot);
                if (excluded) anyExclusionAccountsForWhatIsLeft = true;
                if (!excluded && !bedrock.Contains(plot)) allFinished = false;

                // 3. The ramp's own split. Past the guard every plot is one or the other:
                //    surveyedStamp > 0, so "not mineable" means minedStamp >= surveyedStamp > 0.
                if (PlotFreshness.IsMineable(surveyedStamp(plot), minedStamp(plot)))
                    anyMineable = true;
                else
                    anyMinedSinceSurvey = true;
            }

            if (!anyPlot)
                return AreaLifecycleStatus.Unsurveyed;

            if (allFinished)
                return anyExclusionAccountsForWhatIsLeft
                    ? AreaLifecycleStatus.Cleared
                    : AreaLifecycleStatus.Empty;

            if (!anyMinedSinceSurvey) return AreaLifecycleStatus.Surveyed;   // every plot untouched (R5)
            if (anyMineable) return AreaLifecycleStatus.Digging;             // some dug, some left
            return AreaLifecycleStatus.Mined;                                // none mineable, some dug (R6)
        }

        /// <summary>
        /// Whether a mining dock may be offered this area for assignment (R44).
        ///
        /// <para>
        /// <c>[cleared]</c> and <c>[empty]</c> are not offered: there is nothing a mining drone
        /// could take. Both stay visible on both tabs and stay assignable to a SURVEY dock,
        /// which is the only thing that can return them to the ramp -- a resurvey re-derives
        /// at-bedrock from the ground (R8), and assigning a mining dock again is what lifts
        /// that dock's attempt facts (R45).
        /// </para>
        /// <para>
        /// This reads the status <see cref="DeriveStatus"/> produced rather than re-deriving
        /// anything, so R44's offer test and R3's display cannot disagree (KTD3). A status the
        /// ramp gains later is offerable by default, which is the safe direction: a new status
        /// is only withheld once someone decides it should be.
        /// </para>
        /// </summary>
        public static bool IsOfferableToMiningDock(AreaLifecycleStatus status) =>
            status != AreaLifecycleStatus.Cleared && status != AreaLifecycleStatus.Empty;

        /// <summary>
        /// Builds the exclusion set <see cref="DeriveStatus"/> reads: the union of the area's
        /// own at-bedrock observations with the attempt facts of every dock that recorded one
        /// against it (R26, U5).
        ///
        /// <para>
        /// This exists so the union has one named source. The Eco side collects the attempt
        /// facts over the raw dock enumeration KTD8 defines -- every mining dock, not the
        /// reading one -- and takes the bedrock plots from the area's own
        /// <c>RestsOnBedrock</c> record (U4). Both the roster render and the assignment path
        /// call it, so both read the same set.
        /// </para>
        /// <para>
        /// A null on either side means "none recorded", which is an ordinary state for a fresh
        /// area. A null LEDGER at <see cref="DeriveStatus"/> is not the same thing and is
        /// refused there: it means the caller never assembled the set.
        /// </para>
        /// </summary>
        public static MiningExclusionLedger AssembleExclusions(
            IEnumerable<PlotCoord> bedrockPlots,
            IEnumerable<MiningExclusion> everyDockAttemptFacts)
        {
            var ledger = new MiningExclusionLedger();
            ledger.RecordGroundFacts(bedrockPlots);

            foreach (var exclusion in everyDockAttemptFacts ?? Enumerable.Empty<MiningExclusion>())
                ledger.Record(exclusion);

            return ledger;
        }
    }
}
