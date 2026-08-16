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
    }
}
