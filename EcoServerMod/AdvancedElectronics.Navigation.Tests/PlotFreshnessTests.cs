using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class PlotFreshnessTests
    {
        private static readonly PlotCoord P = new PlotCoord(1, 1);

        [Fact]
        public void SurveyedNeverMined_IsMineable()
        {
            Assert.True(PlotFreshness.IsMineable(surveyedStamp: 5, minedStamp: 0));
        }

        [Fact]
        public void MinedStampNewerThanSurveyed_IsNotMineable()
        {
            Assert.False(PlotFreshness.IsMineable(surveyedStamp: 5, minedStamp: 10));
        }

        [Fact]
        public void NewSweepOfMinedPlot_WritesNewerSurveyedStamp_RestoresMineability()
        {
            var surveyed = new PlotStampAccumulator();
            var mined = new PlotStampAccumulator();

            surveyed.Record(P, 5);
            mined.Record(P, 10);
            Assert.False(PlotFreshness.IsMineable(surveyed.StampFor(P), mined.StampFor(P)));

            surveyed.Record(P, 15); // a fresh sweep after the mining
            Assert.True(PlotFreshness.IsMineable(surveyed.StampFor(P), mined.StampFor(P)));

            // A sweep of a DIFFERENT plot does not affect this one.
            var other = new PlotCoord(2, 2);
            surveyed.Record(other, 99);
            Assert.Equal(15, surveyed.StampFor(P));
        }

        [Fact]
        public void UnsurveyedPlot_NeitherStampSet_IsNeverMineable()
        {
            Assert.False(PlotFreshness.IsMineable(surveyedStamp: 0, minedStamp: 0));
        }

        [Fact]
        public void WritingMinedStampTwiceAtSameValue_IsIdempotent()
        {
            var accumulator = new PlotStampAccumulator();
            accumulator.Record(P, 7);
            accumulator.Record(P, 7);

            Assert.Equal(7, accumulator.StampFor(P));
        }

        [Fact]
        public void OlderStamp_DoesNotOverwriteNewer()
        {
            var accumulator = new PlotStampAccumulator();
            accumulator.Record(P, 10);
            accumulator.Record(P, 3);

            Assert.Equal(10, accumulator.StampFor(P));
        }

        [Fact]
        public void PersistedSnapshot_RoundTrips_ProjectClearRehydrate_SamePlotsSameStamps()
        {
            var accumulator = new PlotStampAccumulator();
            accumulator.Record(P, 5);
            accumulator.Record(new PlotCoord(3, 3), 8);

            var snapshot = accumulator.Snapshot().ToList();
            var rehydrated = PlotStampAccumulator.FromSnapshot(snapshot);

            Assert.Equal(5, rehydrated.StampFor(P));
            Assert.Equal(8, rehydrated.StampFor(new PlotCoord(3, 3)));
        }

        [Fact]
        public void EmptyAccumulator_ProjectionGuard_ReportsEmpty()
        {
            var accumulator = new PlotStampAccumulator();
            Assert.True(accumulator.IsEmpty);

            accumulator.Record(P, 1);
            Assert.False(accumulator.IsEmpty);
        }

        // ---------------------------------------------------------------
        // U2 (R1, R2, R6, R13): the mined record moved off the mining dock and onto the
        // survey area, so BOTH stamps IsMineable compares now come from one object every
        // dock reads. These tests hold the topology fixed -- one surveyed accumulator and
        // one mined accumulator per AREA, shared by every reader -- which is what the Eco
        // side's SurveyAreaEntry.ReadSurveyedStamps/ReadMinedStamps pair hands out.
        // IsMineable itself is unchanged and still takes two plain longs (KTD2).
        // ---------------------------------------------------------------

        /// <summary>The two per-area stamp records a dock reads, standing in for one SurveyAreaEntry.</summary>
        private sealed class Area
        {
            public PlotStampAccumulator Surveyed = new PlotStampAccumulator();
            public PlotStampAccumulator Mined = new PlotStampAccumulator();

            public bool IsMineable(PlotCoord plot) =>
                PlotFreshness.IsMineable(this.Surveyed.StampFor(plot), this.Mined.StampFor(plot));
        }

        [Fact]
        public void Covers_AE1_SurveyedAt100AndMinedAt200_ReadsMined_FromADockThatNeverWorkedIt()
        {
            var area = new Area();
            area.Surveyed.Record(P, 100);
            area.Mined.Record(P, 200); // written by the dock that dug it

            // A second dock brings no mined record of its own to the question -- there is nowhere
            // for one to live any more. It reads the area and gets the same 200.
            Assert.False(area.IsMineable(P));
            Assert.True(PlotFreshness.IsMinedOut(new[] { P }, area.Surveyed.StampFor, area.Mined.StampFor));
        }

        [Fact]
        public void Covers_R1_PlotMinedByOneDock_IsNotMineableToAnother()
        {
            var area = new Area();
            area.Surveyed.Record(P, 100);

            Assert.True(area.IsMineable(P)); // before anyone digs, every dock is offered it

            area.Mined.Record(P, 200); // dock A digs it

            // Dock B asks the same area and is refused. Before U2 dock B held its own empty
            // mined list, so it read the plot as mineable and dug ground already taken.
            Assert.False(area.IsMineable(P));
        }

        [Fact]
        public void Covers_R2_TwoDocksOnOneArea_KeepSeparateJobLedgers_ButAgreeOnWhatIsMined()
        {
            var plots = new[] { new PlotCoord(0, 0), new PlotCoord(0, 1) };
            var area = new Area();
            foreach (var plot in plots)
                area.Surveyed.Record(plot, 100);

            var jobA = new MiningJob(plots);
            var jobB = new MiningJob(plots);
            jobA.Dispatch();
            jobB.Dispatch();

            var takenByA = jobA.NextPlot(area.IsMineable);
            Assert.NotNull(takenByA);
            jobA.MarkWorked(takenByA.Value);
            area.Mined.Record(takenByA.Value, 200); // R1: recorded on the area, not on dock A

            // R2: the ledgers are per dock and do not leak into one another.
            Assert.Equal(1, jobA.WorkedCount);
            Assert.Equal(0, jobB.WorkedCount);

            // R1: but the ground record is shared, so dock B is handed the other plot.
            var offeredToB = jobB.NextPlot(area.IsMineable);
            Assert.NotNull(offeredToB);
            Assert.NotEqual(takenByA.Value, offeredToB.Value);

            // And once both plots are taken, neither dock is offered anything, whichever dug them.
            jobB.MarkWorked(offeredToB.Value);
            area.Mined.Record(offeredToB.Value, 201);
            Assert.Null(jobA.NextPlot(area.IsMineable));
            Assert.Null(jobB.NextPlot(area.IsMineable));
        }

        [Fact]
        public void Covers_R13_ClearingTheSurveyRecordForAResurvey_LeavesTheMinedRecordStanding()
        {
            var area = new Area();
            area.Surveyed.Record(P, 100);
            area.Mined.Record(P, 200);

            // A resurvey clears findings and surveyed stamps. The mined stamps are a separate
            // member and are deliberately not part of that clear.
            area.Surveyed = new PlotStampAccumulator();

            Assert.Equal(200, area.Mined.StampFor(P));
            Assert.False(area.IsMineable(P)); // 0 surveyed vs 200 mined: nothing to offer yet
        }

        [Fact]
        public void Covers_AE3_MinedAt200_ResurveyedAt300_IsMineableAgain_AndThe200StampSurvives()
        {
            var area = new Area();
            area.Surveyed.Record(P, 100);
            area.Mined.Record(P, 200);
            Assert.False(area.IsMineable(P));

            area.Surveyed = new PlotStampAccumulator(); // R10's clear
            area.Surveyed.Record(P, 300);               // the pass completes

            Assert.True(area.IsMineable(P));
            Assert.Equal(200, area.Mined.StampFor(P)); // R13: still recorded
        }

        [Fact]
        public void MinedStampsRoundTripThroughTheFlattenedTripleList_WithoutLosingAPlot()
        {
            // The persisted shape both stamp kinds use: consecutive (x, z, stamp) longs, read
            // back with the loop bound `i + 2 < Count`. Negative plot coordinates and a plot at
            // the origin are the two cases a cast or a bound gets wrong, and (0, 0) is a real
            // plot near the world origin rather than an absent one.
            var plots = new[] { new PlotCoord(0, 0), new PlotCoord(-4, 7), new PlotCoord(12, -9) };
            var accumulator = new PlotStampAccumulator();
            for (var i = 0; i < plots.Length; i++)
                accumulator.Record(plots[i], 100 + i);

            var flat = new List<long>();
            foreach (var entry in accumulator.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            Assert.Equal(plots.Length * 3, flat.Count);

            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < flat.Count; i += 3)
                entries[new PlotCoord((int)flat[i], (int)flat[i + 1])] = flat[i + 2];
            var rehydrated = PlotStampAccumulator.FromSnapshot(entries);

            for (var i = 0; i < plots.Length; i++)
                Assert.Equal(100 + i, rehydrated.StampFor(plots[i]));
        }

        [Fact]
        public void PlotNeverMined_ReadsAsNeverAndStaysMineable_WhileAnEmptyRecordOffersEverything()
        {
            var area = new Area();
            var untouched = new PlotCoord(8, 8);
            area.Surveyed.Record(untouched, 50);

            Assert.True(area.Mined.IsEmpty);
            Assert.Equal(0, area.Mined.StampFor(untouched)); // "never", not "unknown"
            Assert.True(area.IsMineable(untouched));
            Assert.False(PlotFreshness.IsMinedOut(new[] { untouched }, area.Surveyed.StampFor, area.Mined.StampFor));
        }
    }
}
