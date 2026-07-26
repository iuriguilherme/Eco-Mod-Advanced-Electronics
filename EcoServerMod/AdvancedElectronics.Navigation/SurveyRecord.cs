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
    /// <b>Not persisted.</b> This record is session-scoped by design (R8/KTD3) —
    /// the world already stores block data. The Eco side holds one of these on
    /// the dock and never serializes it.
    /// </para>
    /// </remarks>
    public sealed class SurveyRecord
    {
        private readonly int _plotSize;
        private readonly HashSet<BlockPos> _sampledBlocks = new HashSet<BlockPos>();

        // areaId -> plot -> aggregated counts for that plot.
        private readonly Dictionary<int, Dictionary<PlotCoord, PlotData>> _byArea =
            new Dictionary<int, Dictionary<PlotCoord, PlotData>>();

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
            if (!_sampledBlocks.Add(new BlockPos(x, y, z)))
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
            if (!string.IsNullOrEmpty(oreType))
                data.RecordOre(oreType, new BlockPos(x, y, z), depthBelowSurface);
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
        /// The area-total finding per material type in <paramref name="areaId"/> — what the readout
        /// renders (R2). Only this area's own findings appear; another area's are never included (R3a).
        /// </summary>
        public IEnumerable<SurveyFinding> Findings(int areaId) =>
            SampledOreTypes(areaId).Select(ore => MaterialFinding(areaId, ore)).Where(f => f.Found);

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

        /// <summary>Discards every finding for <paramref name="areaId"/>. Used when an area is deleted or reassigned away.</summary>
        public void ClearArea(int areaId)
        {
            if (_byArea.Remove(areaId))
            {
                // Drop this area's sampled blocks so re-surveying it later records
                // fresh rather than being silently deduped against stale positions.
                _sampledBlocks.RemoveWhere(b =>
                    !_byArea.Values.Any(plots => plots.ContainsKey(PlotCoord.FromWorldColumn(b.X, b.Z, _plotSize))));
            }
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
}
