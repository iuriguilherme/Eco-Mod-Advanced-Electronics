using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// <b>One assignment a load-time pass has to undo, and everything the undoing has to say
    /// (R13, R15).</b>
    ///
    /// <para>
    /// The area is carried as an <see cref="AreaProjection"/> and not as anything richer, for the
    /// reason R41 gives at every other boundary in this path: the pair
    /// (<see cref="AreaProjection.OwningDockId"/>, <see cref="AreaProjection.AreaId"/>) is enough
    /// for the Eco side to find the entry it owns, and nothing else about another player's area
    /// needs to cross. The same is true of <see cref="Holder"/>, which is the blocking area as
    /// the projection limits it.
    /// </para>
    /// <para>
    /// <see cref="Conflicts"/> is the whole finding and the two properties beside it are the
    /// reading of it that the record on the area can actually hold: ONE reason and ONE list of
    /// plots, because <c>SurveyAreaEntry.RecordReconciliationBlock</c> stores one of each.
    /// </para>
    /// </summary>
    public readonly struct AreaToUnassign
    {
        /// <summary>The area whose assignment is undone — the offender, by KTD10's rule.</summary>
        public AreaProjection Area { get; }

        /// <summary>
        /// Every conflict this area is answerable for, in the order the overlap scan found them
        /// and after the offender rule has dropped the ones the OTHER side answers for.
        /// </summary>
        public IReadOnlyList<AreaClaimConflict> Conflicts { get; }

        public AreaToUnassign(AreaProjection area, IReadOnlyList<AreaClaimConflict> conflicts)
        {
            Area = area;
            Conflicts = conflicts ?? Array.Empty<AreaClaimConflict>();
        }

        /// <summary>
        /// The one block to record, farmland first when an area is blocked by both.
        ///
        /// <para>
        /// The order is not a preference between two equal answers: the two members are lifted by
        /// DIFFERENT acts (<c>MiningReadout.FormatClaimRefusal</c> words each by the act that
        /// lifts it), and the one still standing after the player has done what the other asks
        /// for is the one worth telling them about. An assignment can be unassigned; farmland is
        /// released only by its owner deleting the farming area.
        /// </para>
        /// </summary>
        public AreaClaimBlock Reason =>
            this.Conflicts != null && this.Conflicts.Any(c => c.Reason == AreaClaimBlock.FarmlandReserved)
                ? AreaClaimBlock.FarmlandReserved
                : AreaClaimBlock.HeldByAssignment;

        /// <summary>
        /// The area to name as holding the ground: the first one blocking for
        /// <see cref="Reason"/>, or null when there is nothing to name.
        ///
        /// <para>
        /// One holder rather than all of them, because the record R15 leaves behind names one.
        /// Naming the holder of the reason actually reported is what keeps the two halves of that
        /// record from describing two different areas.
        /// </para>
        /// </summary>
        public AreaProjection Holder
        {
            get
            {
                if (this.Conflicts == null) return null;

                // Copied out before the lambda: a lambda inside a struct may not touch `this`.
                var reason = this.Reason;
                return this.Conflicts.FirstOrDefault(c => c.Reason == reason).Holder;
            }
        }

        /// <summary>
        /// Every plot in dispute, across every conflict, in raster order and without repeats.
        ///
        /// <para>
        /// ALL of the contested ground and not just the reported holder's share: a player sent to
        /// one of two contested patches would go, look, clear it, and still be blocked by the
        /// other. The ordering is the one every other plot list in the mod is read down in, and
        /// it is applied here rather than left to the caller because an unordered set walked out
        /// of a hash table renders differently on each refresh.
        /// </para>
        /// </summary>
        public IReadOnlyList<PlotCoord> ContestedPlots =>
            this.Conflicts == null
                ? Array.Empty<PlotCoord>()
                : this.Conflicts
                    .SelectMany(c => c.Plots)
                    .Distinct()
                    .OrderBy(p => p.X)
                    .ThenBy(p => p.Z)
                    .ToArray();
    }

    /// <summary>
    /// <b>Which assignments a save is not entitled to keep (U5, R13, R16).</b> The whole of the
    /// load-time decision, and pure: projections in, the assignments to undo out, no Eco type
    /// anywhere near it and nothing written.
    ///
    /// <para>
    /// <b>It restates no rule.</b> Who may hold which ground is <see cref="AreaClaims.Conflicts"/>
    /// and stays there — reconciliation is the assignment-time test (R1 through R8, R7a included)
    /// applied to a save written before that test existed, so a second copy of those rules here
    /// would be a second thing to keep in step with the first, and the drift would show up as a
    /// load that quietly undoes assignments the assignment path would have allowed.
    /// </para>
    /// <para>
    /// <b>What it does add is the offender rule (KTD10),</b> and it has to.
    /// <see cref="AreaClaims.Conflicts"/> is a ONE-SIDED query: asked from the mine it reports
    /// the farmland reservation, asked from the farm it reports the mine's assignment holding the
    /// same plots. Every collision therefore answers twice, and undoing both sides would punish
    /// two players for one overlap. So exactly one side is the offender:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Mining versus farming: the mining side, always.</b> R3 and R4 already say the farm is
    /// entitled to the ground — its reservation outlives its own assignment, and the one-way
    /// asymmetry lets a farm take ground a mine has finished with and never the reverse. A farm
    /// is never undone by a cross-kind collision.
    /// </description></item>
    /// <item><description>
    /// <b>Same kind, two docks: the higher (owning dock id, area id) pair.</b> No requirement
    /// settles this one, so the rule is a total order and any total order would do. The POINT is
    /// that it is an order at all: <c>IWorldObjectManager.All</c> has none, so without this the
    /// same save could undo dock A on one load and dock B on the next.
    /// </description></item>
    /// </list>
    /// <para>
    /// Same-dock pairs never reach either branch — <see cref="AreaClaims.Conflicts"/> drops them
    /// ahead of the farmland test (R7a) — so this inherits that exemption rather than repeating
    /// it.
    /// </para>
    /// <para>
    /// <b>The result is a fixed point (R16).</b> Undoing the chosen side leaves a world that
    /// reconciles clean on the next pass, which is what makes "once per load" enough. Undoing the
    /// other side would not: a farm undone in favour of a mine would leave that mine standing on
    /// farmland, an R3 violation the next load would have to undo as well.
    /// </para>
    /// </summary>
    public static class AreaReconciliation
    {
        /// <summary>
        /// The assignments this world is not entitled to keep, in a fixed order, or an empty list
        /// when every claim is legitimate.
        ///
        /// <para>
        /// <b>Only a HELD assignment can be undone.</b> R13 speaks of an area holding plots it is
        /// not entitled to, and an area holding nothing holds no plots — so an unassigned area is
        /// never returned, and neither is a mining area whose ground reads <c>[empty]</c>, whose
        /// claim <see cref="AreaClaims.HoldsClaim"/> has already dropped before the projection
        /// was built. That is R4's exception arriving here for free rather than being re-tested:
        /// a farm standing on spent mining ground costs this pass nothing at all.
        /// </para>
        /// <para>
        /// One pass over the world, one overlap scan per held area, and no writes when nothing
        /// conflicts (R16).
        /// </para>
        /// </summary>
        /// <param name="published">
        /// The RAW projection set, unfiltered by owner and by radius (KTD8) — what
        /// <c>MiningComponent.AllAreaProjections()</c> builds. Nulls are passed over rather than
        /// thrown on: this runs at world load, where a throw costs the load and leaves the player
        /// no way to reach a dock and repair anything.
        /// </param>
        public static IReadOnlyList<AreaToUnassign> AssignmentsToUndo(IEnumerable<AreaProjection> published)
        {
            if (published == null) return Array.Empty<AreaToUnassign>();

            // Materialised once: every held area is scanned against the whole set, and the set is
            // walked once per scan.
            var world = published.Where(p => p != null).ToList();
            if (world.Count == 0) return Array.Empty<AreaToUnassign>();

            var undo = new List<AreaToUnassign>();

            // Ordered by the same pair the offender rule compares, so the RESULT is stable too --
            // the Eco side writes a log line per undone assignment, and a list that shuffles
            // between loads reads as a different set of decisions.
            foreach (var area in world.Where(a => a.HoldsClaim).OrderBy(a => a.OwningDockId).ThenBy(a => a.AreaId))
            {
                // The area's own kind is the claimant kind, exactly as the survey assignment path
                // passes it (R30): a pass serves whatever the area is for. A mining area asks as
                // mining and meets R47's farmland branch; a farming area asks as farming and does
                // not, which is the whole of the asymmetry.
                var conflicts = AreaClaims
                    .Conflicts(area.Kind, AreaOverlap.Matches(area, world))
                    .Where(c => IsOffender(area, c.Holder))
                    .ToArray();

                if (conflicts.Length == 0) continue;

                undo.Add(new AreaToUnassign(area, conflicts));
            }

            return undo;
        }

        /// <summary>
        /// Whether <paramref name="claimant"/> is the side of this collision that gives way
        /// (KTD10). See the type's own summary for why each branch reads as it does.
        /// </summary>
        private static bool IsOffender(AreaProjection claimant, AreaProjection holder)
        {
            // A conflict with nothing named cannot be attributed to either side, so it is not
            // acted on. AreaClaims never produces one; this is the load-time posture again.
            if (claimant == null || holder == null) return false;

            // Cross-kind: the mine gives way and the farm never does.
            if (claimant.Kind != holder.Kind) return claimant.Kind == AreaKind.Mining;

            // Same kind: a total order over the identity. The dock first, then the area, which is
            // the same pair that identifies an area everywhere else in this path. The area half
            // is unreachable today -- two areas on one dock are exempt (R7a) and never get here --
            // and it stays so the comparison is total rather than nearly so.
            var byDock = claimant.OwningDockId.CompareTo(holder.OwningDockId);
            return byDock != 0 ? byDock > 0 : claimant.AreaId > holder.AreaId;
        }
    }
}
