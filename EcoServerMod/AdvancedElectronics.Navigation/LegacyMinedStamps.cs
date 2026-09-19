using System.Collections.Generic;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Folds a 0.3.0 save's dock-level mined stamps into the area-level record 0.4.0 keeps.
    ///
    /// <para>
    /// Until 0.4.0 the mined-plot stamps lived on the MINING dock as one flat
    /// <c>(x, z, stamp)</c> triple list covering whatever area that dock was working. U2 moved
    /// the record onto the area, because a stamp is a fact about the ground rather than about
    /// the dock that dug it. A save written by 0.3.0 therefore carries stamps in a place 0.4.0
    /// no longer reads, and without this fold every plot a dock had already mined reads as
    /// never mined: <see cref="PlotFreshness.IsMineable"/> compares the area's surveyed stamp
    /// against the area's mined stamp, so an empty mined record makes a finished shaft look
    /// like fresh ground and the drone digs it again.
    /// </para>
    ///
    /// <para>
    /// The merge keeps the HIGHER stamp per plot rather than letting either side win by
    /// position. A stamp is a monotonic marker of when the plot was last dug, so the higher one
    /// is the later one, and taking it means the migration can run twice, or run after the area
    /// has already recorded newer digging, without moving any plot backwards in time.
    /// </para>
    /// </summary>
    public static class LegacyMinedStamps
    {
        /// <summary>
        /// Reads <paramref name="flatTriples"/> as consecutive <c>(x, z, stamp)</c> values and
        /// merges them into <paramref name="existing"/>, keeping the higher stamp per plot.
        ///
        /// A trailing partial triple is ignored, matching how every other reader of this shape
        /// walks it: the list is written three values at a time, so anything shorter than a
        /// whole triple is not a record.
        ///
        /// Returns a new accumulator; neither argument is modified.
        /// </summary>
        public static PlotStampAccumulator MergeInto(IEnumerable<long> flatTriples, PlotStampAccumulator existing)
        {
            var merged = new Dictionary<PlotCoord, long>();

            if (existing != null)
            {
                foreach (var entry in existing.Snapshot())
                    merged[entry.Key] = entry.Value;
            }

            if (flatTriples == null)
                return PlotStampAccumulator.FromSnapshot(merged);

            var triple = new long[3];
            var filled = 0;

            foreach (var value in flatTriples)
            {
                triple[filled++] = value;
                if (filled < 3) continue;

                filled = 0;
                var plot = new PlotCoord((int)triple[0], (int)triple[1]);
                var stamp = triple[2];

                if (!merged.TryGetValue(plot, out var current) || stamp > current)
                    merged[plot] = stamp;
            }

            return PlotStampAccumulator.FromSnapshot(merged);
        }
    }
}
