using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// The dock's in-memory survey record (KTD2/KTD3): findings accumulated as
    /// the drone roams, attributed to the survey area that produced them (R3a),
    /// standardized for both the player readout and a future mining drone (R5/R6).
    /// Replaces the old <c>SurveyGrid</c> density model — same proven arithmetic
    /// (concentration is ore-blocks / sampled-blocks, argmax by ratio not raw
    /// count; per-block sampling is idempotent), but the unit of aggregation is a
    /// property plot and the reported location is a precise block rather than a
    /// cell coordinate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aggregation window.</b> Concentration needs a region — a lone block is
    /// ore or not, with no ratio. That region is one plot (<c>plotSize</c> world
    /// blocks per axis). A finding's <see cref="SurveyFinding.Concentration"/> is
    /// the ratio for the plot it came from; its
    /// <see cref="SurveyFinding.Position"/> is the shallowest ore block in that
    /// plot — the precise dig target. This keeps "finer than a plot" (the
    /// position) and "how concentrated" (the plot ratio) both honest.
    /// </para>
    /// <para>
    /// <b>Sampling idempotency.</b> <see cref="RecordSample"/> is idempotent per
    /// exact block position, exactly as the old grid was: a roaming drone
    /// revisits ground it has already covered, and per-call counting would let
    /// dwell time silently inflate both coverage and apparent concentration
    /// without observing anything new about the world.
    /// </para>
    /// <para>
    /// <b>Persisted with the area, since U7 (R25).</b> This paragraph used to say the
    /// opposite — that the record was session-scoped by design and never serialized. That
    /// decision is reversed. A pass that stops before finishing has to resume from where it
    /// stopped rather than re-fly ground it already covered, and the sampled-block set is
    /// what "already covered" means, so the set and the sweep cursor persist together on the
    /// survey area (<c>SurveyAreaEntry.SweepColumns</c>, projected by
    /// <see cref="PassColumns"/> and rebuilt by <see cref="RestorePassColumn"/>).
    /// </para>
    /// <para>
    /// That reversal is only safe because a NEWLY STARTED pass clears this record for the
    /// area first (R10). Without the clear, <see cref="RecordSample"/>'s idempotency — a
    /// block already seen is a no-op — would survive the restart, and the stale-findings
    /// fault that used to be intermittent (it healed whenever the server restarted) would
    /// become permanent. Clear-on-start and persist-across-restart are one decision; neither
    /// half is correct on its own.
    /// </para>
    /// <para>
    /// <b>Per area, not per record.</b> Everything here — samples, columns, cursor — is
    /// keyed by area id, including the sampled-block dedupe set. Two areas may cover the
    /// same ground (R35), and an area-blind dedupe set meant clearing one area could not
    /// drop a block another area's geometry still covered.
    /// </para>
    /// </remarks>
    public sealed class SurveyRecord
    {
        private readonly int _plotSize;

        // areaId -> the exact blocks sampled for THAT area. Per area rather than global so a
        // clear can actually drop them (R10) and so two overlapping areas each sample the
        // ground they cover.
        private readonly Dictionary<int, HashSet<BlockPos>> _sampledBlocks =
            new Dictionary<int, HashSet<BlockPos>>();

        // areaId -> plot -> aggregated counts for that plot.
        private readonly Dictionary<int, Dictionary<PlotCoord, PlotData>> _byArea =
            new Dictionary<int, Dictionary<PlotCoord, PlotData>>();

        // areaId -> column (x, z) -> everything the pass learned about that column: its surface
        // height, how many of its blocks were sampled, and whether it rests on the world floor.
        // One map rather than three because a column is the unit the sweep advances in, and the
        // persisted projection is one row per column (PassColumns).
        private readonly Dictionary<int, Dictionary<(int X, int Z), ColumnState>> _columnsByArea =
            new Dictionary<int, Dictionary<(int X, int Z), ColumnState>>();

        // areaId -> how far the sweep got. Kept beside the samples deliberately (U7): resuming
        // without the cursor re-flies ground the sampled set already treats as done, and
        // resuming with a cursor but no samples reports coverage the pass never earned.
        private readonly Dictionary<int, SweepCursor> _cursorByArea =
            new Dictionary<int, SweepCursor>();

        public SurveyRecord(int plotSize)
        {
            if (plotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(plotSize), "plotSize must be positive.");

            _plotSize = plotSize;
        }

        /// <summary>
        /// Records one sampled block, attributed to <paramref name="areaId"/> and
        /// to the plot its (x, z) column falls in. <paramref name="oreType"/> null
        /// or empty means "sampled, no ore" — it still counts toward the plot's
        /// coverage and concentration denominator but toward no ore's numerator.
        /// Idempotent per exact (x, y, z): a repeat is a no-op regardless of the
        /// ore type it carries (the position is the dedupe key).
        /// </summary>
        /// <param name="depthBelowSurface">Blocks below the surface (0 = surface). The shallowest sighting per ore per plot is kept as the dig target.</param>
        public void RecordSample(int x, int y, int z, string oreType, int depthBelowSurface, int areaId)
        {
            if (!Sampled(areaId).Add(new BlockPos(x, y, z)))
                return;

            var plot = PlotCoord.FromWorldColumn(x, z, _plotSize);

            if (!_byArea.TryGetValue(areaId, out var plots))
            {
                plots = new Dictionary<PlotCoord, PlotData>();
                _byArea[areaId] = plots;
            }

            if (!plots.TryGetValue(plot, out var data))
            {
                data = new PlotData();
                plots[plot] = data;
            }

            data.SampledCount++;
            Column(areaId, x, z).SampledBlocks++;
            if (!string.IsNullOrEmpty(oreType))
                data.RecordOre(oreType, new BlockPos(x, y, z), depthBelowSurface);
        }

        private HashSet<BlockPos> Sampled(int areaId)
        {
            if (!_sampledBlocks.TryGetValue(areaId, out var blocks))
            {
                blocks = new HashSet<BlockPos>();
                _sampledBlocks[areaId] = blocks;
            }
            return blocks;
        }

        private ColumnState Column(int areaId, int x, int z)
        {
            if (!_columnsByArea.TryGetValue(areaId, out var columns))
            {
                columns = new Dictionary<(int X, int Z), ColumnState>();
                _columnsByArea[areaId] = columns;
            }
            if (!columns.TryGetValue((x, z), out var column))
            {
                column = new ColumnState();
                columns[(x, z)] = column;
            }
            return column;
        }

        /// <summary>
        /// Records the surface height of column (<paramref name="x"/>, <paramref name="z"/>) in
        /// <paramref name="areaId"/>, so <see cref="MedianSurfaceLevel"/> can report the area's median
        /// terrain elevation. Idempotent per column (one surface per column).
        /// </summary>
        public void RecordSurface(int areaId, int x, int z, int surfaceY)
            => Column(areaId, x, z).SurfaceY = surfaceY;

        /// <summary>
        /// The median surface height across the columns sampled in <paramref name="areaId"/>, or null
        /// when none have been recorded. Median (not mean) so a cliff or pit column doesn't skew the
        /// reported terrain level.
        /// </summary>
        public int? MedianSurfaceLevel(int areaId)
        {
            if (!_columnsByArea.TryGetValue(areaId, out var columns))
                return null;

            var sorted = columns.Values.Where(c => c.SurfaceY.HasValue).Select(c => c.SurfaceY.Value).OrderBy(v => v).ToList();
            if (sorted.Count == 0)
                return null;

            var mid = sorted.Count / 2;
            return (sorted.Count % 2 == 1)
                ? sorted[mid]
                : (int)System.Math.Round((sorted[mid - 1] + sorted[mid]) / 2.0);
        }

        /// <summary>
        /// Records whether column (<paramref name="x"/>, <paramref name="z"/>) of
        /// <paramref name="areaId"/> rests on the impenetrable world floor — the answer
        /// <see cref="BedrockWalk.ColumnRestsOnBedrock"/> produced for it (U4, KTD4).
        ///
        /// Unlike <see cref="RecordSample"/> this is LAST-WRITE-WINS rather than
        /// first-write-wins: the ground under a column changes between passes — that is the
        /// whole point of a resurvey — so the newest observation is the true one, exactly as
        /// <see cref="RecordSurface"/> already treats a column's surface height.
        /// </summary>
        public void RecordColumnBedrock(int areaId, int x, int z, bool restsOnBedrock)
            => Column(areaId, x, z).RestsOnBedrock = restsOnBedrock;

        /// <summary>
        /// True when <paramref name="plot"/> of <paramref name="areaId"/> is down at bedrock:
        /// every one of its <c>plotSize * plotSize</c> columns has been observed AND every
        /// observation was positive (KTD4).
        ///
        /// Requiring the plot to be observed in full, not merely observed consistently, is
        /// what keeps a pass that stopped part-way through a plot from claiming it: an
        /// unproven column is not a column at bedrock, and `[cleared]` is derived from this
        /// answer (R7, R8).
        /// </summary>
        public bool PlotRestsOnBedrock(int areaId, PlotCoord plot)
        {
            if (!_columnsByArea.TryGetValue(areaId, out var columns))
                return false;

            var observed = 0;
            foreach (var entry in columns)
            {
                if (!entry.Value.RestsOnBedrock.HasValue)
                    continue;
                if (!PlotCoord.FromWorldColumn(entry.Key.X, entry.Key.Z, _plotSize).Equals(plot))
                    continue;
                if (!entry.Value.RestsOnBedrock.Value)
                    return false;
                observed++;
            }

            return observed == _plotSize * _plotSize;
        }

        /// <summary>
        /// Every plot of <paramref name="areaId"/> that reads as at bedrock, in no particular
        /// order — the projection the Eco side persists on the area beside the findings rows.
        /// </summary>
        public IEnumerable<PlotCoord> BedrockPlots(int areaId)
        {
            if (!_columnsByArea.TryGetValue(areaId, out var columns))
                yield break;

            var candidates = columns
                .Where(c => c.Value.RestsOnBedrock.HasValue)
                .Select(c => PlotCoord.FromWorldColumn(c.Key.X, c.Key.Z, _plotSize))
                .Distinct()
                .ToList();

            foreach (var plot in candidates)
                if (PlotRestsOnBedrock(areaId, plot))
                    yield return plot;
        }

        /// <summary>
        /// Every ore type with at least one sample in <paramref name="areaId"/>,
        /// in no particular order. Lets a readout enumerate ore types without a
        /// hardcoded list.
        /// </summary>
        public IEnumerable<string> SampledOreTypes(int areaId)
        {
            if (!_byArea.TryGetValue(areaId, out var plots))
                return Enumerable.Empty<string>();

            return plots.Values.SelectMany(p => p.OreTypes).Distinct();
        }

        /// <summary>
        /// The area-total finding for <paramref name="oreType"/> in <paramref name="areaId"/> (KTD2):
        /// total block count across every plot, the shallowest occurrence in the area as the dig
        /// target, and the depth range (shallowest–deepest). Concentration is retained as a secondary
        /// area-level ratio (material blocks / sampled blocks). Returns <see cref="SurveyFinding.NotFound"/>
        /// when no sample of this material has been recorded in this area yet.
        /// </summary>
        public SurveyFinding MaterialFinding(int areaId, string oreType)
        {
            if (string.IsNullOrEmpty(oreType) || !_byArea.TryGetValue(areaId, out var plots))
                return SurveyFinding.NotFound;

            var totalCount = 0;
            var totalSampled = 0;
            var shallowestDepth = int.MaxValue;
            var deepestDepth = int.MinValue;
            var shallowestPos = default(BlockPos);
            var found = false;

            foreach (var data in plots.Values)
            {
                totalSampled += data.SampledCount;
                if (!data.TryGetOre(oreType, out var count, out var plotShallowest, out var plotShallowestPos, out var plotDeepest) || count == 0)
                    continue;

                found = true;
                totalCount += count;
                if (plotShallowest < shallowestDepth)
                {
                    shallowestDepth = plotShallowest;
                    shallowestPos = plotShallowestPos;
                }
                if (plotDeepest > deepestDepth)
                    deepestDepth = plotDeepest;
            }

            if (!found)
                return SurveyFinding.NotFound;

            var concentration = totalSampled > 0 ? (float)totalCount / totalSampled : 0f;
            return SurveyFinding.Create(areaId, oreType, totalCount, shallowestPos, shallowestDepth, deepestDepth, concentration);
        }

        /// <summary>
        /// One row per (plot, material) in <paramref name="areaId"/> (KTD1) — the shape that is
        /// persisted on the area, so a later rule can invalidate or preserve one plot's findings
        /// without touching the rest (R16, R20). Each row's <see cref="SurveyFinding.Count"/>,
        /// depth range and <see cref="SurveyFinding.Concentration"/> are that plot's alone; the
        /// area totals the readout shows are re-derived from these rows by
        /// <see cref="AreaTotals"/>. Only this area's own rows appear; another area's are never
        /// included (R3a). This is a projection change only — the live accumulator has always
        /// keyed its inner dictionary by plot.
        /// </summary>
        public IEnumerable<SurveyFinding> Findings(int areaId)
        {
            if (!_byArea.TryGetValue(areaId, out var plots))
                yield break;

            foreach (var entry in plots)
                foreach (var row in PlotRows(areaId, entry.Key, entry.Value))
                    yield return row;
        }

        /// <summary>
        /// The rows for one plot of <paramref name="areaId"/> — empty when nothing has been
        /// sampled there. The per-plot read R16's plot-level invalidation and R20's
        /// plot-level preservation are decided against.
        /// </summary>
        public IEnumerable<SurveyFinding> Findings(int areaId, PlotCoord plot)
        {
            if (!_byArea.TryGetValue(areaId, out var plots) || !plots.TryGetValue(plot, out var data))
                return Enumerable.Empty<SurveyFinding>();

            return PlotRows(areaId, plot, data);
        }

        private static IEnumerable<SurveyFinding> PlotRows(int areaId, PlotCoord plot, PlotData data)
        {
            foreach (var ore in data.OreTypes.ToList())
            {
                if (!data.TryGetOre(ore, out var count, out var shallowestDepth, out var shallowestPos, out var deepestDepth) || count == 0)
                    continue;

                var concentration = data.SampledCount > 0 ? (float)count / data.SampledCount : 0f;
                yield return SurveyFinding.CreateInPlot(areaId, plot, ore, count, shallowestPos, shallowestDepth, deepestDepth, concentration);
            }
        }

        /// <summary>
        /// Folds per-plot rows back into one area total per material — the figures the survey tab,
        /// the roster line and the chat readout have always shown, now re-derived at read time
        /// rather than stored (KTD1). Pure over the rows, so the persisted snapshot and the live
        /// record aggregate through the same code.
        /// </summary>
        /// <remarks>
        /// The total's count is the sum across plots, its position and minimum depth come from the
        /// shallowest row, and its maximum depth from the deepest. Concentration is recovered by
        /// summing each row's own sampled-block denominator (<c>count / concentration</c>), so the
        /// ratio is over the plots that actually carry the material. Before KTD1 the denominator
        /// was every sampled block in the area including ore-free plots; per-plot rows do not
        /// record ore-free plots at all, so that wider denominator no longer exists. Concentration
        /// is the secondary signal the readout does not render — <see cref="SurveyFinding.Count"/>
        /// is the headline, and it is unchanged.
        /// </remarks>
        public static IEnumerable<SurveyFinding> AreaTotals(IEnumerable<SurveyFinding> rows)
        {
            if (rows == null)
                yield break;

            var byOre = new Dictionary<string, List<SurveyFinding>>();
            var order = new List<string>();
            foreach (var row in rows)
            {
                if (!row.Found || string.IsNullOrEmpty(row.OreType))
                    continue;

                if (!byOre.TryGetValue(row.OreType, out var list))
                {
                    list = new List<SurveyFinding>();
                    byOre[row.OreType] = list;
                    order.Add(row.OreType);
                }
                list.Add(row);
            }

            foreach (var ore in order)
            {
                var list = byOre[ore];
                var areaId = list[0].AreaId;
                var totalCount = 0;
                var shallowestDepth = int.MaxValue;
                var deepestDepth = int.MinValue;
                var shallowestPos = default(BlockPos);
                var sampledDenominator = 0d;

                foreach (var row in list)
                {
                    totalCount += row.Count;
                    if (row.DepthBelowSurface < shallowestDepth)
                    {
                        shallowestDepth = row.DepthBelowSurface;
                        shallowestPos = row.Position;
                    }
                    if (row.DepthMax > deepestDepth)
                        deepestDepth = row.DepthMax;
                    if (row.Concentration > 0f)
                        sampledDenominator += row.Count / (double)row.Concentration;
                }

                var concentration = sampledDenominator > 0d ? (float)(totalCount / sampledDenominator) : 0f;
                yield return SurveyFinding.Create(areaId, ore, totalCount, shallowestPos, shallowestDepth, deepestDepth, concentration);
            }
        }

        /// <summary>
        /// How much of <paramref name="area"/> has been surveyed: the fraction of
        /// its plots with at least one recorded sample, 0..1 (R7a). Zero for an
        /// area with no samples, which is distinct from an area fully walked but
        /// containing no ore (that reads coverage 1.0 with no findings). An area
        /// with no plots reports 0 rather than dividing by zero.
        /// </summary>
        public float Coverage(SurveyArea area)
        {
            if (area == null)
                throw new ArgumentNullException(nameof(area));

            if (area.PlotCount == 0)
                return 0f;

            if (!_byArea.TryGetValue(area.Id, out var plots))
                return 0f;

            var surveyedInArea = area.EnumeratePlots().Count(plots.ContainsKey);
            return (float)surveyedInArea / area.PlotCount;
        }

        /// <summary>
        /// Discards everything this record holds about <paramref name="areaId"/> — its per-plot
        /// findings, its sampled-block set, its column observations (surface and at-bedrock) and
        /// its sweep cursor. Used when an area is deleted or redrawn, and by the R10 clear a
        /// newly started resurvey performs.
        ///
        /// Dropping the sampled-block set is the load-bearing half: <see cref="RecordSample"/> is
        /// idempotent per exact block, so a resurvey that did not clear here would be deduped
        /// against the previous pass and re-report material the ground no longer holds. Because
        /// the set is keyed by area, this drops exactly this area's blocks even where another
        /// area covers the same plots (R35).
        ///
        /// Nothing about MINED ground lives in this record, and nothing here can reach it (R13):
        /// the mined stamps are the area's, they record that digging happened, and no later
        /// survey makes that untrue.
        /// </summary>
        public void ClearArea(int areaId)
        {
            _columnsByArea.Remove(areaId);
            _cursorByArea.Remove(areaId);
            _sampledBlocks.Remove(areaId);
            _byArea.Remove(areaId);
        }

        /// <summary>
        /// Discards everything this record holds about ONE plot of <paramref name="areaId"/>
        /// (U8, R16): its findings, its column observations, and the sampled blocks that dedupe
        /// it. Used when ground the mod did not change moves under a surveyed plot, which is why
        /// it is a plot and not an area -- the rest of the area still describes its own ground,
        /// and <see cref="Coverage"/> therefore falls by that plot's share rather than to zero.
        ///
        /// <para>
        /// <b>Dropping the sampled BLOCKS is the load-bearing half</b>, exactly as it is in
        /// <see cref="ClearArea"/>. <see cref="RecordSample"/> is idempotent per exact block, so a
        /// plot whose findings are gone but whose blocks are still in the set is skipped by every
        /// later pass -- a plot that reads unsurveyed and can never be re-read. Since U7 that set
        /// survives a restart, so getting this half wrong makes the stale-findings fault durable
        /// rather than intermittent.
        /// </para>
        /// <para>
        /// Scoped to one area: two areas may cover the same ground (R35), and the sampled set is
        /// keyed by area precisely so one area's reset cannot reach into another's.
        /// </para>
        /// </summary>
        public void ForgetPlot(int areaId, PlotCoord plot)
        {
            if (_byArea.TryGetValue(areaId, out var plots))
                plots.Remove(plot);

            if (_columnsByArea.TryGetValue(areaId, out var columns))
                foreach (var key in columns.Keys.Where(k => InPlot(k.X, k.Z, plot)).ToList())
                    columns.Remove(key);

            if (_sampledBlocks.TryGetValue(areaId, out var blocks))
                blocks.RemoveWhere(b => InPlot(b.X, b.Z, plot));
        }

        private bool InPlot(int x, int z, PlotCoord plot) =>
            PlotCoord.FromWorldColumn(x, z, _plotSize).Equals(plot);

        // --- The pass projection: what the Eco side persists on the area so a stopped pass
        //     resumes instead of restarting (U7, R25). ---

        /// <summary>
        /// One row per column this pass has touched in <paramref name="areaId"/> — the shape the
        /// area persists (<c>SurveyAreaEntry.SweepColumns</c>) and hands back to
        /// <see cref="RestorePassColumn"/> after a restart.
        ///
        /// Per COLUMN rather than per block, because the sweep is column-atomic: the sensor scans
        /// a column top-down in one call, so "how many of its blocks were sampled" plus its
        /// surface height names the same block set that listing every block would, at a
        /// fifteenth of the size. This is the largest structure the mod persists and the shape is
        /// the reason it stays affordable.
        /// </summary>
        public IEnumerable<SurveyColumnState> PassColumns(int areaId)
        {
            if (!_columnsByArea.TryGetValue(areaId, out var columns))
                yield break;

            foreach (var entry in columns)
                yield return new SurveyColumnState(
                    entry.Key.X, entry.Key.Z, entry.Value.SurfaceY, entry.Value.SampledBlocks, entry.Value.RestsOnBedrock);
        }

        /// <summary>
        /// Rebuilds one column of <paramref name="areaId"/> from its persisted row, replaying the
        /// blocks the pass sampled there as "sampled, no ore" — the ore attribution comes back
        /// separately from the area's persisted findings rows (<see cref="RestoreFinding"/>),
        /// which are the authoritative record of what was found.
        ///
        /// The blocks are replayed downward from the recorded surface exactly as the sensor
        /// walked them, so the rebuilt sampled-block set, the per-plot sampled counts, and
        /// therefore <see cref="Coverage"/> match what the stopped pass had reached.
        /// </summary>
        public void RestorePassColumn(int areaId, SurveyColumnState column)
        {
            if (column.SurfaceY.HasValue)
            {
                RecordSurface(areaId, column.X, column.Z, column.SurfaceY.Value);
                for (var depth = 0; depth < column.SampledBlocks; depth++)
                {
                    var y = column.SurfaceY.Value - depth;
                    if (y < 0)
                        break;
                    RecordSample(column.X, y, column.Z, null, depth, areaId);
                }
            }

            if (column.RestsOnBedrock.HasValue)
                RecordColumnBedrock(areaId, column.X, column.Z, column.RestsOnBedrock.Value);
        }

        /// <summary>
        /// Restores one persisted per-plot findings row into the live record, so a resumed pass
        /// keeps what the stopped one found rather than projecting an emptied record back over
        /// the area's snapshot. Ignores a row that is not found or carries no plot.
        /// </summary>
        public void RestoreFinding(int areaId, SurveyFinding row)
        {
            if (!row.Found || !row.HasPlot || string.IsNullOrEmpty(row.OreType) || row.Count <= 0)
                return;

            if (!_byArea.TryGetValue(areaId, out var plots))
            {
                plots = new Dictionary<PlotCoord, PlotData>();
                _byArea[areaId] = plots;
            }
            if (!plots.TryGetValue(row.Plot, out var data))
            {
                data = new PlotData();
                plots[row.Plot] = data;
            }

            data.RestoreOre(row.OreType, row.Count, row.Position, row.DepthBelowSurface, row.DepthMax);
        }

        /// <summary>
        /// Records how far the sweep of <paramref name="areaId"/> has got, so a pass that stops
        /// resumes from here (R25) instead of re-flying ground the sampled set already treats as
        /// done. Held beside the samples on purpose — they are one fact about the pass.
        /// </summary>
        public void SetSweepCursor(int areaId, int plotIndex, int columnCursor)
            => _cursorByArea[areaId] = new SweepCursor(plotIndex, columnCursor);

        /// <summary>The sweep cursor for <paramref name="areaId"/>, or the origin when no pass has recorded one.</summary>
        public SweepCursor SweepCursorFor(int areaId)
            => _cursorByArea.TryGetValue(areaId, out var cursor) ? cursor : default;

        /// <summary>
        /// What the pass learned about one world column: its surface height, how many of its
        /// blocks were sampled, and whether it rests on the world floor. Surface and bedrock are
        /// nullable because either can be recorded without the other (the sensor records both,
        /// but the record's API lets each be written alone), and "not observed" is a different
        /// answer from "observed false" for the at-bedrock fold.
        /// </summary>
        private sealed class ColumnState
        {
            public int? SurfaceY;
            public int SampledBlocks;
            public bool? RestsOnBedrock;
        }

        /// <summary>Per-plot accumulation: total sampled blocks plus, per ore, count and shallowest sighting.</summary>
        private sealed class PlotData
        {
            private readonly Dictionary<string, OreData> _ores = new Dictionary<string, OreData>();

            public int SampledCount;

            public IEnumerable<string> OreTypes => _ores.Keys;

            public void RecordOre(string oreType, BlockPos position, int depthBelowSurface)
            {
                if (!_ores.TryGetValue(oreType, out var ore))
                {
                    ore = new OreData { Count = 0, ShallowestDepth = depthBelowSurface, ShallowestPos = position, DeepestDepth = depthBelowSurface };
                    _ores[oreType] = ore;
                }

                ore.Count++;
                if (depthBelowSurface < ore.ShallowestDepth)
                {
                    ore.ShallowestDepth = depthBelowSurface;
                    ore.ShallowestPos = position;
                }
                if (depthBelowSurface > ore.DeepestDepth)
                    ore.DeepestDepth = depthBelowSurface;
            }

            /// <summary>
            /// Reinstates one ore's already-aggregated totals from a persisted row, rather than
            /// replaying the blocks that produced them (the persisted record keeps counts, not
            /// individual ore sightings). Used only by <see cref="RestoreFinding"/>.
            /// </summary>
            public void RestoreOre(string oreType, int count, BlockPos shallowestPos, int shallowestDepth, int deepestDepth)
                => _ores[oreType] = new OreData
                {
                    Count = count,
                    ShallowestDepth = shallowestDepth,
                    ShallowestPos = shallowestPos,
                    DeepestDepth = deepestDepth,
                };

            public bool TryGetOre(string oreType, out int count, out int shallowestDepth, out BlockPos shallowestPos, out int deepestDepth)
            {
                if (_ores.TryGetValue(oreType, out var ore))
                {
                    count = ore.Count;
                    shallowestDepth = ore.ShallowestDepth;
                    shallowestPos = ore.ShallowestPos;
                    deepestDepth = ore.DeepestDepth;
                    return true;
                }

                count = 0;
                shallowestDepth = 0;
                shallowestPos = default;
                deepestDepth = 0;
                return false;
            }

            private sealed class OreData
            {
                public int Count;
                public int ShallowestDepth;
                public BlockPos ShallowestPos;
                public int DeepestDepth;
            }
        }
    }

    /// <summary>
    /// One column of a survey pass as it crosses the persistence boundary (U7, R25): where the
    /// column is, the surface the sensor found there, how many of its blocks the pass sampled,
    /// and whether it rests on the world floor.
    ///
    /// The unit is a column rather than a block because the sensor scans a column top-down in
    /// one call, so a count plus a surface names the same block set that listing every block
    /// would — the difference between roughly 5 ints and roughly 75 per column, over every
    /// column of every area a dock owns.
    /// </summary>
    public readonly struct SurveyColumnState
    {
        public SurveyColumnState(int x, int z, int? surfaceY, int sampledBlocks, bool? restsOnBedrock)
        {
            this.X = x;
            this.Z = z;
            this.SurfaceY = surfaceY;
            this.SampledBlocks = sampledBlocks;
            this.RestsOnBedrock = restsOnBedrock;
        }

        public int X { get; }
        public int Z { get; }

        /// <summary>The surface height recorded for this column, or null when none was.</summary>
        public int? SurfaceY { get; }

        /// <summary>How many blocks of this column the pass sampled, counting downward from <see cref="SurfaceY"/>.</summary>
        public int SampledBlocks { get; }

        /// <summary>Whether this column was observed to rest on the world floor, or null when it was not observed at all (U4).</summary>
        public bool? RestsOnBedrock { get; }
    }

    /// <summary>
    /// How far a survey sweep has got: which plot of the pass's raster order it is on, and which
    /// column of that plot. Travels with the sampled-block set (U7) — a cursor without the
    /// samples reports coverage the pass never earned, and samples without the cursor re-fly
    /// ground the record already treats as done.
    /// </summary>
    public readonly struct SweepCursor
    {
        public SweepCursor(int plotIndex, int columnCursor)
        {
            this.PlotIndex = plotIndex;
            this.ColumnCursor = columnCursor;
        }

        public int PlotIndex { get; }
        public int ColumnCursor { get; }
    }
}
