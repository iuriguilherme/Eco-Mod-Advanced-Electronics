using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Overlap detection over the internal channel (U12, R34–R36, R41, R42, R47).
    ///
    /// <para>
    /// Two things are pinned here and both matter. The first is the geometry: which plots two
    /// areas share, found without any distance term and without any owner term, because two
    /// areas collide on the ground however far apart their docks sit and whoever owns them
    /// (R34). The second is the BOUNDARY: what a foreign area is allowed to be when it enters
    /// this path. R41 makes this channel internal — a dock placed near another player's ground
    /// grants no sight of it — so the projection is asserted field by field rather than trusted
    /// to stay thin.
    /// </para>
    /// </summary>
    public class AreaOverlapTests
    {
        private const int PlotSize = 5;

        private static readonly Guid DockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid DockB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid DockC = Guid.Parse("33333333-3333-3333-3333-333333333333");

        /// <summary>A rectangle of plots, origin inclusive.</summary>
        private static List<PlotCoord> Block(int x, int z, int width, int depth)
        {
            var plots = new List<PlotCoord>();
            for (var dx = 0; dx < width; dx++)
            for (var dz = 0; dz < depth; dz++)
                plots.Add(new PlotCoord(x + dx, z + dz));
            return plots;
        }

        private static AreaProjection Area(
            int id,
            Guid dock,
            IEnumerable<PlotCoord> plots,
            bool holdsClaim = false,
            AreaKind kind = AreaKind.Mining) =>
            new AreaProjection(id, dock, plots, holdsClaim, kind);

        // --- Geometry (R34, R35) ---

        [Fact]
        public void TwoAreasSharingFourPlots_BothReportThoseFour()
        {
            // A 2x4 strip and a 4x2 strip crossing it: they share the 2x2 corner (2,2)-(3,3).
            var mine = Area(1, DockA, Block(2, 0, 2, 4));
            var farm = Area(2, DockB, Block(0, 2, 4, 2));
            var published = new[] { mine, farm };

            var fromMine = Assert.Single(AreaOverlap.Matches(mine, published));
            var fromFarm = Assert.Single(AreaOverlap.Matches(farm, published));

            var expected = new[]
            {
                new PlotCoord(2, 2), new PlotCoord(2, 3),
                new PlotCoord(3, 2), new PlotCoord(3, 3),
            };

            Assert.Equal(expected, fromMine.SharedPlots);
            Assert.Equal(expected, fromFarm.SharedPlots);
        }

        [Fact]
        public void TwoAdjacentAreasSharingAnEdgeButNoPlot_ReportNoOverlap()
        {
            // Touching along x=1/x=2. A shared BOUNDARY is not a shared plot.
            var left = Area(1, DockA, Block(0, 0, 2, 2));
            var right = Area(2, DockB, Block(2, 0, 2, 2));

            Assert.Empty(AreaOverlap.Matches(left, new[] { left, right }));
            Assert.False(AreaOverlap.HasAny(right, new[] { left, right }));
        }

        [Fact]
        public void AreaOverlappingTwoDifferentAreas_ReportsBoth()
        {
            var spine = Area(1, DockA, Block(0, 0, 6, 1));
            var west = Area(2, DockB, Block(1, 0, 1, 1));
            var east = Area(3, DockC, Block(4, 0, 1, 1));

            var matches = AreaOverlap.Matches(spine, new[] { spine, west, east });

            Assert.Equal(2, matches.Count);
            Assert.Equal(new[] { 2, 3 }, matches.Select(m => m.Other.AreaId).OrderBy(id => id));
        }

        [Fact]
        public void AnAreaNeverOverlapsItself()
        {
            var area = Area(1, DockA, Block(0, 0, 3, 3));

            Assert.False(AreaOverlap.HasAny(area, new[] { area }));
        }

        [Fact]
        public void TwoAreasWithTheSameIdOnDifferentDocks_AreDifferentAreas()
        {
            // Area ids are per dock, so identity is the PAIR. Comparing ids alone would make a
            // real collision between two docks' area #1 read as an area overlapping itself.
            var mine = Area(1, DockA, Block(0, 0, 2, 2));
            var theirs = Area(1, DockB, Block(1, 1, 2, 2));

            Assert.Single(AreaOverlap.Matches(mine, new[] { mine, theirs }));
        }

        // --- R34: neither owner nor distance gates the test ---

        [Fact]
        public void OverlapIsFound_WhoeverOwnsTheTwoAreas()
        {
            var same = AreaOverlap.HasAny(
                Area(1, DockA, Block(0, 0, 2, 2)),
                new[] { Area(1, DockA, Block(0, 0, 2, 2)), Area(9, DockA, Block(1, 1, 2, 2)) });

            var different = AreaOverlap.HasAny(
                Area(1, DockA, Block(0, 0, 2, 2)),
                new[] { Area(1, DockA, Block(0, 0, 2, 2)), Area(9, DockB, Block(1, 1, 2, 2)) });

            Assert.True(same);
            Assert.True(different);
        }

        [Fact]
        public void OverlapIsFound_HoweverFarApartTheGroundAndTheDocksSit()
        {
            // Two areas thousands of plots from the origin and from each other's bulk, sharing a
            // single plot. Nothing in this path can express a radius: the projection carries no
            // dock position (see ProjectionCarriesGeometryAndNothingElse), so the dock-network
            // radius has nothing to filter on even if a caller wanted it to.
            var far = Area(1, DockA, Block(-40_000, 12_000, 3, 1));
            var alsoFar = Area(2, DockB, Block(-39_998, 12_000, 3, 1));

            var match = Assert.Single(AreaOverlap.Matches(far, new[] { far, alsoFar }));

            Assert.Equal(new[] { new PlotCoord(-39_998, 12_000) }, match.SharedPlots);
        }

        // --- AE12, first half ---

        [Fact]
        public void AE12_MiningAreaDrawnOverAnAssignedFarm_IsNotRefusedAndBothCarryOverlap()
        {
            // The farm is assigned and holds its four plots; the mining area has just been drawn
            // and holds nothing, which is why the farm keeps working them.
            var farm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: true, kind: AreaKind.Farming);
            var freshMine = Area(2, DockB, Block(0, 0, 2, 3));
            var published = new[] { farm, freshMine };

            // Nothing here can refuse a draw — the path returns matches, never a veto (R35).
            var onFarm = Assert.Single(AreaOverlap.Matches(farm, published));
            var onMine = Assert.Single(AreaOverlap.Matches(freshMine, published));

            Assert.Equal(4, onFarm.SharedPlots.Count);
            Assert.Equal(4, onMine.SharedPlots.Count);

            // Both lines carry the uncoloured [overlap], through the one annotation channel U6
            // built — not a second marker appended beside it.
            Assert.Equal(
                DockReadout.TagSeparator + "[overlap]",
                DockReadout.FormatAnnotations(AreaAnnotation.Overlap));

            // The farm holds its plots; the new mining area holds none until it is assigned.
            Assert.True(onMine.Other.HoldsClaim);
            Assert.False(onFarm.Other.HoldsClaim);
        }

        // --- R36: the per-plot detail ---

        [Fact]
        public void Detail_NamesTheCentreBlockOfEverySharedPlot()
        {
            var mine = Area(1, DockA, Block(2, 2, 2, 2));
            var other = Area(2, DockB, Block(2, 2, 2, 2), holdsClaim: true);

            var line = AreaOverlap.FormatOverlapDetail(
                Assert.Single(AreaOverlap.Matches(mine, new[] { mine, other })), PlotSize);

            // plot (2,2) -> block (12, 12) for a 5-block plot, and so on across the 2x2.
            Assert.Contains("(12, 12)", line);
            Assert.Contains("(12, 17)", line);
            Assert.Contains("(17, 12)", line);
            Assert.Contains("(17, 17)", line);
            Assert.Contains("4 plots", line);
        }

        [Fact]
        public void Detail_CentreBlockOfANegativePlot_StaysInsideThatPlot()
        {
            // Floor division on the way in has to be matched by a centre on the way out, or a
            // plot west of the origin names a block in its neighbour. Plot -3 spans blocks
            // -15..-11 and plot -1 spans -5..-1, so the centres are -13 and -3 -- and both map
            // back to the plot they came from.
            Assert.Equal((-13, -3), AreaOverlap.CentreColumn(new PlotCoord(-3, -1), PlotSize));
            Assert.Equal(new PlotCoord(-3, -1), PlotCoord.FromWorldColumn(-13, -3, PlotSize));
        }

        [Fact]
        public void Detail_SaysWhetherTheOtherAreaHoldsAClaimRightNow()
        {
            var mine = Area(1, DockA, Block(0, 0, 1, 1));

            var held = AreaOverlap.FormatOverlapDetail(
                Assert.Single(AreaOverlap.Matches(mine, new[] { mine, Area(2, DockB, Block(0, 0, 1, 1), holdsClaim: true) })),
                PlotSize);

            var free = AreaOverlap.FormatOverlapDetail(
                Assert.Single(AreaOverlap.Matches(mine, new[] { mine, Area(2, DockB, Block(0, 0, 1, 1)) })),
                PlotSize);

            Assert.Contains("holds them now", held);
            Assert.Contains("holds no claim", free);
            Assert.NotEqual(held, free);
        }

        [Fact]
        public void Detail_OneSharedPlot_ReadsAsOnePlotNotOnePlots()
        {
            var mine = Area(1, DockA, Block(0, 0, 1, 1));
            var line = AreaOverlap.FormatOverlapDetail(
                Assert.Single(AreaOverlap.Matches(mine, new[] { mine, Area(2, DockB, Block(0, 0, 1, 1)) })),
                PlotSize);

            Assert.Contains("1 plot ", line);
            Assert.DoesNotContain("1 plots", line);
        }

        [Fact]
        public void Detail_NamesNothingAboutTheOtherAreaBeyondItsGroundAndItsClaim()
        {
            // R42 and R30 together: not the area's name (the projection never carried one), and
            // not what it is for. Kind answers nothing about whether the block lifts, because
            // R39's claim test is kind-blind — so naming it would disclose without informing.
            var mine = Area(1, DockA, Block(0, 0, 1, 1));
            var farmNextDoor = Area(2, DockB, Block(0, 0, 1, 1), holdsClaim: true, kind: AreaKind.Farming);

            var line = AreaOverlap.FormatOverlapDetail(
                Assert.Single(AreaOverlap.Matches(mine, new[] { mine, farmNextDoor })), PlotSize);

            Assert.DoesNotContain("farm", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mining", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(DockB.ToString(), line);
        }

        [Fact]
        public void Detail_ForAnAreaOverlappingTwoOthers_IsOneLinePerCollision()
        {
            var spine = Area(1, DockA, Block(0, 0, 6, 1));
            var lines = AreaOverlap.FormatOverlapDetail(
                AreaOverlap.Matches(spine, new[]
                {
                    spine,
                    Area(2, DockB, Block(1, 0, 1, 1), holdsClaim: true),
                    Area(3, DockC, Block(4, 0, 1, 1)),
                }),
                PlotSize);

            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, l => l.Contains("holds them now"));
            Assert.Contains(lines, l => l.Contains("holds no claim"));
        }

        // --- R41: the boundary is structural, not a rule at each call site ---

        [Fact]
        public void ProjectionCarriesGeometryAndNothingElse()
        {
            // The whole of R41 in one assertion. A foreign area reaches this path as plot
            // coordinates, an identity, and a claim flag — never as the SurveyAreaEntry, which
            // carries findings, survey and mined stamps, bedrock observations, exclusions, a
            // name and a sweep cursor. A later render cannot reach through what is not here.
            var carried = typeof(AreaProjection)
                .GetProperties()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[] { "AreaId", "HoldsClaim", "Kind", "OwningDockId", "Plots" },
                carried);
        }

        [Fact]
        public void ProjectionCarriesNoFindingsNoStampsAndNoExclusions()
        {
            // Named separately from the field list above so the failure reads as the rule it
            // breaks rather than as a list that changed.
            var members = typeof(AreaProjection).GetMembers().Select(m => m.Name).ToList();

            Assert.DoesNotContain(members, n => n.IndexOf("Finding", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(members, n => n.IndexOf("Stamp", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(members, n => n.IndexOf("Exclusion", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(members, n => n.IndexOf("Bedrock", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(members, n => n.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void ProjectionCarriesKindForTheEnforcementPathEvenThoughTheDetailNeverShowsIt()
        {
            // R47: farmland's reservation outlives its assignment, so U13 has to be able to see
            // that the ground it is about to claim belongs to a farm — even an unassigned one.
            // Not shown is not the same as not known: the two consumers are different.
            var unassignedFarm = Area(2, DockB, Block(0, 0, 1, 1), holdsClaim: false, kind: AreaKind.Farming);
            var mine = Area(1, DockA, Block(0, 0, 1, 1));

            var match = Assert.Single(AreaOverlap.Matches(mine, new[] { mine, unassignedFarm }));

            Assert.Equal(AreaKind.Farming, match.Other.Kind);
            Assert.False(match.Other.HoldsClaim);
        }

        [Fact]
        public void ProjectionCopiesItsPlots_SoALaterEditCannotReachIntoThePath()
        {
            var source = Block(0, 0, 2, 2);
            var projected = Area(1, DockA, source);

            source.Clear();

            Assert.Equal(4, projected.Plots.Count);
        }

        // --- Degenerate inputs ---

        [Fact]
        public void AnAreaWithNoPlots_OverlapsNothing()
        {
            var empty = Area(1, DockA, Enumerable.Empty<PlotCoord>());
            var real = Area(2, DockB, Block(0, 0, 2, 2));

            Assert.False(AreaOverlap.HasAny(empty, new[] { empty, real }));
            Assert.False(AreaOverlap.HasAny(real, new[] { empty, real }));
        }

        [Fact]
        public void SharedPlots_TakesPlainCoordinates_AndToleratesNull()
        {
            Assert.Empty(AreaOverlap.SharedPlots(null, Block(0, 0, 1, 1)));
            Assert.Empty(AreaOverlap.SharedPlots(Block(0, 0, 1, 1), null));
            Assert.True(AreaOverlap.Overlaps(Block(0, 0, 2, 2), Block(1, 1, 2, 2)));
            Assert.False(AreaOverlap.Overlaps(Block(0, 0, 2, 2), Block(5, 5, 2, 2)));
        }
    }
}
