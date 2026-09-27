using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// What a load-time pass undoes, and what it leaves alone (U5, R3, R4, R5, R13, R14, R15, R16).
    ///
    /// <para>
    /// Reconciliation is the assignment-time test applied to a save written before that test
    /// existed. It decides nothing of its own: <see cref="AreaClaims.Conflicts"/> answers who may
    /// hold which ground, and this pass adds exactly one rule on top — KTD10's offender rule,
    /// which picks ONE side of a collision so a one-sided query asked from both ends does not
    /// undo both.
    /// </para>
    /// <para>
    /// The pass is a pure function of the projections, which is the whole reason it is here: the
    /// input order is <c>IWorldObjectManager.All</c>'s and is not stable, so "the same save
    /// reconciles the same way on every load" is a property only a test can hold down. Several
    /// cases below therefore run the same world twice, in two orders.
    /// </para>
    /// <para>
    /// What is NOT here is the effect. Unassigning, recording the reason and recalling the drone
    /// are Eco-side, in <c>DroneDock.Migration.cs</c>, and the test project cannot reach them —
    /// they are proven in the batched live session.
    /// </para>
    /// </summary>
    public class AreaReconciliationTests
    {
        private static readonly Guid DockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid DockB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid DockC = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid DockD = Guid.Parse("44444444-4444-4444-4444-444444444444");

        private static List<PlotCoord> Block(int x, int z, int width, int depth)
        {
            var plots = new List<PlotCoord>();
            for (var dx = 0; dx < width; dx++)
            for (var dz = 0; dz < depth; dz++)
                plots.Add(new PlotCoord(x + dx, z + dz));
            return plots;
        }

        private static AreaProjection Area(
            int id, Guid dock, IEnumerable<PlotCoord> plots, bool holdsClaim = false, AreaKind kind = AreaKind.Mining) =>
            new AreaProjection(id, dock, plots, holdsClaim, kind);

        private static AreaProjection Mine(int id, Guid dock, IEnumerable<PlotCoord> plots, bool holdsClaim) =>
            Area(id, dock, plots, holdsClaim, AreaKind.Mining);

        private static AreaProjection Farm(int id, Guid dock, IEnumerable<PlotCoord> plots, bool holdsClaim) =>
            Area(id, dock, plots, holdsClaim, AreaKind.Farming);

        private static IReadOnlyList<AreaToUnassign> Undo(params AreaProjection[] world) =>
            AreaReconciliation.AssignmentsToUndo(world);

        /// <summary>
        /// The same world with the projections handed over in the opposite order — what a second
        /// load looks like, since nothing orders <c>IWorldObjectManager.All</c>.
        /// </summary>
        private static IReadOnlyList<AreaToUnassign> UndoReversed(params AreaProjection[] world) =>
            AreaReconciliation.AssignmentsToUndo(world.Reverse());

        /// <summary>
        /// The world as it stands after the pass has been applied: every area it named has been
        /// unassigned, so it holds nothing. This is what R16's second load actually sees.
        /// </summary>
        private static IReadOnlyList<AreaProjection> AfterApplying(
            IReadOnlyList<AreaToUnassign> undone, params AreaProjection[] world) =>
            world
                .Select(a => undone.Any(u => u.Area.IsSameAreaAs(a))
                    ? Area(a.AreaId, a.OwningDockId, a.Plots, holdsClaim: false, a.Kind)
                    : a)
                .ToList();

        // --- R13, AE8: a mining assignment standing on a farm ---

        [Fact]
        public void AMiningAssignmentOverAnAssignedFarm_IsUndone_NamingTheSharedPlotsAndTheFarm()
        {
            // AE8. The save was written before any of this existed: a mining dock is assigned to
            // an area overlapping four plots of another dock's farm, and the drone is digging.
            // R13 undoes the assignment, and R15 needs the ground and the holder named.
            var farm = Farm(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var mine = Mine(2, DockB, Block(0, 0, 2, 3), holdsClaim: true);

            var undone = Assert.Single(Undo(farm, mine));

            Assert.True(undone.Area.IsSameAreaAs(mine));
            Assert.Equal(AreaClaimBlock.FarmlandReserved, undone.Reason);
            Assert.True(undone.Holder.IsSameAreaAs(farm));

            // Exactly the ground in dispute, not the whole of either area: the mining area covers
            // six plots and the collision is four of them.
            Assert.Equal(4, undone.ContestedPlots.Count);
            Assert.All(undone.ContestedPlots, p => Assert.Contains(p, farm.Plots));
        }

        [Fact]
        public void TheFarmIsNeverTheOneUndone_HoweverTheProjectionsArrive()
        {
            // KTD10. AreaClaims.Conflicts is a ONE-SIDED query: asked from the mine it reports
            // the farmland reservation, and asked from the farm it reports the mine's own
            // assignment holding the same plots. Without the offender rule one collision would
            // undo both docks' assignments, which is not what either R3 or R4 says: the farm is
            // entitled to the ground, so the mining side is the offender and only it is undone.
            var farm = Farm(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var mine = Mine(2, DockB, Block(0, 0, 2, 2), holdsClaim: true);

            foreach (var undone in new[] { Undo(farm, mine), UndoReversed(farm, mine) })
            {
                var only = Assert.Single(undone);
                Assert.True(only.Area.IsSameAreaAs(mine));
                Assert.DoesNotContain(undone, u => u.Area.IsSameAreaAs(farm));
            }
        }

        [Fact]
        public void AMiningAssignmentOverAnUnassignedFarm_IsStillUndone()
        {
            // R3. A farm between harvests is idle, not finished -- farmland never reaches the
            // exhausted state a mine does -- so its ground is held against a mining dock whether
            // or not anyone is farming it. The farm holds no claim and is not itself a candidate
            // for undoing; the mine standing on it still is.
            var idleFarm = Farm(1, DockA, Block(0, 0, 2, 2), holdsClaim: false);
            var mine = Mine(2, DockB, Block(1, 1, 2, 2), holdsClaim: true);

            var undone = Assert.Single(Undo(idleFarm, mine));

            Assert.True(undone.Area.IsSameAreaAs(mine));
            Assert.Equal(AreaClaimBlock.FarmlandReserved, undone.Reason);
            Assert.Single(undone.ContestedPlots);
        }

        // --- R4, R5: which way the asymmetry runs ---

        [Fact]
        public void AFarmOverMiningGroundThatReadsEmpty_IsNotReconciledAtAll()
        {
            // AE2, R4. The mine has finished: [empty] drops its claim (AreaClaims.HoldsClaim
            // folds that in before the projection is built), so it holds nothing and the farm is
            // entitled to the ground. Two facts are asserted by the one empty result, and both
            // matter. The FARM is not undone, because nothing holds what it took. And the MINE is
            // not undone either: R13 speaks of an area HOLDING plots it is not entitled to, and
            // an area holding nothing holds no plots -- reconciliation writes nothing here.
            var spentMine = Mine(1, DockA, Block(0, 0, 2, 2), holdsClaim: false);
            var farm = Farm(2, DockB, Block(0, 0, 2, 2), holdsClaim: true);

            Assert.Empty(Undo(spentMine, farm));
            Assert.Empty(UndoReversed(spentMine, farm));
        }

        [Fact]
        public void AFarmOverClearedMiningGround_IsReconciled_UnlikeAFarmOverEmptyGround()
        {
            // AE3, R5. [cleared] is NOT [empty]: the drone stopped because it was refused, the
            // exclusion behind that refusal may lift, and the claim therefore stands. So this
            // collision is real where the one above is not, and the pass has something to undo.
            //
            // WHICH side it undoes is KTD10's, not R5's, and the plan's scenario list words this
            // case as though the farm were the one undone. It is not, and it cannot be. The
            // projection carries HOLDS-A-CLAIM and not the status behind it, so this world is
            // indistinguishable here from an ordinary assigned mine overlapping an assigned farm
            // -- the case directly above, which KTD10 settles in the farm's favour. And undoing
            // the farm would leave the mine standing on farmland, an R3 violation reconciliation
            // had just been asked to remove: the next load would undo the mine as well, so the
            // pass would not be the fixed point R16 requires. Undoing the mine reaches that fixed
            // point in one pass, which the idempotence case below asserts outright.
            var clearedMine = Mine(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var farm = Farm(2, DockB, Block(0, 0, 2, 2), holdsClaim: true);

            var undone = Assert.Single(Undo(clearedMine, farm));

            Assert.True(undone.Area.IsSameAreaAs(clearedMine));
            Assert.Equal(AreaClaimBlock.FarmlandReserved, undone.Reason);
        }

        // --- KTD10: one side of a collision, chosen the same way on every load ---

        [Fact]
        public void TwoMiningAreasOnDifferentDocks_UndoExactlyOne_AndTheSameOneEitherWayRound()
        {
            // Neither R3 nor R4 settles a same-kind collision, so the rule is a total order over
            // the identity: the higher (owning dock id, area id) pair is the offender. Any total
            // order would do. What matters is that the answer does not depend on the order the
            // projections arrived in, because IWorldObjectManager.All has none -- without this
            // the same save would undo dock A on one load and dock B on the next.
            var first = Mine(7, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var second = Mine(3, DockB, Block(1, 0, 2, 2), holdsClaim: true);

            var forwards = Assert.Single(Undo(first, second));
            var backwards = Assert.Single(UndoReversed(first, second));

            // DockB sorts above DockA, so the second area is the offender however it arrives --
            // and the area id, which points the other way here, does not get a vote while the
            // docks differ.
            Assert.True(forwards.Area.IsSameAreaAs(second));
            Assert.True(backwards.Area.IsSameAreaAs(second));
            Assert.Equal(AreaClaimBlock.HeldByAssignment, forwards.Reason);
            Assert.True(forwards.Holder.IsSameAreaAs(first));
        }

        [Fact]
        public void TwoFarmsOnDifferentDocks_AreSettledByTheSameOrder()
        {
            // The same-kind rule is not a mining rule. Two farms colliding across docks is the
            // other half of it, and the farm's exemption from KTD10's cross-kind branch must not
            // widen into "a farm is never undone at all".
            var first = Farm(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var second = Farm(1, DockB, Block(0, 0, 2, 2), holdsClaim: true);

            var undone = Assert.Single(Undo(first, second));

            Assert.True(undone.Area.IsSameAreaAs(second));
            Assert.Equal(AreaClaimBlock.HeldByAssignment, undone.Reason);
        }

        // --- KTD10 in a chain: undo the minimum that settles it ---

        [Fact]
        public void AChainOfThree_UndoesOnlyTheMiddle_LeavingTheFarEndAlone()
        {
            // A--B share ground and B--C share different ground, but A and C never touch. Undoing
            // B settles both collisions at once: A keeps the ground it held, and C was only ever
            // in conflict with B.
            //
            // The trap is that the offender rule is pairwise. Asked in isolation, C IS the
            // offender against B -- its dock sorts higher. So a pass that decides each area
            // against the world as it arrived undoes C as well, for a rival that no longer
            // exists by the time the pass ends. The player whose dock C was assigned to ground
            // nobody else wanted finds it unassigned and its drone home.
            var a = Mine(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var b = Mine(1, DockB, Block(1, 0, 3, 2), holdsClaim: true);
            var c = Mine(1, DockC, Block(3, 0, 2, 2), holdsClaim: true);

            foreach (var undone in new[] { Undo(a, b, c), UndoReversed(a, b, c) })
            {
                var only = Assert.Single(undone);
                Assert.True(only.Area.IsSameAreaAs(b), "only the middle of the chain gives way");
            }
        }

        [Fact]
        public void AChainOfFour_UndoesTwo_AndNotTheThirdThatNeedsNothingUndone()
        {
            // A--B, B--C, C--D, and no other pair touches. Undoing B frees A and C; D is then
            // still standing on C's ground, so D gives way too. Two undone, not three: C keeps
            // its claim because the only area it ever collided with is already gone.
            //
            // This is the case that rules out the obvious repair of dropping any area whose
            // conflicts were all themselves undone -- that drops C AND D, and leaves C and D
            // overlapping and both holding, which is the state the pass exists to remove.
            var a = Mine(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var b = Mine(1, DockB, Block(1, 0, 2, 2), holdsClaim: true);
            var c = Mine(1, DockC, Block(2, 0, 2, 2), holdsClaim: true);
            var d = Mine(1, DockD, Block(3, 0, 2, 2), holdsClaim: true);

            foreach (var undone in new[] { Undo(a, b, c, d), UndoReversed(a, b, c, d) })
            {
                Assert.Equal(2, undone.Count);
                Assert.Contains(undone, u => u.Area.IsSameAreaAs(b));
                Assert.Contains(undone, u => u.Area.IsSameAreaAs(d));
                Assert.DoesNotContain(undone, u => u.Area.IsSameAreaAs(c));
            }
        }

        [Fact]
        public void AChainIsSettledInOnePass_WithNothingLeftOverlappingAndHolding()
        {
            // The property the two cases above are really about: after the pass is applied, no
            // two areas that share ground are both still holding. Minimal is only useful if it
            // is also complete.
            var a = Mine(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var b = Mine(1, DockB, Block(1, 0, 2, 2), holdsClaim: true);
            var c = Mine(1, DockC, Block(2, 0, 2, 2), holdsClaim: true);
            var d = Mine(1, DockD, Block(3, 0, 2, 2), holdsClaim: true);

            var after = AfterApplying(Undo(a, b, c, d), a, b, c, d);

            Assert.Empty(AreaReconciliation.AssignmentsToUndo(after));

            foreach (var left in after.Where(x => x.HoldsClaim))
            foreach (var right in after.Where(x => x.HoldsClaim && !x.IsSameAreaAs(left)))
                Assert.Empty(AreaOverlap.SharedPlots(left.Plots, right.Plots));
        }

        // --- R7a: a dock does not hold ground against itself ---

        [Fact]
        public void TwoAreasOnOneDock_AreNeverUndone_WhateverTheirKinds()
        {
            // R7a, inherited rather than restated: AreaClaims.Conflicts drops a same-dock match
            // ahead of the farmland branch, so the drone-swap case -- a dock still holding last
            // season's farm while a mining area is assigned to it today -- arrives here with no
            // conflict to answer for. Reconciliation must not reintroduce it by asking a
            // different question than the assignment path asks.
            var farm = Farm(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);
            var mine = Mine(2, DockA, Block(0, 0, 2, 2), holdsClaim: true);

            Assert.Empty(Undo(farm, mine));
        }

        // --- R13: only a held assignment is ever undone ---

        [Fact]
        public void AnUnassignedAreaIsNeverUndone_HoweverMuchItOverlaps()
        {
            // It holds nothing, so there is nothing to take from it and nothing it has taken.
            // The area it overlaps is the one holding ground, and that one is entitled to it.
            var idle = Mine(1, DockA, Block(0, 0, 3, 3), holdsClaim: false);
            var working = Mine(2, DockB, Block(0, 0, 3, 3), holdsClaim: true);

            Assert.Empty(Undo(idle, working));
        }

        [Fact]
        public void AnAreaCollidingWithTwoOthers_IsUndoneOnce_NamingBothPatchesOfGround()
        {
            // One assignment is undone once however many areas it collides with, and the record
            // R15 leaves behind has to name all of the ground in dispute -- a player sent to one
            // of two contested patches would go and look, find the overlap, clear it, and still
            // be blocked.
            var farmWest = Farm(1, DockA, Block(0, 0, 1, 1), holdsClaim: false);
            var farmEast = Farm(1, DockB, Block(4, 0, 1, 1), holdsClaim: true);
            var mine = Mine(9, DockC, Block(0, 0, 5, 1), holdsClaim: true);

            var undone = Assert.Single(Undo(farmWest, farmEast, mine));

            Assert.True(undone.Area.IsSameAreaAs(mine));
            Assert.Equal(2, undone.ContestedPlots.Count);
            Assert.Contains(new PlotCoord(0, 0), undone.ContestedPlots);
            Assert.Contains(new PlotCoord(4, 0), undone.ContestedPlots);

            // Raster order, the order every other plot list in the mod is read down in.
            Assert.Equal(undone.ContestedPlots.OrderBy(p => p.X).ThenBy(p => p.Z), undone.ContestedPlots);
        }

        [Fact]
        public void AFarmlandReservationOutranksAPlainHoldWhenOneAreaHasBoth()
        {
            // The two blocks are lifted by different acts -- an assignment can be unassigned,
            // farmland cannot -- so an area blocked by both has to report the one that will still
            // be there after the player has done the thing the other one asks for.
            var otherMine = Mine(1, DockA, Block(0, 0, 1, 1), holdsClaim: true);
            var farm = Farm(1, DockB, Block(1, 0, 1, 1), holdsClaim: true);
            var mine = Mine(4, DockC, Block(0, 0, 2, 1), holdsClaim: true);

            var undone = Undo(otherMine, farm, mine).Single(u => u.Area.IsSameAreaAs(mine));

            Assert.Equal(AreaClaimBlock.FarmlandReserved, undone.Reason);
            Assert.True(undone.Holder.IsSameAreaAs(farm));
            Assert.Equal(2, undone.ContestedPlots.Count);
        }

        // --- R16: once per load, and nothing at all when nothing conflicts ---

        [Fact]
        public void AWorldWithNoCollisionsIsUndisturbed()
        {
            // AE9. The ordinary load, and the one that has to cost nothing: areas that do not
            // share ground, assigned or not, of either kind.
            Assert.Empty(Undo(
                Mine(1, DockA, Block(0, 0, 2, 2), holdsClaim: true),
                Farm(2, DockB, Block(9, 9, 2, 2), holdsClaim: true),
                Mine(3, DockC, Block(20, 0, 1, 1), holdsClaim: false)));
        }

        [Fact]
        public void RunningTheSamePassOverWhatItProducedReturnsNothing()
        {
            // R16's fixed point, and the reason the offender rule cannot undo "whichever side
            // asked first": apply the pass, and the world it leaves must reconcile clean. A pass
            // that had to run twice would either keep unassigning areas on every load or leave a
            // violation standing for the next one to find.
            var world = new[]
            {
                Farm(1, DockA, Block(0, 0, 2, 2), holdsClaim: true),
                Mine(2, DockB, Block(0, 0, 2, 2), holdsClaim: true),
                Mine(5, DockC, Block(1, 1, 3, 3), holdsClaim: true),
            };

            var first = AreaReconciliation.AssignmentsToUndo(world);
            Assert.NotEmpty(first);

            var settled = AfterApplying(first, world);
            Assert.Empty(AreaReconciliation.AssignmentsToUndo(settled));
            Assert.Empty(AreaReconciliation.AssignmentsToUndo(settled.Reverse()));
        }

        // --- R15: how an undone assignment reads afterwards ---
        //
        // The decision above says WHICH assignment goes; these say what the player finds when
        // they next open a dock. They live here rather than with the other roster-line tests
        // because the row they pin exists for exactly one producer -- the record U5 writes -- and
        // a reader asking "who shows the reconciliation reason" should find the answer beside it.

        [Fact]
        public void AReconciledAreaReadsAsBlockedOnItsOwnRosterLine_NamingTheGroundAndWhatLiftsIt()
        {
            // R15. The mining dock's blocked row cannot carry this: reconciliation cleared that
            // dock's assignment, so the tab has nothing left pointing at the area that holds the
            // record. The AREA still has it, and its roster line is rendered by one builder for
            // both tabs -- so wherever the player reads the area, they read the same reason.
            var block = MiningReadout.FormatReconciliationBlock(
                AreaClaimBlock.FarmlandReserved, Block(0, 0, 2, 2), 5);

            var line = DockReadout.FormatRosterLine(Snapshot(reconciliationBlock: block));

            // The area's own figures are untouched and still on the first row -- the whole reason
            // the reason gets a row of its own.
            Assert.StartsWith("<color=green>1. North Ridge -- 4 plots,", line);
            Assert.Contains("[surveyed]</color>", line);

            // And the reason follows, indented, in the colour this mod already uses for the one
            // line a player must not miss.
            Assert.Contains($"\n    <color={FarmReadout.NeedsYouColor}>", line);
            Assert.Contains("unassigned at load", line);
            Assert.Contains("4 plots", line);
            Assert.Contains("(7, 7)", line);
            Assert.Contains("delete that farming area", line);
        }

        [Fact]
        public void AnAreaReconciliationLeftAloneRendersExactlyAsItDidBefore()
        {
            // R16 again, at the panel: the ordinary area is the overwhelming majority of every
            // roster, and it must not gain a row, a separator or a trailing newline from a
            // feature that did not touch it.
            var untouched = DockReadout.FormatRosterLine(Snapshot());

            Assert.Equal(untouched, DockReadout.FormatRosterLine(Snapshot(reconciliationBlock: null)));
            Assert.Equal(untouched, DockReadout.FormatRosterLine(Snapshot(reconciliationBlock: string.Empty)));
            Assert.DoesNotContain("\n", untouched);
        }

        private static AreaSnapshot Snapshot(string reconciliationBlock = null) =>
            new AreaSnapshot(
                1, "North Ridge", 4, 100f, SurveyFinding.NotFound, AreaLifecycleStatus.Surveyed,
                isAssigned: false, isUnreachable: false, hasOverlap: false, needsResurvey: false,
                reconciliationBlock: reconciliationBlock);

        // --- Degenerate inputs ---

        [Fact]
        public void NullAndEmptyWorldsReconcileToNothing()
        {
            Assert.Empty(AreaReconciliation.AssignmentsToUndo(null));
            Assert.Empty(AreaReconciliation.AssignmentsToUndo(Array.Empty<AreaProjection>()));
        }

        [Fact]
        public void NullEntriesAndPlotlessAreasArePassedOverRatherThanThrownOn()
        {
            // This runs at world load, where a throw costs the load and leaves the player no way
            // to reach a dock and repair anything -- the same posture the fold takes over a
            // malformed legacy row.
            var plotless = Mine(1, DockA, Array.Empty<PlotCoord>(), holdsClaim: true);
            var farm = Farm(2, DockB, Block(0, 0, 2, 2), holdsClaim: true);

            Assert.Empty(AreaReconciliation.AssignmentsToUndo(new[] { null, plotless, null, farm }));
        }
    }
}
