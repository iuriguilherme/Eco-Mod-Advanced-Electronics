using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// One serialized survey finding for one ore IN ONE PLOT of the area that produced it
    /// (KTD1): which plot, the dig target block, its depth, and the plot concentration
    /// (R5/R7). The persisted mirror of the Eco-free <see cref="SurveyFinding"/> — a plain
    /// <c>[Serialized]</c> class (parameterless ctor + settable props) so it survives a
    /// restart alongside the area, unlike the in-memory <see cref="SurveyRecord"/> it is
    /// derived from.
    ///
    /// Rows are per plot rather than per area so R16 can return the plots whose ground
    /// changed to unsurveyed and R20 can preserve the plots an edit retains, each without
    /// touching the rest. The area totals the readouts show are re-derived from these rows
    /// at read time by <see cref="SurveyRecord.AreaTotals"/>.
    ///
    /// The plot is two plain ints, matching how <see cref="SurveyAreaEntry.PlotCoords"/>
    /// already flattens: the class stays flat primitives, which is what makes its
    /// serializability not in question.
    /// </summary>
    [Serialized]
    public class OreFindingSnapshot
    {
        /// <summary>Plot x of the plot these counts were accumulated in.</summary>
        [Serialized] public int PlotX { get; set; }

        /// <summary>Plot z of the plot these counts were accumulated in.</summary>
        [Serialized] public int PlotZ { get; set; }

        [Serialized] public string OreType { get; set; }
        [Serialized] public int Count { get; set; }
        [Serialized] public int X { get; set; }
        [Serialized] public int Y { get; set; }
        [Serialized] public int Z { get; set; }
        [Serialized] public int DepthBelowSurface { get; set; }
        [Serialized] public int DepthMax { get; set; }
        [Serialized] public float Concentration { get; set; }

        public OreFindingSnapshot() { }

        public static OreFindingSnapshot From(SurveyFinding f) => new OreFindingSnapshot
        {
            PlotX = f.Plot.X,
            PlotZ = f.Plot.Z,
            OreType = f.OreType,
            Count = f.Count,
            X = f.Position.X,
            Y = f.Position.Y,
            Z = f.Position.Z,
            DepthBelowSurface = f.DepthBelowSurface,
            DepthMax = f.DepthMax,
            Concentration = f.Concentration,
        };

        /// <summary>The plot this row belongs to.</summary>
        public PlotCoord Plot => new PlotCoord(this.PlotX, this.PlotZ);

        /// <summary>Back to the Eco-free per-plot row shape the projection and readouts consume.</summary>
        public SurveyFinding ToSurveyFinding(int areaId) =>
            SurveyFinding.CreateInPlot(areaId, this.Plot, this.OreType, this.Count, new BlockPos(this.X, this.Y, this.Z), this.DepthBelowSurface, this.DepthMax, this.Concentration);
    }

    /// <summary>
    /// The Eco-side serialized record of one dock-owned survey area (U4, R1a/R2a/R3):
    /// a dock-local id, a player-facing name, and the drawn plots. Owned by the
    /// <see cref="DroneDockObject"/> that created it (KTD9) — there is no mod-wide
    /// registry — so it persists exactly because the dock does, and is discarded with the
    /// dock.
    ///
    /// Plots are stored as a FLATTENED <see cref="PlotCoords"/> list (x0, z0, x1, z1, ...)
    /// of plain ints rather than a list of a coordinate struct: Eco's <c>Vector2i</c> is
    /// not <c>[Serialized]</c> and nothing in the game source serializes a list of it, so
    /// per the U4 plan the plot set is stored in a form whose serializability is not in
    /// question. <see cref="ToSurveyArea"/> projects this into the Eco-free
    /// <see cref="SurveyArea"/> (U2) for membership tests and the plot cap.
    /// </summary>
    [Serialized]
    public class SurveyAreaEntry
    {
        /// <summary>Dock-local id, assigned by the owning dock. Stable across renames; identifies the area for assignment.</summary>
        [Serialized] public int Id { get; set; }

        /// <summary>Player-facing name. Not unique — two areas may share a name and stay distinct by <see cref="Id"/>.</summary>
        [Serialized] public string Name { get; set; }

        /// <summary>
        /// Drawn plots, flattened as consecutive (x, z) pairs. Even length by construction.
        /// A <see cref="ThreadSafeList{T}"/>, not a plain <c>List</c>: Eco's serializer rejects a
        /// non-immutable <c>[Serialized]</c> member ("Attempting to serialize non-immutable
        /// member ... Either make immutable or add [ThreadSafe]") and fails server init.
        /// </summary>
        [Serialized] public ThreadSafeList<int> PlotCoords { get; set; } = new();

        /// <summary>
        /// Bumped every time this area's geometry is set or redrawn (U8, KTD2). A mining
        /// dock's <c>MiningAreaRef</c> stores the epoch observed at assignment time, so a
        /// later redraw of THIS area -- whether or not it is the source dock's own
        /// currently-assigned area -- invalidates the mining job the same way a delete
        /// does, without the mining dock needing to compare geometry itself.
        /// </summary>
        [Serialized] public int Epoch { get; set; }

        /// <summary>
        /// This area's survey findings as one row per (plot, ore) (KTD1), persisted with the area
        /// (KTD11 design change): available until the area is deleted or edited. Reassigning the
        /// drone away and back does NOT clear them — they belong to the area, not the drone or the
        /// dock's current assignment. Cleared by <see cref="SetPlots"/> (an edit redraws the
        /// geometry, so it is effectively a new area), by the owning dock on delete, and by a
        /// NEWLY STARTED resurvey before the drone samples anything (R10) — which is what makes a
        /// resurvey report the ground as it is now rather than re-stating what the last pass
        /// found. A pass merely RESUMING one that stopped is not a new start and does not clear
        /// (R25).
        ///
        /// Read through <see cref="ReadFindings()"/> for the area totals or
        /// <see cref="ReadFindings(PlotCoord)"/> for one plot's rows, never directly: those are
        /// what apply the KTD1 upgrade before handing anything back.
        /// </summary>
        [Serialized] public ThreadSafeList<OreFindingSnapshot> Findings { get; set; } = new();

        /// <summary>
        /// Which shape <see cref="Findings"/> is stored in (KTD1). A save written before U1 has no
        /// value for this and loads as 0, which <see cref="FindingsVersion.IsStale"/> reads as a
        /// pre-U1 area whose rows are discarded on first read — a server that upgrades resurveys
        /// once. The marker is explicit rather than inferred from an absent plot, because a pre-U1
        /// row loads as plot (0,0) and (0,0) is a real plot near the world origin.
        /// </summary>
        [Serialized] public int FindingsShapeVersion { get; set; }

        /// <summary>Fraction of this area surveyed, 0-100 (R7a). Persisted with the findings.</summary>
        [Serialized] public float CoveragePercent { get; set; }

        /// <summary>How deep below the surface the survey scanned, in blocks (the drone sensor's reach).
        /// 0 until surveyed. Tells the player how far down was actually looked into.</summary>
        [Serialized] public int SurveyDepth { get; set; }

        /// <summary>Median surface height across the surveyed columns; meaningful when <see cref="SurveyDepth"/> > 0.</summary>
        [Serialized] public int MedianSurface { get; set; }

        /// <summary>
        /// Per-plot surveyed stamps (KTD12, R41), flattened as (x, z, stamp) triples --
        /// the persisted mirror of the live <see cref="PlotStampAccumulator"/> the sweep
        /// writes into. Compared against this area's <see cref="MinedStamps"/>
        /// (<see cref="PlotFreshness.IsMineable"/>) to decide which plots a mining job may
        /// work. Follows the same lifecycle as <see cref="Findings"/>: cleared on a redraw
        /// or delete, since a plot's old stamp says nothing about the new geometry -- which
        /// is exactly where the mined stamps beside them part company (R13).
        /// </summary>
        [Serialized] public ThreadSafeList<long> SurveyedStamps { get; set; } = new();

        /// <summary>
        /// Per-plot MINED stamps (U2, R1), in exactly the shape <see cref="SurveyedStamps"/>
        /// uses -- flattened (x, z, stamp) triples projected from and rehydrated into a
        /// <see cref="PlotStampAccumulator"/> (KTD2). These used to live on the mining dock, one
        /// list per dock, which meant two docks pointed at one area each held a private opinion
        /// about what had been dug and the second one re-dug ground the first had taken. They are
        /// the area's record now: a stamp is a fact about the GROUND, so every dock that can see
        /// the area reads the same one.
        ///
        /// Unlike the surveyed stamps this list is NOT cleared by <see cref="ClearFindings"/>
        /// (R13): a resurvey discards what the old survey claimed to find, but the digging
        /// actually happened and the record of when is what makes the resurveyed area mineable
        /// again rather than merely un-mined. What stays per dock is the mining JOB and its
        /// ledger (R2).
        /// </summary>
        [Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();

        /// <summary>
        /// The plots this area's last survey observed DOWN AT BEDROCK (U4, R7/R8/R26),
        /// flattened as consecutive (x, z) pairs exactly the way <see cref="PlotCoords"/>
        /// already flattens — no new persistence shape is invented for this. A plot is listed
        /// only when every column in it rested on the impenetrable world floor; the fold from
        /// columns to plots is <see cref="SurveyRecord.PlotRestsOnBedrock"/>'s, and only its
        /// result is stored.
        ///
        /// This is what `[cleared]` and `[empty]` are tested against, and it is an OBSERVATION
        /// rather than a flag (R8): it is written by a survey pass and cleared by
        /// <see cref="ClearFindings"/> alongside the findings (R10), so a player who fills a
        /// cleared area in has only to let it be resurveyed for it to rejoin the ramp. That is
        /// the opposite of <see cref="MinedStamps"/>, which survive a resurvey because digging
        /// having happened is not a claim a later survey can falsify.
        ///
        /// A <see cref="ThreadSafeList{T}"/> of plain ints, like every other serialized
        /// collection here: Eco's serializer rejects a non-immutable <c>[Serialized]</c> member
        /// and fails server init.
        /// </summary>
        [Serialized] public ThreadSafeList<int> BedrockPlotCoords { get; set; } = new();

        /// <summary>
        /// The live sample record of the pass currently running on this area (U7, R25), flattened
        /// as consecutive FIVE-int rows: x, z, surfaceY, sampledBlocks, bedrockState.
        ///
        /// One row per COLUMN, not per block. The sensor scans a column top-down in a single call,
        /// so the surface height plus the number of blocks taken names exactly the same block set
        /// that listing every block would, at roughly a fifteenth of the size — and this is by
        /// some distance the largest structure the mod persists, so the shape is the reason it is
        /// affordable at all. <see cref="SurveyRecord.RestorePassColumn"/> replays each row
        /// downward from its surface, rebuilding the sampled-block set, the per-plot sampled
        /// counts, and therefore the coverage the stopped pass had reached.
        ///
        /// Two sentinel encodings keep the row flat primitives, matching every other serialized
        /// collection here: <see cref="NoSurfaceRecorded"/> in the surfaceY slot means no surface
        /// was observed, and bedrockState is 0 for "not observed", 1 for "above bedrock", 2 for
        /// "at bedrock" — "not observed" and "observed false" are different answers to the fold
        /// in <see cref="SurveyRecord.PlotRestsOnBedrock"/>.
        ///
        /// Persisting this REVERSES the design decision <see cref="SurveyRecord"/> used to state
        /// in its own header — that the record was session-scoped and never serialized. The
        /// reversal is safe only because a newly started pass clears it first (R10): without that
        /// clear, the record's per-block idempotency would survive a restart and the intermittent
        /// stale-findings fault would become permanent.
        /// </summary>
        [Serialized] public ThreadSafeList<int> SweepColumns { get; set; } = new();

        /// <summary>Which plot of the running pass's raster order the sweep is on (U7, R25).</summary>
        [Serialized] public int SweepPlotIndex { get; set; }

        /// <summary>Which column of <see cref="SweepPlotIndex"/>'s plot the sweep is on (U7, R25).</summary>
        [Serialized] public int SweepColumnCursor { get; set; }

        /// <summary>
        /// True while a survey pass is part-way through this area. This is what tells a dispatch
        /// apart from a NEWLY STARTED resurvey (R10) and a RESUMING one (R25) — the one
        /// distinction U7 exists to draw. Set when a pass begins sampling, cleared when the sweep
        /// finishes or the area is cleared, and explicit rather than inferred from a non-zero
        /// cursor, because a pass stopped during its very first plot has a cursor of (0, 0) and
        /// must still resume rather than clear what it already sampled.
        /// </summary>
        [Serialized] public bool SweepInProgress { get; set; }

        /// <summary>Sentinel in the surfaceY slot of a <see cref="SweepColumns"/> row: no surface was recorded for that column.</summary>
        public const int NoSurfaceRecorded = int.MinValue;

        /// <summary>Parameterless constructor required by the Eco serializer.</summary>
        public SurveyAreaEntry() { }

        public SurveyAreaEntry(int id, string name, IEnumerable<PlotCoord> plots)
        {
            this.Id = id;
            this.Name = name;
            this.SetPlots(plots);
        }

        /// <summary>Number of plots this area covers (the value R1b's tier cap is checked against).</summary>
        public int PlotCount => this.PlotCoords.Count / 2;

        /// <summary>
        /// Replaces the stored plots with <paramref name="plots"/>, flattening to (x, z) pairs.
        /// Also clears any findings: a redraw changes the area's geometry, so the old survey no
        /// longer describes it — the drone re-surveys the new shape from scratch (KTD11).
        /// </summary>
        public void SetPlots(IEnumerable<PlotCoord> plots)
        {
            this.PlotCoords = new ThreadSafeList<int>();
            foreach (var p in plots)
            {
                this.PlotCoords.Add(p.X);
                this.PlotCoords.Add(p.Z);
            }
            this.Epoch++;
            this.ClearFindings();
        }

        /// <summary>
        /// Replaces this area's persisted findings from a fresh survey pass. <paramref name="findings"/>
        /// are the per-plot rows <see cref="SurveyRecord.Findings(int)"/> projects — one per
        /// (plot, ore) — and writing them stamps the current shape version, so the rows this area
        /// now holds are never mistaken for a pre-U1 save.
        /// </summary>
        public void SetFindings(IEnumerable<SurveyFinding> findings, float coveragePercent, int surveyDepth, int medianSurface)
        {
            var snapshot = new ThreadSafeList<OreFindingSnapshot>();
            foreach (var f in findings.Where(f => f.Found))
                snapshot.Add(OreFindingSnapshot.From(f));
            this.Findings = snapshot;
            this.FindingsShapeVersion = FindingsVersion.Current;
            this.CoveragePercent = coveragePercent;
            this.SurveyDepth = surveyDepth;
            this.MedianSurface = medianSurface;
        }

        /// <summary>
        /// Discards this area's findings, surveyed stamps and at-bedrock observations (delete, an
        /// edit that redraws the geometry, or a newly started resurvey).
        ///
        /// The bedrock observations go with the findings, not with the mined stamps (R10): they
        /// are a claim about what the ground is like NOW, so a pass that has not yet re-observed
        /// a plot must not be able to answer for it. This is what makes AE5 work — a `[cleared]`
        /// area a player has filled in stops reading cleared as soon as it is resurveyed.
        ///
        /// <see cref="MinedStamps"/> is deliberately NOT cleared here (R13). The findings are a
        /// claim about what is in the ground and a resurvey replaces them; the mined stamps are a
        /// record that digging happened, which no later survey makes untrue. Keeping them is also
        /// what makes AE3 work: an area mined at 200 and resurveyed at 300 is mineable again
        /// because 300 postdates a 200 that is still there to be postdated.
        ///
        /// The running pass goes too (U7). A cleared area's sweep cursor points into a plot list
        /// that no longer describes it, and the sampled-block set it names is exactly what R10
        /// requires a newly started resurvey to discard.
        /// </summary>
        public void ClearFindings()
        {
            this.Findings = new ThreadSafeList<OreFindingSnapshot>();
            // An empty list is trivially in the current shape, so stamping here is what keeps the
            // KTD1 upgrade a one-shot: a cleared area is never re-cleared on every later read.
            this.FindingsShapeVersion = FindingsVersion.Current;
            this.CoveragePercent = 0f;
            this.SurveyDepth = 0;
            this.MedianSurface = 0;
            this.SurveyedStamps = new ThreadSafeList<long>();
            this.BedrockPlotCoords = new ThreadSafeList<int>();
            this.ClearSweep();
        }

        /// <summary>
        /// Forgets the pass in flight: its per-column sample record, its cursor, and the flag that
        /// says one is running. A dispatch after this reads as a NEWLY STARTED pass (R10) rather
        /// than a resuming one (R25).
        /// </summary>
        public void ClearSweep()
        {
            this.SweepColumns = new ThreadSafeList<int>();
            this.SweepPlotIndex = 0;
            this.SweepColumnCursor = 0;
            this.SweepInProgress = false;
        }

        /// <summary>
        /// Projects the live record's pass state for this area — its per-column samples and its
        /// sweep cursor — onto the persisted rows, so a pass that stops resumes where it stopped.
        /// The two are written together on purpose: a cursor saved without its samples resumes
        /// past ground whose coverage was lost, and samples saved without their cursor re-fly
        /// ground the record already treats as done.
        /// </summary>
        public void SetSweep(SurveyRecord record)
        {
            if (record == null)
                return;

            var flat = new ThreadSafeList<int>();
            foreach (var column in record.PassColumns(this.Id))
            {
                flat.Add(column.X);
                flat.Add(column.Z);
                flat.Add(column.SurfaceY ?? NoSurfaceRecorded);
                flat.Add(column.SampledBlocks);
                flat.Add(column.RestsOnBedrock.HasValue ? (column.RestsOnBedrock.Value ? 2 : 1) : 0);
            }
            this.SweepColumns = flat;

            var cursor = record.SweepCursorFor(this.Id);
            this.SweepPlotIndex = cursor.PlotIndex;
            this.SweepColumnCursor = cursor.ColumnCursor;
        }

        /// <summary>This area's persisted per-column pass record, unflattened.</summary>
        public IEnumerable<SurveyColumnState> ReadSweepColumns()
        {
            for (var i = 0; i + 4 < this.SweepColumns.Count; i += 5)
            {
                var surface = this.SweepColumns[i + 2];
                var bedrock = this.SweepColumns[i + 4];
                yield return new SurveyColumnState(
                    this.SweepColumns[i],
                    this.SweepColumns[i + 1],
                    surface == NoSurfaceRecorded ? (int?)null : surface,
                    this.SweepColumns[i + 3],
                    bedrock == 0 ? (bool?)null : bedrock == 2);
            }
        }

        /// <summary>
        /// Rebuilds <paramref name="record"/>'s view of this area from what is persisted — the
        /// pass's per-column samples, its findings rows, and its sweep cursor — so a resumed pass
        /// carries the coverage and the findings the stopped one reached (R25).
        ///
        /// Restoring the findings rows is not decoration. The dock projects the LIVE record back
        /// onto this area every readout tick, replacing what is stored; a record rehydrated with
        /// samples but no findings would report coverage without material and overwrite a
        /// perfectly good snapshot with an empty one on the first tick after a restart.
        /// </summary>
        public void RestoreInto(SurveyRecord record)
        {
            if (record == null)
                return;

            // Findings first: reading them applies the KTD1 shape upgrade, which on a pre-U1 save
            // clears this area — including the sweep rows read below.
            var rows = this.ReadFindingRows().ToList();
            var columns = this.ReadSweepColumns().ToList();

            record.ClearArea(this.Id);
            foreach (var column in columns)
                record.RestorePassColumn(this.Id, column);
            foreach (var row in rows)
                record.RestoreFinding(this.Id, row);
            record.SetSweepCursor(this.Id, this.SweepPlotIndex, this.SweepColumnCursor);
        }

        /// <summary>
        /// Discards findings written in a pre-U1 shape (KTD1). Called before every read, so the
        /// upgrade happens on the first read after a load and never again. Findings stored per
        /// area cannot be attributed to a plot, so there is nothing to migrate — the area reads as
        /// unsurveyed and is surveyed once more.
        /// </summary>
        private void UpgradeFindingsIfStale()
        {
            if (!FindingsVersion.IsStale(this.FindingsShapeVersion))
                return;

            this.ClearFindings();
        }

        /// <summary>
        /// Replaces this area's persisted surveyed stamps from the live accumulator's
        /// current snapshot. Skips the write when <paramref name="stamps"/> is empty, so a
        /// just-restarted, not-yet-repopulated accumulator never overwrites a populated
        /// persisted snapshot before the drone has re-surveyed.
        /// </summary>
        public void SetSurveyedStamps(PlotStampAccumulator stamps)
        {
            if (stamps == null || stamps.IsEmpty)
                return;

            var flat = new ThreadSafeList<long>();
            foreach (var entry in stamps.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            this.SurveyedStamps = flat;
        }

        /// <summary>This area's persisted surveyed stamps, rehydrated into a live accumulator.</summary>
        public PlotStampAccumulator ReadSurveyedStamps()
        {
            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < this.SurveyedStamps.Count; i += 3)
                entries[new PlotCoord((int)this.SurveyedStamps[i], (int)this.SurveyedStamps[i + 1])] = this.SurveyedStamps[i + 2];
            return PlotStampAccumulator.FromSnapshot(entries);
        }

        /// <summary>Records <paramref name="plot"/> surveyed at <paramref name="stampValue"/> and persists it immediately, mirroring <see cref="RecordMinedPlot"/>.</summary>
        public void RecordSurveyedPlot(PlotCoord plot, long stampValue)
        {
            var accumulator = this.ReadSurveyedStamps();
            accumulator.Record(plot, stampValue);
            this.SetSurveyedStamps(accumulator);
        }

        /// <summary>
        /// Replaces this area's persisted mined stamps from the live accumulator's current
        /// snapshot. The exact mirror of <see cref="SetSurveyedStamps"/>, empty guard included:
        /// an empty accumulator never overwrites a populated persisted snapshot.
        /// </summary>
        public void SetMinedStamps(PlotStampAccumulator stamps)
        {
            if (stamps == null || stamps.IsEmpty)
                return;

            var flat = new ThreadSafeList<long>();
            foreach (var entry in stamps.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            this.MinedStamps = flat;
        }

        /// <summary>
        /// This area's persisted mined stamps, rehydrated into a live accumulator (U2, R1).
        /// Every dock reading this area gets the same answer, which is the whole point of the
        /// record having moved here: <see cref="PlotFreshness.IsMineable"/> now compares two
        /// stamps that both came off one object.
        /// </summary>
        public PlotStampAccumulator ReadMinedStamps()
        {
            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < this.MinedStamps.Count; i += 3)
                entries[new PlotCoord((int)this.MinedStamps[i], (int)this.MinedStamps[i + 1])] = this.MinedStamps[i + 2];
            return PlotStampAccumulator.FromSnapshot(entries);
        }

        /// <summary>
        /// Replaces this area's persisted at-bedrock plots with what the pass just observed
        /// (U4). <paramref name="plots"/> are the ones <see cref="SurveyRecord.BedrockPlots"/>
        /// projects — every column in each of them walked down to the world floor.
        ///
        /// Deliberately UNGUARDED, unlike <see cref="SetSurveyedStamps"/> and
        /// <see cref="SetMinedStamps"/>: an empty result here is a real answer ("this pass found
        /// nothing at bedrock"), not an unpopulated accumulator. R8 requires a survey that finds
        /// ground standing above bedrock to stop restating `[cleared]`, and an empty-write guard
        /// would make a once-cleared area permanently cleared — exactly the stored-flag
        /// behaviour KTD3 rejects. The caller only reaches this once the pass has coverage.
        /// </summary>
        public void SetBedrockPlots(IEnumerable<PlotCoord> plots)
        {
            var flat = new ThreadSafeList<int>();
            foreach (var p in plots)
            {
                flat.Add(p.X);
                flat.Add(p.Z);
            }
            this.BedrockPlotCoords = flat;
        }

        /// <summary>This area's persisted at-bedrock plots (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> ReadBedrockPlots()
        {
            for (var i = 0; i + 1 < this.BedrockPlotCoords.Count; i += 2)
                yield return new PlotCoord(this.BedrockPlotCoords[i], this.BedrockPlotCoords[i + 1]);
        }

        /// <summary>True when the last survey observed <paramref name="plot"/> down at bedrock.</summary>
        public bool PlotRestsOnBedrock(PlotCoord plot)
        {
            for (var i = 0; i + 1 < this.BedrockPlotCoords.Count; i += 2)
                if (this.BedrockPlotCoords[i] == plot.X && this.BedrockPlotCoords[i + 1] == plot.Z)
                    return true;
            return false;
        }

        /// <summary>
        /// True when EVERY plot of this area was observed down at bedrock — the floor test R7
        /// and R26 put `[cleared]` and `[empty]` behind. False for an area with no plots, which
        /// is a degenerate area rather than an exhausted one.
        /// </summary>
        public bool RestsOnBedrock()
        {
            if (this.PlotCount == 0)
                return false;

            foreach (var plot in this.Plots())
                if (!this.PlotRestsOnBedrock(plot))
                    return false;
            return true;
        }

        /// <summary>
        /// Records <paramref name="plot"/> mined at <paramref name="stampValue"/> and persists it
        /// immediately, mirroring <see cref="RecordSurveyedPlot"/>. Unlike the survey side there
        /// is no live/throttled projection step, since a mined stamp is written once per plot
        /// rather than accumulated per column.
        /// </summary>
        public void RecordMinedPlot(PlotCoord plot, long stampValue)
        {
            var accumulator = this.ReadMinedStamps();
            accumulator.Record(plot, stampValue);
            this.SetMinedStamps(accumulator);
        }

        /// <summary>
        /// The area totals — one finding per ore across the whole area — re-derived from the
        /// per-plot rows at read time (KTD1), in the Eco-free shape the readout formatter
        /// consumes. This is what the survey tab, the roster line and the chat readouts show, and
        /// the figures are the same ones they showed before findings became per-plot.
        /// </summary>
        public IEnumerable<SurveyFinding> ReadFindings() => SurveyRecord.AreaTotals(this.ReadFindingRows());

        /// <summary>
        /// Every persisted per-plot row, unfolded — the shape the record itself projects (KTD1),
        /// before <see cref="SurveyRecord.AreaTotals"/> folds it for display. This is what
        /// <see cref="RestoreInto"/> hands back to the live record, and it is the read that
        /// applies the KTD1 shape upgrade for every other reader.
        /// </summary>
        public IEnumerable<SurveyFinding> ReadFindingRows()
        {
            this.UpgradeFindingsIfStale();
            return this.Findings.Select(s => s.ToSurveyFinding(this.Id)).ToList();
        }

        /// <summary>
        /// The rows for one plot — the per-plot read R16's invalidation and R20's preservation are
        /// decided against. Empty for a plot this area has no findings for.
        /// </summary>
        public IEnumerable<SurveyFinding> ReadFindings(PlotCoord plot)
        {
            this.UpgradeFindingsIfStale();
            return this.Findings
                .Where(s => s.PlotX == plot.X && s.PlotZ == plot.Z)
                .Select(s => s.ToSurveyFinding(this.Id))
                .ToList();
        }

        /// <summary>The stored plots as <see cref="PlotCoord"/>s (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> Plots()
        {
            for (var i = 0; i + 1 < this.PlotCoords.Count; i += 2)
                yield return new PlotCoord(this.PlotCoords[i], this.PlotCoords[i + 1]);
        }

        /// <summary>Projects this entry into the Eco-free <see cref="SurveyArea"/> for membership and cap logic (U2).</summary>
        public SurveyArea ToSurveyArea() => new SurveyArea(this.Id, this.Name, this.Plots());
    }
}
