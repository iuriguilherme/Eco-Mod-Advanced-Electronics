using System;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Which column of a plot the drone flies to when it is sent there.
    /// </summary>
    /// <remarks>
    /// The pathfinder refuses a goal column that is solid overhead or occupied, and the
    /// drone used to aim at the plot's centre column and nothing else. One tree standing
    /// at the centre therefore made a whole plot unreachable while every other column of
    /// it was open, and a farm area next to trees reported "unreachable" with ground the
    /// drone could have worked. Any open column of the plot is as good a place to stand:
    /// the work itself walks every column regardless of where the drone hovers.
    /// </remarks>
    public static class PlotApproach
    {
        /// <summary>
        /// The open column of <paramref name="plot"/> nearest its centre, or null when every
        /// column is blocked. Ties break by x then z, so the choice is the same every time.
        /// </summary>
        /// <param name="isBlocked">True for a column the pathfinder would refuse as a goal.</param>
        public static (int X, int Z)? FirstOpenColumn(PlotCoord plot, int plotSide, Func<int, int, bool> isBlocked)
        {
            if (plotSide <= 0) throw new ArgumentOutOfRangeException(nameof(plotSide));
            if (isBlocked == null) throw new ArgumentNullException(nameof(isBlocked));

            var baseX = plot.X * plotSide;
            var baseZ = plot.Z * plotSide;
            var centreX = baseX + plotSide / 2;
            var centreZ = baseZ + plotSide / 2;

            var ordered =
                from dx in Enumerable.Range(0, plotSide)
                from dz in Enumerable.Range(0, plotSide)
                let x = baseX + dx
                let z = baseZ + dz
                orderby Math.Abs(x - centreX) + Math.Abs(z - centreZ), x, z
                select (X: x, Z: z);

            foreach (var column in ordered)
                if (!isBlocked(column.X, column.Z))
                    return column;

            return null;
        }
    }
}
