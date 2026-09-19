using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// The 0.3.0 -> 0.4.0 save fold for mined-plot stamps.
    ///
    /// <para>
    /// 0.3.0 kept the stamps on the mining dock; 0.4.0 keeps them on the area (U2). The fold
    /// is what stops a dock carried across the update from re-digging every shaft it had
    /// already finished, and the properties pinned here are the ones that make it safe to run
    /// more than once: it never lowers a plot's stamp, it never drops what the area already
    /// holds, and a half-written triple at the end of the legacy list is not a record.
    /// </para>
    /// </summary>
    public class LegacyMinedStampsTests
    {
        private static Dictionary<PlotCoord, long> Snapshot(PlotStampAccumulator accumulator) =>
            accumulator.Snapshot().ToDictionary(e => e.Key, e => e.Value);

        [Fact]
        public void Legacy_stamps_reach_an_empty_area_record()
        {
            var legacy = new long[] { 3, 4, 100, -1, 7, 250 };

            var merged = Snapshot(LegacyMinedStamps.MergeInto(legacy, new PlotStampAccumulator()));

            Assert.Equal(2, merged.Count);
            Assert.Equal(100, merged[new PlotCoord(3, 4)]);
            Assert.Equal(250, merged[new PlotCoord(-1, 7)]);
        }

        [Fact]
        public void What_the_area_already_holds_survives_the_fold()
        {
            var existing = new PlotStampAccumulator();
            existing.Record(new PlotCoord(9, 9), 500);

            var merged = Snapshot(LegacyMinedStamps.MergeInto(new long[] { 3, 4, 100 }, existing));

            Assert.Equal(500, merged[new PlotCoord(9, 9)]);
            Assert.Equal(100, merged[new PlotCoord(3, 4)]);
        }

        [Fact]
        public void A_plot_never_moves_backwards_in_time()
        {
            var existing = new PlotStampAccumulator();
            existing.Record(new PlotCoord(3, 4), 900);

            var merged = Snapshot(LegacyMinedStamps.MergeInto(new long[] { 3, 4, 100 }, existing));

            Assert.Equal(900, merged[new PlotCoord(3, 4)]);
        }

        [Fact]
        public void A_later_legacy_stamp_wins_over_an_older_recorded_one()
        {
            var existing = new PlotStampAccumulator();
            existing.Record(new PlotCoord(3, 4), 100);

            var merged = Snapshot(LegacyMinedStamps.MergeInto(new long[] { 3, 4, 900 }, existing));

            Assert.Equal(900, merged[new PlotCoord(3, 4)]);
        }

        [Fact]
        public void Running_the_fold_twice_changes_nothing()
        {
            var legacy = new long[] { 3, 4, 100, 5, 6, 200 };

            var once = LegacyMinedStamps.MergeInto(legacy, new PlotStampAccumulator());
            var twice = LegacyMinedStamps.MergeInto(legacy, once);

            Assert.Equal(Snapshot(once), Snapshot(twice));
        }

        [Fact]
        public void A_trailing_partial_triple_is_not_a_record()
        {
            var merged = Snapshot(LegacyMinedStamps.MergeInto(new long[] { 3, 4, 100, 5, 6 }, new PlotStampAccumulator()));

            Assert.Single(merged);
            Assert.Equal(100, merged[new PlotCoord(3, 4)]);
        }

        [Fact]
        public void Nothing_to_fold_leaves_the_record_as_it_was()
        {
            var existing = new PlotStampAccumulator();
            existing.Record(new PlotCoord(1, 1), 42);

            var fromEmpty = Snapshot(LegacyMinedStamps.MergeInto(new long[0], existing));
            var fromNull = Snapshot(LegacyMinedStamps.MergeInto(null, existing));

            Assert.Equal(42, fromEmpty[new PlotCoord(1, 1)]);
            Assert.Equal(42, fromNull[new PlotCoord(1, 1)]);
            Assert.Single(fromEmpty);
            Assert.Single(fromNull);
        }
    }
}
