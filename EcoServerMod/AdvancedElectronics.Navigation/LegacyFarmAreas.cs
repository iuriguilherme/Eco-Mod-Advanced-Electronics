using System.Collections.Generic;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// One farm as the pre-fold save holds it: every <c>[Serialized]</c> member of the Eco-side
    /// <c>FarmAreaEntry</c>, flattened to plain values so the fold can be decided without an Eco
    /// type in play.
    ///
    /// <para>
    /// <b>The constructor takes all eighteen and defaults none of them.</b> That is the point of
    /// the type. A member added to <c>FarmAreaEntry</c> and mirrored here breaks every call site
    /// at compile time, which is the only mechanical link available across the assembly boundary
    /// — the test project cannot reference the Eco-side project, so nothing else would notice a
    /// setting quietly failing to make the crossing. An optional parameter would convert that
    /// compile error into a folded farm missing a value, discovered after someone's world was
    /// already upgraded.
    /// </para>
    /// </summary>
    public sealed class LegacyFarmRow
    {
        /// <summary>The farm's id in the legacy per-dock farm numbering, minted by its own counter from 1.</summary>
        public int Id { get; }

        /// <summary>Player-facing name.</summary>
        public string Name { get; }

        /// <summary>Drawn plots, flattened as consecutive (x, z) pairs — the same flattening the area side uses.</summary>
        public IReadOnlyList<int> PlotCoords { get; }

        /// <summary>Redraw counter, so a job built against the old shape can tell.</summary>
        public int Epoch { get; }

        /// <summary>The one crop this area grows, as the engine's species name; null until a citizen picks one.</summary>
        public string Crop { get; }

        /// <summary>
        /// The level-first toggle. Behavioural, not a readout: it is what decides whether the
        /// drone levels the ground before planting, so a fold that drops it changes what the
        /// drone does with nothing on screen to say so.
        /// </summary>
        public bool LevelFirst { get; }

        /// <summary>Whether the drone works this area. Assignment is what makes an area hold its plots.</summary>
        public bool Assigned { get; }

        /// <summary>Last reported stall as a stall-reason ordinal, or -1 for none.</summary>
        public int LastStallReason { get; }

        /// <summary>Last reported next action as an action ordinal, or -1 for none.</summary>
        public int LastNextAction { get; }

        /// <summary>Hours until the least-grown plant comes due; negative for none. Display only.</summary>
        public double LastNextDueHours { get; }

        /// <summary>When that plant comes due, as an absolute world time. The scheduling value.</summary>
        public double LastDueAtWorldSeconds { get; }

        /// <summary>Plots held by an overlap with another dock's area.</summary>
        public int LastHeldPlotCount { get; }

        /// <summary>The engine's own word for why the ground refused the crop.</summary>
        public string LastUnfitCondition { get; }

        /// <summary>What linked storage was short of.</summary>
        public string LastMissingMaterial { get; }

        /// <summary>Whether the surface was flat enough to farm, as last derived.</summary>
        public bool LastFlat { get; }

        /// <summary>Whether a level pass is under way. Distinct from the toggle: the toggle is the request, this is the run.</summary>
        public bool LevelPassStarted { get; }

        /// <summary>The height the running pass is levelling to, pinned at pass entry.</summary>
        public int LevelTargetHeight { get; }

        /// <summary>Blocks the running pass has removed and still counts as its own material.</summary>
        public int LevelBankedSpoil { get; }

        public LegacyFarmRow(
            int id,
            string name,
            IEnumerable<int> plotCoords,
            int epoch,
            string crop,
            bool levelFirst,
            bool assigned,
            int lastStallReason,
            int lastNextAction,
            double lastNextDueHours,
            double lastDueAtWorldSeconds,
            int lastHeldPlotCount,
            string lastUnfitCondition,
            string lastMissingMaterial,
            bool lastFlat,
            bool levelPassStarted,
            int levelTargetHeight,
            int levelBankedSpoil)
        {
            this.Id = id;
            this.Name = name;
            // Copied, not aliased: the caller's source is the live legacy list, and the fold's
            // answer must not change underneath it if that list is edited afterwards.
            this.PlotCoords = plotCoords == null ? new List<int>() : new List<int>(plotCoords);
            this.Epoch = epoch;
            this.Crop = crop;
            this.LevelFirst = levelFirst;
            this.Assigned = assigned;
            this.LastStallReason = lastStallReason;
            this.LastNextAction = lastNextAction;
            this.LastNextDueHours = lastNextDueHours;
            this.LastDueAtWorldSeconds = lastDueAtWorldSeconds;
            this.LastHeldPlotCount = lastHeldPlotCount;
            this.LastUnfitCondition = lastUnfitCondition;
            this.LastMissingMaterial = lastMissingMaterial;
            this.LastFlat = lastFlat;
            this.LevelPassStarted = levelPassStarted;
            this.LevelTargetHeight = levelTargetHeight;
            this.LevelBankedSpoil = levelBankedSpoil;
        }
    }

    /// <summary>
    /// One area the fold wants written, as the caller should write it: the id it takes, the kind
    /// it carries, the legacy farm id it consumed, and everything that farm recorded.
    ///
    /// <para>
    /// <see cref="Kind"/> is stated on every one of these and is never left to a default. Coming
    /// the other way, <see cref="AreaKind.Mining"/> is <c>0</c>, so an area whose kind is not
    /// written explicitly loads as a MINE: it would run the lifecycle ladder instead of reading
    /// as farmland, it would be offered to mining docks, and a neighbouring mining dock could
    /// claim the ground a player's crop is standing in.
    /// </para>
    /// </summary>
    public sealed class FoldedFarmArea
    {
        /// <summary>The id this area takes in the dock's one area collection, minted from the survey counter.</summary>
        public int AreaId { get; }

        /// <summary>What the area is for. Always <see cref="AreaKind.Farming"/>, stated rather than defaulted.</summary>
        public AreaKind Kind { get; }

        /// <summary>
        /// The legacy farm id this area was folded from — the fold marker, and the whole of what
        /// makes a second run recognise its own earlier work. The fold sees ids and the counter
        /// and nothing else, so without this a re-run cannot tell a farm already folded from one
        /// still to fold, and mints a fresh id for every row a second time.
        /// </summary>
        public int FoldedFromLegacyFarmId { get; }

        /// <inheritdoc cref="LegacyFarmRow.Name"/>
        public string Name { get; }

        /// <inheritdoc cref="LegacyFarmRow.PlotCoords"/>
        public IReadOnlyList<int> PlotCoords { get; }

        /// <inheritdoc cref="LegacyFarmRow.Epoch"/>
        public int Epoch { get; }

        /// <inheritdoc cref="LegacyFarmRow.Crop"/>
        public string Crop { get; }

        /// <inheritdoc cref="LegacyFarmRow.LevelFirst"/>
        public bool LevelFirst { get; }

        /// <inheritdoc cref="LegacyFarmRow.Assigned"/>
        public bool Assigned { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastStallReason"/>
        public int LastStallReason { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastNextAction"/>
        public int LastNextAction { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastNextDueHours"/>
        public double LastNextDueHours { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastDueAtWorldSeconds"/>
        public double LastDueAtWorldSeconds { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastHeldPlotCount"/>
        public int LastHeldPlotCount { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastUnfitCondition"/>
        public string LastUnfitCondition { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastMissingMaterial"/>
        public string LastMissingMaterial { get; }

        /// <inheritdoc cref="LegacyFarmRow.LastFlat"/>
        public bool LastFlat { get; }

        /// <inheritdoc cref="LegacyFarmRow.LevelPassStarted"/>
        public bool LevelPassStarted { get; }

        /// <inheritdoc cref="LegacyFarmRow.LevelTargetHeight"/>
        public int LevelTargetHeight { get; }

        /// <inheritdoc cref="LegacyFarmRow.LevelBankedSpoil"/>
        public int LevelBankedSpoil { get; }

        internal FoldedFarmArea(int areaId, AreaKind kind, LegacyFarmRow from)
        {
            this.AreaId = areaId;
            this.Kind = kind;
            this.FoldedFromLegacyFarmId = from.Id;

            this.Name = from.Name;
            this.PlotCoords = new List<int>(from.PlotCoords);
            this.Epoch = from.Epoch;
            this.Crop = from.Crop;
            this.LevelFirst = from.LevelFirst;
            this.Assigned = from.Assigned;
            this.LastStallReason = from.LastStallReason;
            this.LastNextAction = from.LastNextAction;
            this.LastNextDueHours = from.LastNextDueHours;
            this.LastDueAtWorldSeconds = from.LastDueAtWorldSeconds;
            this.LastHeldPlotCount = from.LastHeldPlotCount;
            this.LastUnfitCondition = from.LastUnfitCondition;
            this.LastMissingMaterial = from.LastMissingMaterial;
            this.LastFlat = from.LastFlat;
            this.LevelPassStarted = from.LevelPassStarted;
            this.LevelTargetHeight = from.LevelTargetHeight;
            this.LevelBankedSpoil = from.LevelBankedSpoil;
        }
    }

    /// <summary>
    /// What one dock's fold decided: the areas to write, and the counter the dock carries away.
    /// An empty <see cref="Areas"/> with an unchanged <see cref="NextAreaId"/> is the "nothing to
    /// do" answer, and is what a second run against an already-folded dock returns.
    /// </summary>
    public sealed class LegacyFarmFold
    {
        /// <summary>The areas to append, in the order the legacy rows appeared.</summary>
        public IReadOnlyList<FoldedFarmArea> Areas { get; }

        /// <summary>The dock's area counter after the fold. Never lower than the value passed in.</summary>
        public int NextAreaId { get; }

        internal LegacyFarmFold(IReadOnlyList<FoldedFarmArea> areas, int nextAreaId)
        {
            this.Areas = areas;
            this.NextAreaId = nextAreaId;
        }
    }

    /// <summary>
    /// Folds a dock's legacy farm rows into the one per-dock area collection that now holds mines
    /// and farms alike (U1, R17, R18).
    ///
    /// <para>
    /// Farms used to live in their own collection with their own counter, and mining areas in
    /// another with another — both starting at 1, so a dock's farm 1 and its survey area 1 were
    /// two different pieces of ground wearing the same number. That is why the claim machinery
    /// could not see farms at all: there was no one collection to ask. This fold is the arithmetic
    /// that merges them.
    /// </para>
    /// <para>
    /// <b>Only the farms are renumbered (KTD3).</b> The two counters collide by construction, so
    /// one side has to move, and it is the farms: a survey area's id is referenced from persisted
    /// state elsewhere on the dock, while a farm id is referenced by nothing. The fold returns no
    /// survey area at all, which is the strongest available form of leaving them alone.
    /// </para>
    /// <para>
    /// <b>Idempotence is carried by the marker, not assumed.</b> Each folded area records the
    /// legacy id it consumed, and a row whose id is already marked on the dock is returned as no
    /// change. That is the only form of idempotence this signature can honestly deliver: the fold
    /// sees ids and the counter, so "these rows were already folded" is not observable in the rows
    /// themselves, and without the marker a second load would mint fresh ids and duplicate every
    /// farm a player owns.
    /// </para>
    /// <para>
    /// Malformed input is passed over rather than thrown on, matching
    /// <see cref="LegacyMinedStamps"/>: this runs at world load, where an exception costs the load
    /// itself and leaves the player no way to reach the dock and repair it.
    /// </para>
    /// </summary>
    public static class LegacyFarmAreas
    {
        /// <summary>
        /// Decides what <paramref name="legacyRows"/> become in a dock whose area counter stands
        /// at <paramref name="nextAreaId"/> and whose existing areas carry the fold markers in
        /// <paramref name="foldedLegacyFarmIds"/>.
        ///
        /// <para>
        /// <paramref name="foldedLegacyFarmIds"/> is every existing area's
        /// <c>FoldedFromLegacyFarmId</c>, marker and non-marker alike; a zero means an area folded
        /// from no farm and marks nothing as done. A legacy row whose id appears there has already
        /// been folded and is skipped.
        /// </para>
        /// <para>
        /// Nothing is modified and nothing Eco-side is touched: the caller writes the result.
        /// </para>
        /// </summary>
        public static LegacyFarmFold Fold(
            IEnumerable<LegacyFarmRow> legacyRows, IEnumerable<int> foldedLegacyFarmIds, int nextAreaId)
        {
            // Id 0 is what every area reference on the dock reads as "no area", so it is not an id
            // the fold may hand out however a counter arrived below 1.
            var cursor = nextAreaId < 1 ? 1 : nextAreaId;
            var folded = new List<FoldedFarmArea>();

            if (legacyRows == null)
                return new LegacyFarmFold(folded, cursor);

            var alreadyFolded = new HashSet<int>();
            if (foldedLegacyFarmIds != null)
            {
                foreach (var marker in foldedLegacyFarmIds)
                {
                    // Zero is the unset default of the marker member, not a farm id: an area that
                    // was never folded from anything must not mark farm 0 as done.
                    if (marker != 0) alreadyFolded.Add(marker);
                }
            }

            foreach (var row in legacyRows)
            {
                if (row == null) continue;
                if (alreadyFolded.Contains(row.Id)) continue;

                folded.Add(new FoldedFarmArea(cursor, AreaKind.Farming, row));
                cursor++;
            }

            return new LegacyFarmFold(folded, cursor);
        }
    }
}
