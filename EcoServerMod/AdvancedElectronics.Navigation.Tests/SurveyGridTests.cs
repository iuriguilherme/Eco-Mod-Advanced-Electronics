using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class SurveyGridTests
    {
        private const string Iron = "IronOre";
        private const string Gold = "GoldOre";

        // --- R8: single-cell ore makes that cell densest ---

        [Fact]
        public void OreSampledInOnlyOneCell_ThatCellIsDensestForThatOre()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            // All samples land in the same cell (cellSize=10 -> cell (0,0)).
            grid.RecordSample(1, 64, 1, Iron);
            grid.RecordSample(2, 64, 1, null);
            grid.RecordSample(3, 64, 1, null);

            var result = grid.DensestCell(Iron);

            Assert.True(result.Found);
            Assert.Equal(new SurveyCell(0, 0), result.Cell);
            Assert.Equal(1, result.OreCount);
            Assert.Equal(3, result.SampledCount);
        }

        // --- R8: ratio wins over raw count (the bug-catching scenario) ---

        [Fact]
        public void DensestCell_PicksHigherRatio_NotHigherRawCount()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            // Cell A (low ratio, high raw count): 10 ore blocks out of 100 sampled -> ratio 0.10.
            // Cell (0,0) covers x in [0,9] and z in [0,9] with cellSize=10 - every
            // position below stays inside that single cell, varying x/z (not y) to
            // get 100 distinct block positions.
            for (int i = 0; i < 10; i++)
                grid.RecordSample(i, 64, 0, Iron);
            for (int i = 0; i < 90; i++)
                grid.RecordSample(i % 10, 64, 1 + i / 10, null);

            // Cell B (high ratio, low raw count): 3 ore blocks out of 5 sampled -> ratio 0.60.
            // x=21..25 -> cell (2, *) with cellSize=10, distinct from cell A's (0, *).
            grid.RecordSample(21, 64, 1, Iron);
            grid.RecordSample(22, 64, 1, Iron);
            grid.RecordSample(23, 64, 1, Iron);
            grid.RecordSample(24, 64, 1, null);
            grid.RecordSample(25, 64, 1, null);

            var result = grid.DensestCell(Iron);

            Assert.True(result.Found);
            Assert.Equal(new SurveyCell(2, 0), result.Cell);
            Assert.Equal(3, result.OreCount);
            Assert.Equal(5, result.SampledCount);
            Assert.True(result.Ratio > 0.5f);
        }

        // --- R7: multiple ore types accumulate independently ---

        [Fact]
        public void MultipleOreTypes_AccumulateIndependently_EachHasOwnDensestCell()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            // Cell (0,0): mostly iron.
            grid.RecordSample(1, 64, 1, Iron);
            grid.RecordSample(2, 64, 1, Iron);
            grid.RecordSample(3, 64, 1, null);

            // Cell (5,0) (x=50..59): mostly gold.
            grid.RecordSample(50, 64, 1, Gold);
            grid.RecordSample(51, 64, 1, Gold);
            grid.RecordSample(52, 64, 1, Gold);
            grid.RecordSample(53, 64, 1, null);

            var ironResult = grid.DensestCell(Iron);
            var goldResult = grid.DensestCell(Gold);

            Assert.True(ironResult.Found);
            Assert.Equal(new SurveyCell(0, 0), ironResult.Cell);

            Assert.True(goldResult.Found);
            Assert.Equal(new SurveyCell(5, 0), goldResult.Cell);

            // Gold's samples must not have polluted iron's counts, and vice versa.
            Assert.Equal(2, ironResult.OreCount);
            Assert.Equal(3, goldResult.OreCount);
        }

        // --- Sampling idempotency: same block sampled twice counts once ---

        [Fact]
        public void SameBlockSampledTwice_DoesNotDoubleCount()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            grid.RecordSample(4, 64, 4, Iron);
            grid.RecordSample(4, 64, 4, Iron); // same exact block, reported again
            grid.RecordSample(4, 64, 4, Iron); // and again

            var result = grid.DensestCell(Iron);

            Assert.True(result.Found);
            Assert.Equal(1, result.OreCount);
            Assert.Equal(1, result.SampledCount);
        }

        [Fact]
        public void SameBlockResampledWithDifferentOreType_KeepsFirstRecordedResult()
        {
            // Documents the idempotency rule's edge case: once a block position has
            // been recorded, later calls are a no-op regardless of what oreType they
            // carry - the position, not the ore type, is the dedupe key.
            var grid = new SurveyGrid(cellSize: 10f);

            grid.RecordSample(7, 64, 7, Iron);
            grid.RecordSample(7, 64, 7, Gold); // ignored: (7, 64, 7) was already recorded

            var ironResult = grid.DensestCell(Iron);
            var goldResult = grid.DensestCell(Gold);

            Assert.True(ironResult.Found);
            Assert.Equal(1, ironResult.OreCount);
            Assert.False(goldResult.Found);
        }

        // --- Empty survey: no-data result, not a false default cell ---

        [Fact]
        public void EmptySurvey_DensestCellQuery_ReturnsNotFound()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            var result = grid.DensestCell(Iron);

            Assert.False(result.Found);
        }

        [Fact]
        public void SurveyWithSamples_ButNoneOfQueriedOreType_ReturnsNotFound()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            grid.RecordSample(1, 64, 1, null);
            grid.RecordSample(2, 64, 1, Iron);

            var result = grid.DensestCell(Gold);

            Assert.False(result.Found);
        }

        // --- World-position-to-cell mapping: deterministic, gapless boundaries ---

        [Fact]
        public void PositionExactlyOnCellBoundary_MapsToTheCellThatStartsThere()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            // x=20 is the boundary between cell 1 ([10,20)) and cell 2 ([20,30)).
            // Floor-division convention: it belongs to cell 2.
            Assert.Equal(new SurveyCell(2, 0), grid.CellAt(20, 0));
            Assert.Equal(new SurveyCell(1, 0), grid.CellAt(19, 0));
            Assert.Equal(new SurveyCell(1, 0), grid.CellAt(10, 0));
        }

        [Fact]
        public void CellMapping_IsDeterministic_SameInputAlwaysMapsToSameCell()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            var first = grid.CellAt(20, 20);
            var second = grid.CellAt(20, 20);

            Assert.Equal(first, second);
        }

        [Fact]
        public void CellMapping_NegativeCoordinates_FloorsTowardNegativeInfinity_NoGapAtOrigin()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            // Naive truncating division would map both -1 and 1 to cell 0, leaving a
            // gap at the origin. Floor division must not do that.
            Assert.Equal(new SurveyCell(-1, 0), grid.CellAt(-1, 0));
            Assert.Equal(new SurveyCell(-1, 0), grid.CellAt(-10, 0));
            Assert.Equal(new SurveyCell(-2, 0), grid.CellAt(-11, 0));
            Assert.Equal(new SurveyCell(0, 0), grid.CellAt(0, 0));
            Assert.Equal(new SurveyCell(0, 0), grid.CellAt(9, 0));
        }

        [Fact]
        public void CellMapping_AdjacentCells_ShareNoOverlap()
        {
            var grid = new SurveyGrid(cellSize: 10f);

            // Every integer column from -20..19 must map to exactly one of cells
            // -2, -1, 0, 1 with no column left unmapped (no gaps) and no column
            // mapping to two cells (trivially true for a function, but this also
            // pins down the expected cell boundaries all at once).
            Assert.Equal(new SurveyCell(-2, 0), grid.CellAt(-20, 0));
            Assert.Equal(new SurveyCell(-2, 0), grid.CellAt(-19, 0));
            Assert.Equal(new SurveyCell(-1, 0), grid.CellAt(-10, 0));
            Assert.Equal(new SurveyCell(-1, 0), grid.CellAt(-1, 0));
            Assert.Equal(new SurveyCell(0, 0), grid.CellAt(0, 0));
            Assert.Equal(new SurveyCell(0, 0), grid.CellAt(9, 0));
            Assert.Equal(new SurveyCell(1, 0), grid.CellAt(10, 0));
            Assert.Equal(new SurveyCell(1, 0), grid.CellAt(19, 0));
        }

        [Fact]
        public void Constructor_RejectsNonPositiveCellSize()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SurveyGrid(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SurveyGrid(-5f));
        }
    }
}
