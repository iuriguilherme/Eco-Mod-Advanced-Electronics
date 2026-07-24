using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// A property-plot coordinate: the index of a plot on the world plot grid,
    /// not a world block position. A plot spans <c>plotSize</c> world blocks on
    /// each axis, so world column (x, z) maps to plot
    /// <c>(floor(x / plotSize), floor(z / plotSize))</c>. Distinct from
    /// <see cref="BlockPos"/> (a single world block) exactly as the old
    /// <c>SurveyCell</c> was distinct from a block position.
    /// </summary>
    /// <remarks>
    /// This type exists so the Eco-free Navigation project can model plots
    /// without referencing Eco's <c>PlotPos</c>/<c>Vector2i</c> (KTD5 — the
    /// project must have zero <c>Eco.*</c> dependency to stay unit-testable).
    /// The Eco side converts between this and <c>PlotPos</c> at the boundary.
    /// </remarks>
    public readonly struct PlotCoord : IEquatable<PlotCoord>
    {
        public int X { get; }

        public int Z { get; }

        public PlotCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>
        /// Maps a world column (x, z) to the plot that contains it. Uses floor
        /// division, not truncation, so the mapping is gapless across the origin:
        /// truncating would map both x=-1 and x=1 to plot 0 for plotSize=8,
        /// opening a gap. A column exactly on a plot boundary (x = n*plotSize)
        /// belongs to the plot that *starts* there. Same convention the old
        /// <c>SurveyGrid.CellAt</c> proved, now applied at plot granularity.
        /// </summary>
        public static PlotCoord FromWorldColumn(int worldX, int worldZ, int plotSize)
        {
            if (plotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(plotSize), "plotSize must be positive.");

            return new PlotCoord(FloorDiv(worldX, plotSize), FloorDiv(worldZ, plotSize));
        }

        private static int FloorDiv(int value, int size) => (int)Math.Floor((double)value / size);

        public bool Equals(PlotCoord other) => X == other.X && Z == other.Z;

        public override bool Equals(object obj) => obj is PlotCoord other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);

        public override string ToString() => $"plot({X}, {Z})";
    }

    /// <summary>
    /// A named, drawn survey area: a set of property plots a player marked out
    /// on the mod's own map layer for a drone to survey. The pure-logic half of
    /// the district-replacement (KTD2/KTD3) — it answers "which plots?" and "is
    /// this position inside me?" without any Eco dependency. The Eco side wraps
    /// it with an id source, colour, owner, and serialization (U4).
    /// </summary>
    public sealed class SurveyArea
    {
        private readonly HashSet<PlotCoord> _plots;

        /// <summary>Stable id assigned by the registrar; identifies the area across renames and reassignments.</summary>
        public int Id { get; }

        /// <summary>Player-facing name. Not unique — two areas may share a name and stay distinct by <see cref="Id"/>.</summary>
        public string Name { get; }

        public SurveyArea(int id, string name, IEnumerable<PlotCoord> plots)
        {
            if (plots == null)
                throw new ArgumentNullException(nameof(plots));

            Id = id;
            Name = name;
            _plots = new HashSet<PlotCoord>(plots);
        }

        /// <summary>The plots this area covers, in no particular order.</summary>
        public IReadOnlyCollection<PlotCoord> Plots => _plots;

        /// <summary>How many plots this area covers. The value R1b's tier cap is checked against.</summary>
        public int PlotCount => _plots.Count;

        /// <summary>True when this area's plot count is within <paramref name="maxPlots"/> (R1b tier cap). Exactly at the cap is allowed.</summary>
        public bool WithinPlotCap(int maxPlots) => _plots.Count <= maxPlots;

        /// <summary>True when <paramref name="plot"/> is one of this area's plots.</summary>
        public bool ContainsPlot(PlotCoord plot) => _plots.Contains(plot);

        /// <summary>
        /// True when world column (x, z) falls inside one of this area's plots.
        /// This is the membership test the drone uses to know whether its current
        /// position is still inside its assigned area — the survey-area analogue
        /// of <c>DistrictAssignment.IsPositionInDistrict</c>.
        /// </summary>
        public bool ContainsWorldColumn(int worldX, int worldZ, int plotSize) =>
            _plots.Contains(PlotCoord.FromWorldColumn(worldX, worldZ, plotSize));

        /// <summary>The plots as a set, for the map layer to paint and for coverage denominators.</summary>
        public IEnumerable<PlotCoord> EnumeratePlots() => _plots;
    }
}
