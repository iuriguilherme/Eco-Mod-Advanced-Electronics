using System.Collections.Generic;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// A live, per-plot monotonic-counter stamp accumulator (KTD12) -- the same shape
    /// serves both stamp kinds (surveyed, on the area entry; mined, on the mining dock),
    /// each drawing its values from one shared monotonic counter so the two are
    /// comparable. Mirrors the live-accumulator-plus-flat-snapshot pattern
    /// (docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md):
    /// this is the live half; the Eco side projects <see cref="Snapshot"/> onto a
    /// persisted flattened list and rehydrates it with <see cref="FromSnapshot"/>.
    /// </summary>
    public sealed class PlotStampAccumulator
    {
        private readonly Dictionary<PlotCoord, long> _stamps = new Dictionary<PlotCoord, long>();

        /// <summary>
        /// Records <paramref name="stampValue"/> for <paramref name="plot"/>. Idempotent:
        /// writing the same or an older value than what is already recorded is a no-op,
        /// since the counter only moves forward and a stamp answers "how recently", not
        /// "how many times".
        /// </summary>
        public void Record(PlotCoord plot, long stampValue)
        {
            if (!_stamps.TryGetValue(plot, out var existing) || stampValue > existing)
                _stamps[plot] = stampValue;
        }

        /// <summary>The stamp for <paramref name="plot"/>, or 0 ("never") if none has been recorded.</summary>
        public long StampFor(PlotCoord plot) => _stamps.TryGetValue(plot, out var value) ? value : 0;

        /// <summary>True when nothing has been recorded yet -- the projection guard so an empty post-restart accumulator never overwrites a populated persisted snapshot.</summary>
        public bool IsEmpty => _stamps.Count == 0;

        /// <summary>Every recorded (plot, stamp) pair, for the Eco side to persist as a flattened snapshot.</summary>
        public IEnumerable<KeyValuePair<PlotCoord, long>> Snapshot() => _stamps;

        /// <summary>Rebuilds an accumulator from a persisted snapshot (e.g. on load).</summary>
        public static PlotStampAccumulator FromSnapshot(IEnumerable<KeyValuePair<PlotCoord, long>> entries)
        {
            var accumulator = new PlotStampAccumulator();
            if (entries != null)
                foreach (var entry in entries)
                    accumulator.Record(entry.Key, entry.Value);
            return accumulator;
        }
    }

    /// <summary>
    /// Whether a plot is mineable (R8, R9, R41, KD16): its surveyed stamp is newer than
    /// its mined stamp. An absent stamp reads as 0 ("never"), so an unsurveyed plot
    /// (both stamps 0) is never mineable, and a plot mined after its last survey (mined
    /// stamp newer) stays not-mineable until a fresh sweep writes a newer surveyed stamp.
    /// </summary>
    public static class PlotFreshness
    {
        public static bool IsMineable(long surveyedStamp, long minedStamp) => surveyedStamp > minedStamp;
    }
}
