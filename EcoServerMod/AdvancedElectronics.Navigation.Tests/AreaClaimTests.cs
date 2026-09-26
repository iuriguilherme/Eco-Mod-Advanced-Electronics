using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// Claims at assignment (U13, R37, R38, R39, R40, R44, R47).
    ///
    /// <para>
    /// The decidable half of "who holds this ground" (KTD12). Three rules are pinned here and
    /// each one is a different question. <see cref="AreaClaims.HoldsClaim"/> answers whether an
    /// area holds its plots at all — assignment is what takes ground, and <c>[empty]</c> is the
    /// one status that drops it while <c>[cleared]</c> keeps it (R37).
    /// <see cref="AreaClaims.Conflicts"/> answers whether a claimant may take the ground it
    /// overlaps: kind-blind against every held area (R39), and one-way against farmland (R47).
    /// <see cref="AreaClaims.MayBeOfferedToMiningDock"/> answers what a mining dock is offered
    /// in the first place (R44, R47).
    /// </para>
    /// <para>
    /// What is NOT here is the write. The claim record lives on <c>SurveyAreaEntry</c> and the
    /// lock that makes the test-and-write atomic lives on <c>DroneDockObject</c>, both Eco-side
    /// and neither reachable without a server. The race below therefore pins the DISCIPLINE the
    /// production path is built on rather than the production path itself — see its own comment.
    /// </para>
    /// </summary>
    public class AreaClaimTests
    {
        private const int PlotSize = 5;

        private static readonly Guid DockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid DockB = Guid.Parse("22222222-2222-2222-2222-222222222222");

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

        private static IReadOnlyList<AreaClaimConflict> ConflictsFor(
            AreaProjection claimant, AreaKind claimantKind, params AreaProjection[] published) =>
            AreaClaims.Conflicts(claimantKind, AreaOverlap.Matches(claimant, published.Append(claimant)));

        // --- R37: what an area holds, and what drops it ---

        [Fact]
        public void AnUnassignedArea_HoldsNothing_AndAnAssignedOneHoldsItsPlots()
        {
            Assert.False(AreaClaims.HoldsClaim(assigned: false, AreaLifecycleStatus.Surveyed));
            Assert.True(AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Surveyed));
        }

        [Fact]
        public void AClearedAreaKeepsItsClaim_AnEmptyOneDropsIt()
        {
            // R37's one exception, and the whole reason [cleared] and [empty] are two statuses.
            // [cleared] says the drone is finished because it was REFUSED, and R19 lets a survey
            // lift that refusal, so the material behind it may still be taken by the dock that
            // holds the ground. [empty] says the ground itself is spent: there is nothing left to
            // hold it for, which is what lets land pass from one purpose to the next.
            Assert.True(AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Cleared));
            Assert.False(AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Empty));
        }

        [Fact]
        public void ReleasingAndReassigning_IsANewClaimRatherThanAResumedOne()
        {
            // The decidable half: a released area holds nothing, and assigning it again holds it
            // afresh — nothing carries across the gap. The epoch that makes the SECOND claim a
            // distinguishable record is the dock's own assignment epoch, written onto the area by
            // DroneDockObject.AssignMiningArea; that write is Eco-side and is not reachable here.
            Assert.True(AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Surveyed));
            Assert.False(AreaClaims.HoldsClaim(assigned: false, AreaLifecycleStatus.Surveyed));
            Assert.True(AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Surveyed));
        }

        // --- R39: kind-blind against anything held ---

        [Fact]
        public void AssigningOverAnAssignedFarm_IsRefused_NamingThosePlotsAndTheFarm()
        {
            // AE12. The player drew a mining area overlapping four plots of an assigned farming
            // area. Creating it was never blocked (R35) -- this is the moment they assign it.
            var farm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: true, kind: AreaKind.Farming);
            var mine = Area(2, DockB, Block(0, 0, 2, 3));

            var conflict = Assert.Single(ConflictsFor(mine, AreaKind.Mining, farm));

            Assert.Equal(4, conflict.Plots.Count);

            var refusal = MiningReadout.FormatClaimRefusal(new[] { conflict }, PlotSize);

            // Which plots: the centre block of each, the mod's own way of naming ground (R36).
            Assert.Contains("4 plots", refusal);
            Assert.Contains("(2, 2)", refusal);
            Assert.Contains("(2, 7)", refusal);
            Assert.Contains("(7, 2)", refusal);
            Assert.Contains("(7, 7)", refusal);

            // And what holds them. R47 makes farmland's block kind-dependent, so the player has
            // to be told it is a farm -- otherwise they cannot know that unassigning it changes
            // nothing and only deleting it will.
            Assert.Contains("farm", refusal, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AssigningOverAnUnassignedArea_Succeeds()
        {
            // An unassigned area holds nothing, whoever owns it and whatever it is for. This is
            // the same geometry as AE12 with the farm's assignment taken away.
            var idleMine = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: false);
            var mine = Area(2, DockB, Block(0, 0, 2, 3));

            Assert.Empty(ConflictsFor(mine, AreaKind.Mining, idleMine));
        }

        [Fact]
        public void AClearedAreaStillHoldsItsClaimAgainstAnotherDock()
        {
            // [cleared] is held ground: HoldsClaim says so, and the conflict test refuses it the
            // same way it refuses any other held area. Nothing about the refusal is kind-aware
            // here -- both areas are mining ground.
            var cleared = Area(
                1, DockA, Block(0, 0, 2, 2),
                holdsClaim: AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Cleared));
            var mine = Area(2, DockB, Block(1, 1, 2, 2));

            var conflict = Assert.Single(ConflictsFor(mine, AreaKind.Mining, cleared));

            Assert.Equal(AreaClaimBlock.HeldByAssignment, conflict.Reason);

            // R41: a kind-blind block discloses no kind. "Not shown" is not "not known" -- the
            // enforcement path reads Kind, the player-facing string does not.
            var refusal = MiningReadout.FormatClaimRefusal(new[] { conflict }, PlotSize);
            Assert.DoesNotContain("farm", refusal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mining", refusal, StringComparison.OrdinalIgnoreCase);
        }

        // --- R47: farmland is reserved, and the asymmetry runs one way ---

        [Fact]
        public void AMiningDockCannotClaimGroundAnUnassignedFarmCovers()
        {
            // The [farm] mark reserves the GROUND, not the assignment. An unassigned farm is
            // between passes, not finished -- farmland never reaches an exhausted state the way a
            // mine does, so there is no status that could release it. HoldsClaim alone cannot
            // express that, which is why the projection carries Kind.
            var idleFarm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: false, kind: AreaKind.Farming);
            var mine = Area(2, DockB, Block(0, 0, 2, 2));

            var conflict = Assert.Single(ConflictsFor(mine, AreaKind.Mining, idleFarm));

            Assert.Equal(AreaClaimBlock.FarmlandReserved, conflict.Reason);

            // And the refusal says what would actually lift it: deleting the farming area, which
            // R47 makes the owner's explicit act. Telling them to unassign it would send them to
            // do the one thing that changes nothing.
            var refusal = MiningReadout.FormatClaimRefusal(new[] { conflict }, PlotSize);
            Assert.Contains("delete", refusal, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AFarmMayTakeGroundAMineHasFinishedWith()
        {
            // AE13, and the other side of R47's asymmetry. The mining area's ground reads
            // [empty], so it holds nothing (R37) -- and nothing about it being mining ground
            // blocks a farm. Refused nothing: no release is negotiated and neither area is
            // edited. What marks the ground farmland from then on is the farming data recorded
            // there (R40), which the farming plan owns.
            var spentMine = Area(
                1, DockA, Block(0, 0, 2, 2),
                holdsClaim: AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Empty));
            var farm = Area(2, DockB, Block(0, 0, 2, 2), kind: AreaKind.Farming);

            Assert.Empty(ConflictsFor(farm, AreaKind.Farming, spentMine));
        }

        [Fact]
        public void TheAsymmetryRunsOneWayOnTheSameGeometry()
        {
            // One pair of areas, read from both ends. A mine may not take the farm's ground; the
            // farm may take the mine's. Neither is assigned, so R39 has nothing to say and the
            // whole difference is R47's.
            var farm = Area(1, DockA, Block(0, 0, 2, 2), kind: AreaKind.Farming);
            var mine = Area(2, DockB, Block(0, 0, 2, 2));

            Assert.Single(ConflictsFor(mine, AreaKind.Mining, farm));
            Assert.Empty(ConflictsFor(farm, AreaKind.Farming, mine));
        }

        // --- R37, R39: one area, one holder ---

        [Fact]
        public void AnAreaAlreadyHeldByAnotherDock_IsRefusedToASecondDock()
        {
            // Two docks never both work one area. R37 says a drone works only plots ITS OWN dock
            // has claimed and R39 says any assigned area holds its plots against every dock, so
            // the second dock would be working ground it does not hold.
            //
            // This is the SAME-area case, and it is a separate test from the overlap scan on
            // purpose: AreaOverlap.Matches skips self by identity -- correctly, since an area does
            // not overlap itself -- so an area's own claim is invisible to that path and has to be
            // asked about directly.
            var held = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);

            var conflict = Assert.Single(AreaClaims.ConflictOnTheAreaItself(held, claimantIsHolder: false));

            Assert.Equal(AreaClaimBlock.HeldByAssignment, conflict.Reason);
            Assert.Equal(4, conflict.Plots.Count);

            // Named in the same shape every other refusal uses, through the same formatter.
            var refusal = MiningReadout.FormatClaimRefusal(new[] { conflict }, PlotSize);
            Assert.Contains("4 plots", refusal);
            Assert.Contains("(2, 2)", refusal);
            Assert.Contains("held by", refusal);
        }

        [Fact]
        public void ReassigningToTheDockThatAlreadyHoldsIt_IsNotRefused()
        {
            // The ordinary re-dispatch path, and the one R45 makes load-bearing: assignment IS
            // the retry that lifts this dock's attempt-fact exclusions, so a dock re-pointed at
            // the area it already holds must go through, not be refused by its own claim.
            var held = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: true);

            Assert.Empty(AreaClaims.ConflictOnTheAreaItself(held, claimantIsHolder: true));
        }

        [Fact]
        public void AnUnheldArea_IsRefusedToNobody()
        {
            // Including the [empty] case: HoldsClaim has already folded that in, so ground with
            // nothing left to take reaches here reading unheld and passes to any dock (R37).
            var free = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: false);

            Assert.Empty(AreaClaims.ConflictOnTheAreaItself(free, claimantIsHolder: false));
            Assert.Empty(AreaClaims.ConflictOnTheAreaItself(
                Area(1, DockA, Block(0, 0, 2, 2),
                     holdsClaim: AreaClaims.HoldsClaim(assigned: true, AreaLifecycleStatus.Empty)),
                claimantIsHolder: false));
            Assert.Empty(AreaClaims.ConflictOnTheAreaItself(null, claimantIsHolder: false));
        }

        [Fact]
        public void AClaimantSkipsGroundItAlreadyHolds()
        {
            // Reassigning a dock from one area to another that overlaps its own held ground is
            // not a conflict with anybody: the dock is the holder. The predicate is the caller's
            // because the holder's identity is deliberately absent from the projection (R41) --
            // the Eco side knows which areas it holds, this path only knows they are held.
            //
            // The held area sits on ANOTHER dock: a mining dock holds its claim on a survey
            // dock's area, which is the real shape of this case. Both areas on one dock would
            // now be exempt under R7a before the predicate is ever consulted, so the predicate
            // would prove nothing there.
            var ownHeld = Area(1, DockB, Block(0, 0, 2, 2), holdsClaim: true);
            var next = Area(2, DockA, Block(1, 1, 2, 2));

            var matches = AreaOverlap.Matches(next, new[] { ownHeld, next });

            Assert.Single(AreaClaims.Conflicts(AreaKind.Mining, matches));
            Assert.Empty(AreaClaims.Conflicts(
                AreaKind.Mining, matches, claimantAlreadyHolds: p => p.AreaId == 1 && p.OwningDockId == DockB));
        }

        [Fact]
        public void FarmlandIsReservedEvenWhenTheClaimantAlreadyHoldsGround()
        {
            // The "already holds it" escape above lifts an ordinary hold, never the reservation:
            // R47 is a fact about the ground, so it survives the claimant holding a claim already.
            //
            // The two areas sit on DIFFERENT docks. R7a exempts a dock from colliding with itself,
            // so the same geometry on one dock is no conflict at all -- see
            // TwoOverlappingAreasOnOneDock_DoNotCollide_InEitherKindOrder. This test is what is
            // left of the reservation once that exemption is in force: it binds across docks.
            var ownFarm = Area(1, DockB, Block(0, 0, 2, 2), holdsClaim: true, kind: AreaKind.Farming);
            var mine = Area(2, DockA, Block(0, 0, 2, 2));

            var conflict = Assert.Single(AreaClaims.Conflicts(
                AreaKind.Mining,
                AreaOverlap.Matches(mine, new[] { ownFarm, mine }),
                claimantAlreadyHolds: _ => true));

            Assert.Equal(AreaClaimBlock.FarmlandReserved, conflict.Reason);
        }

        // --- R44: what a mining dock is offered at all ---

        [Fact]
        public void AClearedOrEmptyAreaIsNotOfferedToAMiningDock()
        {
            Assert.False(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Mining, AreaLifecycleStatus.Cleared));
            Assert.False(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Mining, AreaLifecycleStatus.Empty));

            // Everything still on the ramp is.
            Assert.True(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Mining, AreaLifecycleStatus.Unsurveyed));
            Assert.True(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Mining, AreaLifecycleStatus.Surveyed));
            Assert.True(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Mining, AreaLifecycleStatus.Digging));
            Assert.True(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Mining, AreaLifecycleStatus.Mined));
        }

        [Fact]
        public void AClearedAreaStaysAssignableToASurveyDock()
        {
            // R44's other half: a survey dock is the only thing that can return [cleared] or
            // [empty] to the ramp, so the offer test it consults is not this one. Nothing in the
            // survey path reads MayBeOfferedToMiningDock, and the status ladder itself keeps
            // saying so.
            Assert.False(AreaLifecycle.IsOfferableToMiningDock(AreaLifecycleStatus.Cleared));
            Assert.False(AreaLifecycle.IsOfferableToMiningDock(AreaLifecycleStatus.Empty));
        }

        [Fact]
        public void FarmlandIsNeverOfferedToAMiningDock_WhateverItsStatusSlotSays()
        {
            // R47 at the offer, not only at the claim. A farming area reads [farm] and never a
            // rung of the ramp, so the ladder's own test cannot answer this -- kind has to.
            Assert.False(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Farming, AreaLifecycleStatus.Farm));
            Assert.False(AreaClaims.MayBeOfferedToMiningDock(AreaKind.Farming, AreaLifecycleStatus.Surveyed));
        }

        // --- R38: unassigning states what it released ---

        [Fact]
        public void UnassigningStatesWhatItReleased_EvenWhenNothingElseWantedIt()
        {
            // Unconditional (R38), not fired only on contested ground: the claim is what
            // assignment MEANS, and a player who does not know they dropped it cannot know they
            // are exposed. No other area appears anywhere in this call.
            var line = MiningReadout.FormatClaimRelease(Block(0, 0, 2, 2), PlotSize);

            Assert.Contains("4 plots", line);
            Assert.Contains("(2, 2)", line);
            Assert.Contains("claim", line, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AReleaseOfOnePlotReadsAsOnePlot_AndNoPlotsSaysNothing()
        {
            Assert.Contains("1 plot ", MiningReadout.FormatClaimRelease(Block(0, 0, 1, 1), PlotSize));

            // An add-only edit releases nothing (U9), and a message about nothing is noise.
            Assert.Equal(string.Empty, MiningReadout.FormatClaimRelease(Array.Empty<PlotCoord>(), PlotSize));
            Assert.Equal(string.Empty, MiningReadout.FormatClaimRelease(null, PlotSize));
        }

        [Fact]
        public void ALongReleaseNamesTheFirstFewPlotsAndCountsTheRest()
        {
            // A 64-plot area unassigned must not print 64 coordinate pairs into chat.
            var line = MiningReadout.FormatClaimRelease(Block(0, 0, 8, 8), PlotSize);

            Assert.Contains("64 plots", line);
            Assert.Contains("58 more", line);
        }

        [Fact]
        public void AnEditReleasesExactlyThePlotsItRemoved_AndTheClaimItselfStands()
        {
            // U9 already settled which plots leave a claim; U13 only has to say so. A claim is
            // keyed to the assignment rather than to the geometry, so the edit never invalidates
            // it -- it simply stands over whatever plots the area holds now.
            var plan = AreaEdit.Plan(Block(0, 0, 3, 1), Block(1, 0, 3, 1));

            Assert.Equal(new[] { new PlotCoord(0, 0) }, plan.ReleasedFromClaim);

            var line = MiningReadout.FormatClaimRelease(plan.ReleasedFromClaim, PlotSize);
            Assert.Contains("1 plot ", line);
            Assert.Contains("(2, 2)", line);
        }

        // --- R39: the claim is atomic against a concurrent assignment ---

        [Fact]
        public async Task TwoAssignmentsRacingOverTheSamePlots_ProduceOneWinnerAndOneRefusal()
        {
            // KTD6's correction, pinned as the DISCIPLINE the Eco-side path is built on: ONE lock
            // spanning the whole test-and-write, not a lock per area. The decision function under
            // test is the production one (AreaClaims.Conflicts); the book of held areas stands in
            // for the claim records on SurveyAreaEntry, which are Eco-side and unreachable here.
            //
            // The two claimants assign two DIFFERENT areas that overlap each other. That is the
            // case a per-area lock cannot serialise -- each would take a different area's lock and
            // neither would serialise against the other -- and it is why the production lock is
            // one static object on DroneDockObject rather than a field on the entry.
            var contested = Block(4, 4, 2, 2);
            var first = Area(1, DockA, Block(3, 3, 3, 3));
            var second = Area(2, DockB, Block(4, 4, 3, 3));

            for (var attempt = 0; attempt < 200; attempt++)
            {
                var gate = new ManualResetEventSlim(false);
                var claimLock = new object();
                var held = new HashSet<int>();          // area ids currently holding their plots
                var wins = 0;

                bool Claim(AreaProjection self, AreaProjection other)
                {
                    gate.Wait();
                    lock (claimLock)
                    {
                        var published = new[] { self, Area(other.AreaId, other.OwningDockId, other.Plots, held.Contains(other.AreaId)) };
                        if (AreaClaims.Conflicts(AreaKind.Mining, AreaOverlap.Matches(self, published)).Count > 0)
                            return false;

                        held.Add(self.AreaId);
                        Interlocked.Increment(ref wins);
                        return true;
                    }
                }

                var a = Task.Run(() => Claim(first, second));
                var b = Task.Run(() => Claim(second, first));
                gate.Set();

                var results = await Task.WhenAll(a, b);

                Assert.Equal(1, wins);
                Assert.Single(results, r => r);
                Assert.Single(results, r => !r);
                Assert.Equal(4, AreaOverlap.SharedPlots(first.Plots, second.Plots).Count);
                Assert.Equal(contested.OrderBy(p => p.X).ThenBy(p => p.Z), AreaOverlap.SharedPlots(first.Plots, second.Plots));
            }
        }

        // --- R7a: a dock does not hold ground against itself ---

        [Fact]
        public void TwoOverlappingAreasOnOneDock_DoNotCollide_InEitherKindOrder()
        {
            // AE5a, R7a. A dock hosts one drone and that drone's tool decides its job, so a dock
            // cannot work two kinds of ground at once. The pair that reaches this state is the
            // drone swap: a dock still holding the farm it worked last season, and a mining area
            // assigned to it today. Without the exemption the dock's own farm would reserve the
            // ground against the dock itself -- R3 reserves it while unassigned -- which is the
            // one refusal no act of the player's could lift.
            var farm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: true, kind: AreaKind.Farming);
            var mine = Area(2, DockA, Block(0, 0, 2, 3), holdsClaim: true);

            Assert.Empty(ConflictsFor(mine, AreaKind.Mining, farm));
            Assert.Empty(ConflictsFor(farm, AreaKind.Farming, mine));
        }

        [Fact]
        public void TheFarmlandBranchDoesNotFireBetweenTwoAreasOnOneDock()
        {
            // The exemption sits AHEAD of the farmland branch, not behind it. R47's reservation
            // is the one rule that ignores whether the farm is assigned, so a same-dock pair
            // reaching that branch would be refused whatever the player did to the farm. The
            // reason is asserted as well as the count, because an empty list alone would also be
            // produced by an exemption placed after the branch on ground holding no claim.
            var idleFarm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: false, kind: AreaKind.Farming);
            var mine = Area(2, DockA, Block(0, 0, 2, 2));

            var conflicts = ConflictsFor(mine, AreaKind.Mining, idleFarm);

            Assert.DoesNotContain(conflicts, c => c.Reason == AreaClaimBlock.FarmlandReserved);
            Assert.Empty(conflicts);
        }

        [Fact]
        public void TheSameTwoGeometriesOnTwoDocks_DoCollide()
        {
            // R7. The exemption is scoped to the DOCK and not to the geometry: move one of the
            // two areas above onto a second dock and the same plots collide again. Two docks over
            // one piece of ground is the case the rule exists for, and the same-dock exemption
            // must not quietly widen into "overlapping areas do not collide".
            var idleFarm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: false, kind: AreaKind.Farming);
            var mine = Area(2, DockB, Block(0, 0, 2, 2));

            var conflict = Assert.Single(ConflictsFor(mine, AreaKind.Mining, idleFarm));

            Assert.Equal(AreaClaimBlock.FarmlandReserved, conflict.Reason);
            Assert.Equal(4, conflict.Plots.Count);
        }

        [Fact]
        public void AnUnassignedFarmingArea_StillHoldsItsPlotsAgainstAnotherDock()
        {
            // R3, restated against the world walk this unit opens to farms. A farm between
            // harvests is idle, not finished: it holds nothing HoldsClaim can express -- that
            // flag reads assignment -- and it reserves its ground all the same, because R47's
            // branch reads KIND rather than the flag. The two facts are asserted together
            // because the first is what makes the second load-bearing.
            var idleFarm = Area(1, DockA, Block(0, 0, 2, 2), holdsClaim: false, kind: AreaKind.Farming);

            Assert.False(AreaClaims.HoldsClaim(assigned: false, AreaLifecycleStatus.Farm));

            var conflict = Assert.Single(
                ConflictsFor(Area(1, DockB, Block(0, 0, 2, 2)), AreaKind.Mining, idleFarm));

            Assert.Equal(AreaClaimBlock.FarmlandReserved, conflict.Reason);
        }

        // --- R15: the reason an assignment was undone at load ---

        [Fact]
        public void TheReconciliationReasonNamesThePlotsAndWhatHoldsThem()
        {
            // R15. An assignment reconciliation undid has to say the same two things every other
            // refusal says -- which ground, and what holds it -- plus the one thing only this
            // case has to say: that the load did this, not the player. Without that the area
            // reads as an assignment they forgot to make.
            var line = MiningReadout.FormatReconciliationBlock(
                AreaClaimBlock.FarmlandReserved, Block(0, 0, 2, 2), PlotSize);

            Assert.Contains("unassigned at load", line);
            Assert.Contains("4 plots", line);
            Assert.Contains("(2, 2)", line);
            Assert.Contains("(7, 7)", line);
            Assert.Contains("farmland", line, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheReconciliationReasonSpeaksTheRefusalsOwnVocabulary()
        {
            // One vocabulary, not a second one worded for the load path. A player who has read
            // the assignment-time refusal already knows what "held by another area's assignment"
            // means and what lifts it; a paraphrase here would be a second thing to learn about
            // the same rule.
            var plots = Block(0, 0, 2, 2);

            Assert.Contains(
                MiningReadout.FormatClaimRefusal(
                    new[] { new AreaClaimConflict(null, plots, AreaClaimBlock.HeldByAssignment) }, PlotSize),
                MiningReadout.FormatReconciliationBlock(AreaClaimBlock.HeldByAssignment, plots, PlotSize));
        }

        [Fact]
        public void AnAreaWithNoReconciliationBlockReadsExactlyAsItDoesToday()
        {
            // R16's no-op case costs the panel nothing: no reason, no row, and every existing
            // reason string unchanged.
            Assert.Equal(string.Empty, MiningReadout.FormatReconciliationBlock(null, Block(0, 0, 1, 1), PlotSize));
            Assert.Equal(string.Empty, MiningReadout.FormatReconciliationBlock(
                AreaClaimBlock.HeldByAssignment, Array.Empty<PlotCoord>(), PlotSize));
            Assert.Equal(string.Empty, MiningReadout.FormatReconciliationBlock(
                AreaClaimBlock.HeldByAssignment, null, PlotSize));

            Assert.Equal("the area was unassigned", MiningReadout.FormatBlockedReason(false, MiningEndReason.Unassigned));
        }

        [Fact]
        public void TheReconciliationReasonOutranksTheJobsOwnEndReason_AndTheHaltOutranksIt()
        {
            // The job that was running ended reading "the area was unassigned", which is true and
            // says nothing about why -- the same trap R23's out-of-range notice was added to
            // escape. The server-wide halt still speaks first: it refuses dispatch before a job
            // exists at all.
            var reconciled = MiningReadout.FormatReconciliationBlock(
                AreaClaimBlock.FarmlandReserved, Block(0, 0, 1, 1), PlotSize);

            Assert.Equal(
                reconciled,
                MiningReadout.FormatBlockedReason(false, MiningEndReason.Unassigned, false, reconciled));

            Assert.DoesNotContain(
                "unassigned at load",
                MiningReadout.FormatBlockedReason(true, MiningEndReason.Unassigned, false, reconciled));
        }

        [Fact]
        public void AFarmHeldByAnOverlapRendersARealPlotCount()
        {
            // R15 on the farm side. The stall reason and its renderer have both existed since the
            // farming plan with nothing writing them; what this pins is that the count reaching
            // the renderer is the one the conflict actually named, so the Eco-side defensive
            // minimum of 1 stops being the only number this row can ever show.
            var text = FarmReadout.FormatStall(FarmAreaState.HeldByOverlap("North", "Corn", heldPlotCount: 4));

            Assert.Contains("4", text);
            Assert.DoesNotContain("1 plot ", text);
        }

        // --- Degenerate inputs ---

        [Fact]
        public void ConflictsToleratesNullAndEmptyInput()
        {
            Assert.Empty(AreaClaims.Conflicts(AreaKind.Mining, null));
            Assert.Empty(AreaClaims.Conflicts(AreaKind.Mining, Array.Empty<AreaOverlapMatch>()));
            Assert.Equal(string.Empty, MiningReadout.FormatClaimRefusal(null, PlotSize));
            Assert.Equal(string.Empty, MiningReadout.FormatClaimRefusal(Array.Empty<AreaClaimConflict>(), PlotSize));
        }
    }
}
