using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What a change to one column's ground means for ONE area covering it (U8, R16, R17, R43).
    ///
    /// <para>
    /// Four answers, and three of them mean "do nothing" for three different reasons that are not
    /// interchangeable. <see cref="IgnoredNotOurs"/> is the mod having no idea what happened,
    /// because the write was not one of its own; that is the common case and the one this
    /// requirement narrowed the reaction down to. <see cref="RecordedAsOwnWork"/> is the mod
    /// knowing exactly what happened here and having a better record of it than "unsurveyed".
    /// <see cref="NotThisKindsWork"/> is a write addressed to a different kind of area that
    /// happens to overlap this one. Only <see cref="ResetToUnsurveyed"/> touches anything.
    /// </para>
    /// </summary>
    public enum GroundChangeVerdict
    {
        /// <summary>
        /// R16, as narrowed. One of the mod's own drones changed ground belonging to a DIFFERENT
        /// area of the same kind. The plots it touched go back to unsurveyed.
        ///
        /// <para>
        /// A player digging, an administrator command and a map-editor paste used to land here
        /// too. They no longer do: they are not attributable to any of the mod's drones and now
        /// take <see cref="IgnoredNotOurs"/> instead.
        /// </para>
        /// </summary>
        ResetToUnsurveyed,

        /// <summary>
        /// R17, R43. One of the mod's drones writing on the very area its work serves. The area
        /// keeps its findings and its coverage, and the staleness is carried by the mined stamps
        /// alone -- so the player can still see what was there before it was taken.
        /// </summary>
        RecordedAsOwnWork,

        /// <summary>
        /// R43. The write serves an area of a different KIND. Ground two areas cover at once --
        /// handed-over plots are exactly that -- records the state of the work actually being
        /// done on it, so a farming write does not unsurvey the mining area underneath it.
        /// </summary>
        NotThisKindsWork,

        /// <summary>
        /// R1. The write cannot be attributed to any of this mod's drones. A player digging, an
        /// administrator command, a map-editor paste, another mod's write, or a rebuild of the
        /// engine's block caches all land here.
        ///
        /// <para>
        /// Nothing happens. The mod does not monitor the world for changes it did not make, so it
        /// does not know what this write did and does not pretend to. The engine only raises a
        /// signal when the topmost block of a column changes, so reacting to the fraction of
        /// outside changes that happen to surface would produce an arbitrary picture rather than a
        /// current one, and it would pay for that with deleted survey results.
        /// </para>
        /// <para>
        /// An outside change is instead learned in situ: a mining drone reaching the work site
        /// finds that a block is present or absent contrary to what the survey reported, and
        /// records that. That is the only path by which such a change is ever learned.
        /// </para>
        /// </summary>
        IgnoredNotOurs
    }

    /// <summary>
    /// Who made a ground write, and which area their work serves (U8 step 2, R43).
    ///
    /// <para>
    /// The engine's block-write event does not name its writer, so the mod marks its OWN writes
    /// as it makes them (<see cref="ModGroundWrite"/>) and everything unmarked reads as not ours.
    /// </para>
    /// <para>
    /// The safe default direction runs the opposite way from what it once did, and the change is
    /// deliberate. A writer the mod forgets to mark now reads as not ours and is ignored, which
    /// costs nothing immediately and leaves the survey slightly out of date until a mining drone
    /// discovers the discrepancy at the work site. Previously the same slip produced a reset that
    /// deleted survey results. The unsafe direction has become the inert one, which is the
    /// strongest argument for narrowing the reaction this way.
    /// </para>
    /// <para>
    /// The area is named by (owning dock id, area id) rather than by area id alone, because an
    /// area id is dock-local -- two survey docks each publish an area 1, exactly as
    /// <c>MiningExclusionEntry</c> already has to account for.
    /// </para>
    /// </summary>
    public readonly struct GroundWriteAttribution : IEquatable<GroundWriteAttribution>
    {
        private GroundWriteAttribution(bool isModsOwn, string servedAreaOwnerId, int servedAreaId, AreaKind servedKind)
        {
            this.IsModsOwn = isModsOwn;
            this.ServedAreaOwnerId = servedAreaOwnerId;
            this.ServedAreaId = servedAreaId;
            this.ServedKind = servedKind;
        }

        /// <summary>True when one of the mod's own drones made this write.</summary>
        public bool IsModsOwn { get; }

        /// <summary>Id of the dock owning the area this write serves; meaningless when <see cref="IsModsOwn"/> is false.</summary>
        public string ServedAreaOwnerId { get; }

        /// <summary>Dock-local id of the area this write serves; meaningless when <see cref="IsModsOwn"/> is false.</summary>
        public int ServedAreaId { get; }

        /// <summary>What kind of work this write is -- the kind of area it is attributed to (R43).</summary>
        public AreaKind ServedKind { get; }

        /// <summary>The default: a write the mod did not make, or made without marking. Falls to R16 everywhere.</summary>
        public static GroundWriteAttribution Outside => default;

        /// <summary>A write by one of the mod's drones, serving one area of one kind.</summary>
        public static GroundWriteAttribution ByDrone(string servedAreaOwnerId, int servedAreaId, AreaKind servedKind) =>
            new GroundWriteAttribution(true, servedAreaOwnerId, servedAreaId, servedKind);

        public bool Equals(GroundWriteAttribution other) =>
            this.IsModsOwn == other.IsModsOwn
            && this.ServedAreaId == other.ServedAreaId
            && this.ServedKind == other.ServedKind
            && string.Equals(this.ServedAreaOwnerId, other.ServedAreaOwnerId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GroundWriteAttribution other && this.Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(this.IsModsOwn, this.ServedAreaOwnerId, this.ServedAreaId, this.ServedKind);
    }

    /// <summary>
    /// The decidable half of U8: which plots a changed column reaches, and what a change means
    /// for an area once it is attributed. Eco-free, so both halves are unit-testable without a
    /// server -- the subscription itself is the only part that is not.
    /// </summary>
    public static class GroundChange
    {
        /// <summary>The plot a changed world column belongs to -- the same fold the survey pass uses.</summary>
        public static PlotCoord PlotOf(int worldX, int worldZ, int plotSize) =>
            PlotCoord.FromWorldColumn(worldX, worldZ, plotSize);

        /// <summary>
        /// The plots of <paramref name="areaPlots"/> that <paramref name="changedColumns"/> reach,
        /// distinct and in no particular order. Empty when the change fell outside the area
        /// entirely, which is the overwhelmingly common case on a world-wide block event.
        /// </summary>
        public static IReadOnlyList<PlotCoord> AffectedPlots(
            IEnumerable<PlotCoord> areaPlots, IEnumerable<(int X, int Z)> changedColumns, int plotSize)
        {
            if (areaPlots == null) throw new ArgumentNullException(nameof(areaPlots));
            if (changedColumns == null) throw new ArgumentNullException(nameof(changedColumns));

            var covered = new HashSet<PlotCoord>(areaPlots);
            var affected = new List<PlotCoord>();
            var seen = new HashSet<PlotCoord>();

            foreach (var (x, z) in changedColumns)
            {
                var plot = PlotOf(x, z, plotSize);
                if (covered.Contains(plot) && seen.Add(plot))
                    affected.Add(plot);
            }

            return affected;
        }

        /// <summary>
        /// What a write attributed as <paramref name="attribution"/> means for the area
        /// (<paramref name="areaOwnerId"/>, <paramref name="areaId"/>) of kind
        /// <paramref name="areaKind"/>. Assumes the change actually reached that area --
        /// <see cref="AffectedPlots"/> is what answers whether it did.
        ///
        /// <para>
        /// The first test is the one that decides almost every call, and it is the narrowing this
        /// requirement is about. A write the mod cannot attribute to one of its own drones is
        /// ignored entirely (R1). The mod does not monitor the world for changes it did not make,
        /// so it does not know what such a write did, and reacting to it would mean acting on a
        /// guess. An outside change is learned in situ instead, by the mining drone that reaches
        /// the work site and finds the world does not match what the survey reported.
        /// </para>
        /// <para>
        /// After that, the area's own work wins (R17/R43); then a write serving a different kind
        /// is not this area's business (R43); and everything else -- one of the mod's own drones
        /// digging ground that belongs to another dock's area of the same kind -- is R16.
        /// </para>
        /// </summary>
        public static GroundChangeVerdict VerdictFor(
            GroundWriteAttribution attribution, string areaOwnerId, int areaId, AreaKind areaKind)
        {
            if (!attribution.IsModsOwn)
                return GroundChangeVerdict.IgnoredNotOurs;

            if (attribution.ServedAreaId == areaId
                && string.Equals(attribution.ServedAreaOwnerId, areaOwnerId, StringComparison.Ordinal))
                return GroundChangeVerdict.RecordedAsOwnWork;

            if (attribution.ServedKind != areaKind)
                return GroundChangeVerdict.NotThisKindsWork;

            return GroundChangeVerdict.ResetToUnsurveyed;
        }

        /// <summary>The one verdict that touches anything. Every caller keys off this rather than re-listing the enum.</summary>
        public static bool RequiresReset(GroundChangeVerdict verdict) =>
            verdict == GroundChangeVerdict.ResetToUnsurveyed;

        /// <summary>
        /// Whether this verdict calls for the mod to do anything at all. This is the name callers
        /// should use: what a reaction does is to mark the affected plots for re-reading, which is
        /// not a reset, and calling the predicate "requires reset" would describe behaviour the
        /// mod no longer has.
        /// </summary>
        public static bool RequiresReaction(GroundChangeVerdict verdict) =>
            verdict == GroundChangeVerdict.ResetToUnsurveyed;
    }

    /// <summary>
    /// The mod marking its own ground writes so the block-write handler can attribute them
    /// (U8 step 2, KTD7).
    ///
    /// <para>
    /// Ambient and thread-scoped rather than threaded through a parameter, because the write is
    /// several frames away from the code that knows who is writing: the drone submits a game
    /// action pack, and the engine runs the pack's post-effects -- which are what actually delete
    /// the blocks -- synchronously on the same thread inside <c>TryPerform</c>. A parameter
    /// cannot cross that, and the engine's event carries no writer of its own.
    /// </para>
    /// <para>
    /// Scopes nest and restore, so an outer attribution survives an inner one. Outside every
    /// scope the answer is <see cref="GroundWriteAttribution.Outside"/>, which is what makes
    /// every other writer in the world -- players, admin commands, the map editor -- fall to R16
    /// without any of them being enumerated.
    /// </para>
    /// </summary>
    public static class ModGroundWrite
    {
        [ThreadStatic] private static GroundWriteAttribution current;
        [ThreadStatic] private static int depth;

        /// <summary>Who is writing on this thread right now, or <see cref="GroundWriteAttribution.Outside"/>.</summary>
        public static GroundWriteAttribution Current => depth > 0 ? current : GroundWriteAttribution.Outside;

        /// <summary>Opens a scope attributing every ground write this thread makes until it is disposed.</summary>
        public static IDisposable Attribute(GroundWriteAttribution attribution)
        {
            var scope = new Scope(current, depth);
            current = attribution;
            depth++;
            return scope;
        }

        private sealed class Scope : IDisposable
        {
            private readonly GroundWriteAttribution previous;
            private readonly int previousDepth;
            private bool closed;

            public Scope(GroundWriteAttribution previous, int previousDepth)
            {
                this.previous = previous;
                this.previousDepth = previousDepth;
            }

            public void Dispose()
            {
                if (this.closed) return;
                this.closed = true;
                current = this.previous;
                depth = this.previousDepth;
            }
        }
    }

    /// <summary>
    /// The order a survey sweep visits an area's plots in, and the one arithmetic a reset has to
    /// do to it (U7's cursor, U8's reset).
    ///
    /// <para>
    /// The order lives here rather than inside the strategy because two callers now depend on it
    /// meaning the same thing: the sweep indexes into it, and a reset has to know where in it the
    /// plots it just dropped sit. Two hand-written <c>OrderBy</c> clauses that must agree is
    /// exactly the drift this removes.
    /// </para>
    /// </summary>
    public static class SweepOrder
    {
        /// <summary>Raster order -- by Z then X, a roughly lawn-mower visitation.</summary>
        public static IReadOnlyList<PlotCoord> RasterOrder(IEnumerable<PlotCoord> plots)
        {
            if (plots == null) throw new ArgumentNullException(nameof(plots));
            return plots.OrderBy(p => p.Z).ThenBy(p => p.X).ToList();
        }

        /// <summary>
        /// Where the sweep must carry on from so that every plot in <paramref name="resetPlots"/>
        /// is visited again: the earliest of their raster indices, or
        /// <paramref name="currentPlotIndex"/> when none of them sits behind it.
        ///
        /// <para>
        /// The cursor only ever moves BACKWARDS here. A plot the pass has not reached yet needs no
        /// rewind, and a plot the area does not contain cannot move it at all. Re-flying the plots
        /// in between costs travel and nothing else -- their samples are still in the record, so
        /// they are deduped on arrival and their coverage never moves.
        /// </para>
        /// </summary>
        public static int RewindIndex(
            IReadOnlyList<PlotCoord> rasterOrdered, IEnumerable<PlotCoord> resetPlots, int currentPlotIndex)
        {
            if (rasterOrdered == null) throw new ArgumentNullException(nameof(rasterOrdered));
            if (resetPlots == null) return currentPlotIndex;

            var rewound = currentPlotIndex;
            foreach (var plot in resetPlots)
            {
                for (var i = 0; i < rewound; i++)
                {
                    if (!rasterOrdered[i].Equals(plot)) continue;
                    rewound = i;
                    break;
                }
            }

            return rewound;
        }
    }
}
