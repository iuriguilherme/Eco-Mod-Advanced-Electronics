using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// <b>A foreign area, reduced to the geometry the overlap path is allowed to know (R41).</b>
    ///
    /// <para>
    /// R41 makes the dock-to-dock channel internal: the system may see where two areas want the
    /// same ground, but a dock placed near another player's area grants its owner no sight of
    /// that area. The obvious way to honour that is discipline — "don't render the other entry"
    /// — repeated at every call site, which is a rule that holds only until someone adds a call
    /// site. This type is the mechanism instead. The geometry test and the detail formatter take
    /// projections and plain plot coordinates and nothing else, so an entry belonging to another
    /// owner cannot reach them to be leaked.
    /// </para>
    /// <para>
    /// What is here is the whole of it: the plots, the identity (which area, on which dock), the
    /// claim flag R36 needs to say whether the block will ever lift, and the kind R47's
    /// enforcement needs. What is NOT here is everything the persisted entry carries — findings,
    /// survey and mined stamps, bedrock observations, exclusions, the sweep cursor, and the
    /// area's name. Adding a field here widens what an overlap surface can disclose, so treat
    /// the field list as the boundary it is; <c>AreaOverlapTests</c> asserts it outright.
    /// </para>
    /// <para>
    /// Note what is missing besides: there is no dock position and no distance. R34 has overlap
    /// see past the dock-network radius, and the surest way to keep it that way is to build the
    /// projection with nothing a radius could be applied to.
    /// </para>
    /// </summary>
    public sealed class AreaProjection
    {
        /// <summary>The area's id, unique only within <see cref="OwningDockId"/>.</summary>
        public int AreaId { get; }

        /// <summary>The dock that published the area. With <see cref="AreaId"/>, the identity.</summary>
        public Guid OwningDockId { get; }

        /// <summary>The plots the area covers, on the world plot grid, copied at construction.</summary>
        public IReadOnlyList<PlotCoord> Plots { get; }

        /// <summary>
        /// Whether the area currently holds its plots against another dock (R36, R39). This is
        /// what tells a player whether an overlap is blocking anything right now: an unassigned
        /// area overlapping yours takes nothing from you, which is why AE12's freshly drawn
        /// mining area does not stop the farm working the ground they share.
        /// </summary>
        public bool HoldsClaim { get; }

        /// <summary>
        /// What the other area is for (R30) — carried for the ENFORCEMENT path, never for a
        /// render.
        ///
        /// <para>
        /// R47 is why it has to be here at all: ground a farming area covers is reserved against
        /// a mining dock whether or not that farm is currently assigned, so the assignment test
        /// cannot decide from <see cref="HoldsClaim"/> alone. It is deliberately absent from
        /// <see cref="AreaOverlap.FormatOverlapDetail(AreaOverlapMatch, int)"/>: R39's claim test
        /// is kind-blind, so naming the kind to the player would disclose something about
        /// another player's area while answering nothing about whether their own block lifts.
        /// "Not shown" and "not known" are two different things, and these are two different
        /// consumers.
        /// </para>
        /// </summary>
        public AreaKind Kind { get; }

        public AreaProjection(
            int areaId,
            Guid owningDockId,
            IEnumerable<PlotCoord> plots,
            bool holdsClaim = false,
            AreaKind kind = AreaKind.Mining)
        {
            AreaId = areaId;
            OwningDockId = owningDockId;
            // Copied, not aliased: the caller's source is the live area's plot list, and an edit
            // landing mid-render must not change the geometry a comparison already started on.
            Plots = plots == null ? Array.Empty<PlotCoord>() : plots.ToArray();
            HoldsClaim = holdsClaim;
            Kind = kind;
        }

        /// <summary>
        /// Whether two projections describe the same area. The PAIR, not the id: area ids are
        /// assigned per dock, so two docks both holding an area #1 is ordinary — comparing ids
        /// alone would read a real collision between them as an area overlapping itself.
        /// </summary>
        public bool IsSameAreaAs(AreaProjection other) =>
            other != null && other.AreaId == this.AreaId && other.OwningDockId == this.OwningDockId;
    }

    /// <summary>One collision: the other area as geometry, and the plots the two share.</summary>
    public readonly struct AreaOverlapMatch
    {
        /// <summary>The other area, as the projection R41 limits it to.</summary>
        public AreaProjection Other { get; }

        /// <summary>The plots both areas cover, ordered so a rendered detail is stable.</summary>
        public IReadOnlyList<PlotCoord> SharedPlots { get; }

        public AreaOverlapMatch(AreaProjection other, IReadOnlyList<PlotCoord> sharedPlots)
        {
            Other = other;
            SharedPlots = sharedPlots ?? Array.Empty<PlotCoord>();
        }

        public int SharedPlotCount => this.SharedPlots.Count;
    }

    /// <summary>
    /// <b>Where two areas want the same ground (R34, R35, R36).</b> Pure geometry over plot
    /// coordinates, per KTD12 — the Eco side does the world walk and builds the projections, and
    /// every decidable part of the rule is here where it can be tested without a server.
    ///
    /// <para>
    /// Two filters deliberately do not appear in any signature. There is no radius: R34 has
    /// overlap see past the dock network, because two areas collide on the ground however far
    /// apart the docks that drew them sit. And there is no owner: whoever owns them, the same
    /// plots are the same plots. Both are the caller's business only in that the caller must
    /// feed this the RAW enumeration (KTD8's <c>AllPublishedAreas</c>) rather than the offered
    /// one, or overlap goes blind to exactly the cases it exists to catch.
    /// </para>
    /// <para>
    /// Nothing here refuses anything. R35 keeps an overlapping draw legal and marks it instead,
    /// so this returns what was found and never a veto — pushing the conflict into the drawing
    /// UI is the worse place to resolve it.
    /// </para>
    /// </summary>
    public static class AreaOverlap
    {
        /// <summary>
        /// The plots two areas share, ordered by x then z. Ordered rather than merely collected
        /// because the ordering is what a player reads down in <see cref="FormatOverlapDetail(AreaOverlapMatch, int)"/>,
        /// and an unordered set walked out of a hash table renders differently on each refresh.
        /// </summary>
        public static IReadOnlyList<PlotCoord> SharedPlots(IEnumerable<PlotCoord> a, IEnumerable<PlotCoord> b)
        {
            if (a == null || b == null) return Array.Empty<PlotCoord>();

            var mine = new HashSet<PlotCoord>(a);
            if (mine.Count == 0) return Array.Empty<PlotCoord>();

            return b.Where(mine.Contains)
                .Distinct()
                .OrderBy(p => p.X)
                .ThenBy(p => p.Z)
                .ToArray();
        }

        /// <summary>
        /// Whether two areas cover any plot in common. A shared EDGE is not a shared plot: two
        /// areas drawn back to back touch on the ground and collide over nothing.
        /// </summary>
        public static bool Overlaps(IEnumerable<PlotCoord> a, IEnumerable<PlotCoord> b)
        {
            if (a == null || b == null) return false;

            var mine = new HashSet<PlotCoord>(a);
            return mine.Count != 0 && b.Any(mine.Contains);
        }

        /// <summary>
        /// Every area in <paramref name="published"/> that collides with <paramref name="self"/>,
        /// with the plots each collision covers.
        /// </summary>
        /// <param name="published">
        /// The raw enumeration, projected (KTD8). It may contain <paramref name="self"/> — it
        /// normally does, since self is published too — and self is skipped by identity, so an
        /// area never reports as overlapping itself.
        /// </param>
        public static IReadOnlyList<AreaOverlapMatch> Matches(
            AreaProjection self, IEnumerable<AreaProjection> published)
        {
            if (self == null || published == null) return Array.Empty<AreaOverlapMatch>();

            var mine = new HashSet<PlotCoord>(self.Plots);
            if (mine.Count == 0) return Array.Empty<AreaOverlapMatch>();

            var matches = new List<AreaOverlapMatch>();
            foreach (var other in published)
            {
                if (other == null || self.IsSameAreaAs(other)) continue;

                var shared = SharedPlots(mine, other.Plots);
                if (shared.Count == 0) continue;

                matches.Add(new AreaOverlapMatch(other, shared));
            }

            return matches;
        }

        /// <summary>
        /// Whether this area collides with anything — what the roster line needs, since the panel
        /// says only THAT an overlap exists and the diagnostic command says where (R36).
        /// </summary>
        public static bool HasAny(AreaProjection self, IEnumerable<AreaProjection> published)
        {
            if (self == null || published == null) return false;

            var mine = new HashSet<PlotCoord>(self.Plots);
            if (mine.Count == 0) return false;

            return published.Any(other =>
                other != null && !self.IsSameAreaAs(other) && other.Plots.Any(mine.Contains));
        }

        /// <summary>
        /// The world column at the centre of a plot — the block a player can fly to and stand on
        /// to find the shared ground.
        ///
        /// <para>
        /// Integer arithmetic on the plot's base corner rather than division of a centre point,
        /// so it agrees with <see cref="PlotCoord.FromWorldColumn"/> on the far side of the
        /// origin: plot (-3) starts at block -15 for a 5-block plot and its centre is -13, both
        /// inside the plot. Halving a signed centre would round toward zero and name a block in
        /// the neighbouring plot.
        /// </para>
        /// </summary>
        public static (int X, int Z) CentreColumn(PlotCoord plot, int plotSize)
        {
            if (plotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(plotSize), "plotSize must be positive.");

            var half = plotSize / 2;
            return ((plot.X * plotSize) + half, (plot.Z * plotSize) + half);
        }

        /// <summary>The centre column written the way the rest of the mod writes a position.</summary>
        public static string FormatCentreBlock(PlotCoord plot, int plotSize)
        {
            var (x, z) = CentreColumn(plot, plotSize);
            return $"({x}, {z})";
        }

        /// <summary>
        /// <b>One collision, in the words the diagnostic command prints (R36).</b> The panel says
        /// an overlap exists; this says exactly where, because a row per plot is a debugging
        /// surface and the panel's budget is one summary row per fact.
        ///
        /// <para>
        /// It names two things and stops. The ground — the centre block of every shared plot, so
        /// the player can go and look. And whether the other area holds those plots right now,
        /// which is the only thing that answers "will this block ever lift" and what R27 already
        /// obliges a blocked area to say. It names nothing else about the other area: not its
        /// name, not its owner, not what it is for (R30, R42) — and it cannot, because
        /// <see cref="AreaProjection"/> does not carry the first two at all.
        /// </para>
        /// </summary>
        public static string FormatOverlapDetail(AreaOverlapMatch match, int plotSize)
        {
            var plots = match.SharedPlots;
            if (plots.Count == 0) return string.Empty;

            var claim = match.Other != null && match.Other.HoldsClaim
                ? "that area holds them now"
                : "that area holds no claim on them";

            var blocks = new StringBuilder();
            for (var i = 0; i < plots.Count; i++)
            {
                if (i > 0) blocks.Append(", ");
                blocks.Append(FormatCentreBlock(plots[i], plotSize));
            }

            var noun = plots.Count == 1 ? "plot" : "plots";
            return $"{plots.Count} {noun} shared with another area -- {claim} -- centre blocks {blocks}";
        }

        /// <summary>
        /// One line per collision, in the order <see cref="Matches"/> found them. An area
        /// overlapping two others reports both, rather than collapsing to a count that cannot say
        /// which of them holds the ground.
        /// </summary>
        public static IReadOnlyList<string> FormatOverlapDetail(
            IEnumerable<AreaOverlapMatch> matches, int plotSize)
        {
            if (matches == null) return Array.Empty<string>();

            return matches
                .Select(m => FormatOverlapDetail(m, plotSize))
                .Where(line => !string.IsNullOrEmpty(line))
                .ToArray();
        }
    }

    /// <summary>Why a claim was refused — the two are lifted by different acts, so they are told apart.</summary>
    public enum AreaClaimBlock
    {
        /// <summary>
        /// Another area holds these plots through its assignment (R39). Kind-blind: any assigned
        /// area holds its plots against every dock. Lifted by that area being unassigned.
        /// </summary>
        HeldByAssignment,

        /// <summary>
        /// The ground is farmland (R47), whether or not that farm is currently assigned. Lifted
        /// only by the farm's owner deleting the farming area.
        /// </summary>
        FarmlandReserved
    }

    /// <summary>One reason one claim cannot be taken: what blocks it, over which plots, and why.</summary>
    public readonly struct AreaClaimConflict
    {
        /// <summary>The blocking area, as the projection R41 limits it to.</summary>
        public AreaProjection Holder { get; }

        /// <summary>The plots it blocks — exactly the ones the two areas share, in raster order.</summary>
        public IReadOnlyList<PlotCoord> Plots { get; }

        public AreaClaimBlock Reason { get; }

        public AreaClaimConflict(AreaProjection holder, IReadOnlyList<PlotCoord> plots, AreaClaimBlock reason)
        {
            Holder = holder;
            Plots = plots ?? Array.Empty<PlotCoord>();
            Reason = reason;
        }
    }

    /// <summary>
    /// <b>Who may take which ground (U13, R37, R39, R44, R47).</b> The decidable half of
    /// assignment, per KTD12: plain values in, a verdict out, and no Eco type anywhere near it.
    ///
    /// <para>
    /// Three questions, deliberately separate. <see cref="HoldsClaim"/> is what an area holds —
    /// assignment is the act that takes ground, and <c>[empty]</c> is the one status that drops
    /// it again. <see cref="Conflicts"/> is whether a claimant may take what it overlaps.
    /// <see cref="MayBeOfferedToMiningDock"/> is whether a mining dock is shown the area at all.
    /// They are asked at different moments — the first when a projection is built, the second
    /// under the assignment lock, the third on the roster tick — and folding them together would
    /// put a world walk on the render path.
    /// </para>
    /// <para>
    /// Nothing here writes a claim, and nothing here is atomic. R39's atomicity is the caller's:
    /// ONE lock spanning the overlap scan and the write, held by the Eco-side assignment path
    /// (KTD6). A lock per area cannot do it — two docks assigning two DIFFERENT overlapping areas
    /// would each take a different area's lock and neither would serialise against the other.
    /// </para>
    /// </summary>
    public static class AreaClaims
    {
        /// <summary>
        /// Whether an area holds its plots against other docks right now (R37).
        ///
        /// <para>
        /// Assignment is the whole of it, with one exception. An unassigned area holds nothing —
        /// which is why AE12's freshly drawn mining area does not stop the farm working the ground
        /// they share. And neither does an area whose ground reads <c>[empty]</c>: there is
        /// nothing left to hold it for, and that is precisely what lets land pass from one purpose
        /// to the next without a release being negotiated.
        /// </para>
        /// <para>
        /// <c>[cleared]</c> is NOT that exception and the difference is the reason the two
        /// statuses exist. <c>[cleared]</c> says the drone is finished because it was refused, and
        /// R19 lets a survey lift that refusal, so the material behind it may yet be taken by the
        /// dock that holds the ground.
        /// </para>
        /// <para>
        /// Farmland is not read here at all. Its reservation OUTLIVES its assignment (R47), so it
        /// is not something a claim flag can express — see <see cref="Conflicts"/>.
        /// </para>
        /// </summary>
        public static bool HoldsClaim(bool assigned, AreaLifecycleStatus status) =>
            assigned && status != AreaLifecycleStatus.Empty;

        /// <summary>
        /// Whether a mining dock may be offered an area for assignment (R44, R47).
        ///
        /// <para>
        /// Two independent refusals. <c>[cleared]</c> and <c>[empty]</c> are withheld by the
        /// ladder's own test (R44): there is nothing a mining drone could take, and both stay
        /// visible on both tabs and assignable to a SURVEY dock, which is the only thing that
        /// returns them to the ramp. Farmland is withheld by KIND (R47), which the ladder cannot
        /// answer because a farming area never reads a rung of the ramp at all — it reads
        /// <c>[farm]</c>, and <see cref="AreaLifecycle.IsOfferableToMiningDock"/> is deliberately
        /// a question about a mining status.
        /// </para>
        /// </summary>
        public static bool MayBeOfferedToMiningDock(AreaKind kind, AreaLifecycleStatus status) =>
            kind == AreaKind.Mining && AreaLifecycle.IsOfferableToMiningDock(status);

        /// <summary>
        /// Everything standing between a claimant and the ground it overlaps (R39, R47), or an
        /// empty list when it may take all of it.
        ///
        /// <para>
        /// Two rules, tested in this order for a reason. <b>R47 first:</b> if the claimant is
        /// mining and the other area is farmland, the ground is reserved whether or not that farm
        /// is assigned — an unassigned farm is between passes, not finished, because farmland
        /// never reaches an exhausted state the way a mine does. Reporting that case as an
        /// ordinary held claim would send the player to unassign the farm, which changes nothing.
        /// <b>R39 second, and kind-blind:</b> any assigned area holds its plots against every
        /// dock. The <c>[empty]</c> exception is already folded into
        /// <see cref="AreaProjection.HoldsClaim"/> by <see cref="HoldsClaim"/>.
        /// </para>
        /// <para>
        /// The asymmetry runs ONE way and is deliberate: a farm may take ground a mine has
        /// finished with, and a mine may not take ground a farm has not been released from.
        /// </para>
        /// </summary>
        /// <param name="claimantKind">
        /// The kind of work the claim is for. A mining dock passes <see cref="AreaKind.Mining"/>;
        /// a survey dock passes the area's own kind, because a survey pass serves whatever the
        /// area is for and removes nothing from the ground itself.
        /// </param>
        /// <param name="claimantAlreadyHolds">
        /// Which of the overlapping areas the claimant itself already holds, so reassigning a dock
        /// onto ground beside its own standing claim does not report the dock as blocking itself.
        /// Null means "none". It is a PREDICATE rather than a field on the projection because the
        /// holder's identity is exactly what R41 keeps out of this channel: the Eco side knows
        /// which areas it holds, this path only knows that they are held.
        ///
        /// <para>
        /// It lifts <see cref="AreaClaimBlock.HeldByAssignment"/> only. R47 is a fact about the
        /// ground rather than about who is asking, so farmland stays reserved even against the
        /// dock that holds the farm.
        /// </para>
        /// </param>
        public static IReadOnlyList<AreaClaimConflict> Conflicts(
            AreaKind claimantKind,
            IEnumerable<AreaOverlapMatch> matches,
            Func<AreaProjection, bool> claimantAlreadyHolds = null)
        {
            if (matches == null) return Array.Empty<AreaClaimConflict>();

            var conflicts = new List<AreaClaimConflict>();
            foreach (var match in matches)
            {
                var other = match.Other;
                if (other == null || match.SharedPlots.Count == 0) continue;

                if (claimantKind == AreaKind.Mining && other.Kind == AreaKind.Farming)
                {
                    conflicts.Add(new AreaClaimConflict(other, match.SharedPlots, AreaClaimBlock.FarmlandReserved));
                    continue;
                }

                if (!other.HoldsClaim) continue;
                if (claimantAlreadyHolds != null && claimantAlreadyHolds(other)) continue;

                conflicts.Add(new AreaClaimConflict(other, match.SharedPlots, AreaClaimBlock.HeldByAssignment));
            }

            return conflicts;
        }
    }
}
