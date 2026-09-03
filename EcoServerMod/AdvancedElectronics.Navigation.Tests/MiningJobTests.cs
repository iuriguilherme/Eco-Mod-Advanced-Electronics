using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    public class MiningJobTests
    {
        private static readonly PlotCoord P00 = new PlotCoord(0, 0);
        private static readonly PlotCoord P10 = new PlotCoord(1, 0);
        private static readonly PlotCoord P01 = new PlotCoord(0, 1);

        private static bool AllSurveyed(PlotCoord _) => true;

        [Fact]
        public void FreshJob_StartsIdle_MovesToWorkingOnDispatch()
        {
            var job = new MiningJob(new[] { P00 });
            Assert.Equal(MiningJobStatus.Idle, job.Status);

            job.Dispatch();

            Assert.Equal(MiningJobStatus.Working, job.Status);
        }

        [Fact]
        public void FinishingAPlot_MarksItWorked_OffersNextUnworkedPlot()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            var next = job.NextPlot(AllSurveyed);
            Assert.Equal(P00, next); // raster order: Z then X

            job.MarkWorked(P00);

            Assert.Equal(PlotOutcome.Worked, job.OutcomeOf(P00));
            Assert.Equal(P10, job.NextPlot(AllSurveyed));
        }

        [Fact]
        public void RefusedUnload_MovesWorkingToWaitingToUnload_LaterSuccessReturnsToWorking()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();

            job.OnUnloadRefused();
            Assert.Equal(MiningJobStatus.WaitingToUnload, job.Status);

            job.OnUnloadSucceeded();
            Assert.Equal(MiningJobStatus.Working, job.Status);
        }

        [Fact]
        public void JobWhoseEveryPlotIsSkipped_ReachesComplete_ZeroWorkedEveryPlotSkipped()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            job.MarkSkipped(P00, SkipCategory.Property);
            Assert.False(job.TryComplete(AllSurveyed));

            job.MarkSkipped(P10, SkipCategory.Unreachable);
            Assert.True(job.TryComplete(AllSurveyed));

            Assert.Equal(MiningJobStatus.Complete, job.Status);
            Assert.Equal(0, job.WorkedCount);
            Assert.Equal(2, job.SkippedCount);
        }

        [Fact]
        public void EachSkipCategory_RecordedAndCountedSeparately_NoDoubleCounting()
        {
            var plots = new[] { P00, P10, P01, new PlotCoord(2, 0), new PlotCoord(2, 1) };
            var job = new MiningJob(plots);
            job.Dispatch();

            job.MarkSkipped(plots[0], SkipCategory.Unreachable);
            job.MarkSkipped(plots[1], SkipCategory.Property);
            job.MarkSkipped(plots[2], SkipCategory.SettlementLaw);
            job.MarkSkipped(plots[3], SkipCategory.Obstructed);
            job.MarkSkipped(plots[4], SkipCategory.Other);

            var counts = job.SkipCountsByCategory();
            Assert.Equal(1, counts[SkipCategory.Unreachable]);
            Assert.Equal(1, counts[SkipCategory.Property]);
            Assert.Equal(1, counts[SkipCategory.SettlementLaw]);
            Assert.Equal(1, counts[SkipCategory.Obstructed]);
            Assert.Equal(1, counts[SkipCategory.Other]);
            Assert.Equal(job.SkippedCount, counts.Values.Sum());
        }

        [Theory]
        [InlineData(RemovalRefusalStage.Property, SkipCategory.Property)]
        [InlineData(RemovalRefusalStage.SettlementLaw, SkipCategory.SettlementLaw)]
        [InlineData(RemovalRefusalStage.Pretest, SkipCategory.Obstructed)]
        [InlineData(RemovalRefusalStage.Unrecognised, SkipCategory.Other)]
        public void RefusalMapping_ReturnsExpectedCategory_UnrecognisedFallsBackToOther(RemovalRefusalStage stage, SkipCategory expected)
        {
            Assert.Equal(expected, RefusalMapping.ToSkipCategory(stage));
        }

        [Fact]
        public void WorkedPlot_NotReOfferedAsNextPlot()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();
            job.MarkWorked(P00);

            Assert.Null(job.NextPlot(AllSurveyed));
        }

        [Fact]
        public void SkippedPlot_NotReOfferedWithinSameJob()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();
            job.MarkSkipped(P00, SkipCategory.Obstructed);

            Assert.Null(job.NextPlot(AllSurveyed));
        }

        [Fact]
        public void EndingFromWorkingAndFromWaitingToUnload_EachPreservesLedgerAndCarriesEndReason()
        {
            var jobA = new MiningJob(new[] { P00, P10 });
            jobA.Dispatch();
            jobA.MarkWorked(P00);
            jobA.End(MiningEndReason.AreaGone);

            Assert.Equal(MiningJobStatus.Ended, jobA.Status);
            Assert.Equal(MiningEndReason.AreaGone, jobA.EndReason);
            Assert.Equal(PlotOutcome.Worked, jobA.OutcomeOf(P00));

            var jobB = new MiningJob(new[] { P00, P10 });
            jobB.Dispatch();
            jobB.MarkWorked(P00);
            jobB.OnUnloadRefused();
            jobB.End(MiningEndReason.Halted);

            Assert.Equal(MiningJobStatus.Ended, jobB.Status);
            Assert.Equal(MiningEndReason.Halted, jobB.EndReason);
            Assert.Equal(PlotOutcome.Worked, jobB.OutcomeOf(P00));
        }

        [Fact]
        public void EndingFromIdle_EndsTheJob_SoAJobThatNeverSetOutCanStillStop()
        {
            // The state a job sits in until its first dispatch succeeds. A job whose area was
            // deleted before it ever set out must be able to end here, or it reports itself
            // neither finished nor stoppable and the lifecycle re-dispatches it forever.
            var job = new MiningJob(new[] { P00, P10 });

            Assert.Equal(MiningJobStatus.Idle, job.Status);
            job.End(MiningEndReason.AreaGone);

            Assert.Equal(MiningJobStatus.Ended, job.Status);
            Assert.Equal(MiningEndReason.AreaGone, job.EndReason);
        }

        [Fact]
        public void EndingFromTerminalStates_IsIgnored_SoAFinishedJobIsNotRelabelled()
        {
            var completed = new MiningJob(new[] { P00 });
            completed.Dispatch();
            completed.MarkWorked(P00);
            Assert.True(completed.TryComplete(AllSurveyed));

            completed.End(MiningEndReason.AreaGone);
            Assert.Equal(MiningJobStatus.Complete, completed.Status);
            Assert.Null(completed.EndReason);

            var ended = new MiningJob(new[] { P00 });
            ended.Dispatch();
            ended.End(MiningEndReason.Halted);
            ended.End(MiningEndReason.AreaGone);

            Assert.Equal(MiningJobStatus.Ended, ended.Status);
            Assert.Equal(MiningEndReason.Halted, ended.EndReason);
        }

        [Fact]
        public void ReMarkingAlreadyWorkedPlot_IsIdempotent_DoesNotInflateCount()
        {
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();

            job.MarkWorked(P00);
            job.MarkWorked(P00);

            Assert.Equal(1, job.WorkedCount);
        }

        // Supplemental: R17/AE6 -- an unsurveyed plot is never offered and never blocks
        // completion, so an area with plots the survey drone has not reached still
        // completes once every SURVEYED plot is worked or skipped.
        [Fact]
        public void UnsurveyedPlot_NeverOffered_NeverBlocksCompletion()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            bool IsSurveyed(PlotCoord p) => p.Equals(P00); // P10 never surveyed

            Assert.Equal(P00, job.NextPlot(IsSurveyed));

            job.MarkWorked(P00);

            Assert.Null(job.NextPlot(IsSurveyed));
            Assert.True(job.TryComplete(IsSurveyed));
            Assert.Equal(PlotOutcome.Unworked, job.OutcomeOf(P10));
        }

        [Fact]
        public void SnapshotRoundTrips_ThroughProjectionAndRehydration_IdenticalCounts()
        {
            var job = new MiningJob(new[] { P00, P10, P01 });
            job.Dispatch();
            job.MarkWorked(P00);
            job.MarkSkipped(P10, SkipCategory.Property);

            var snapshot = job.ToSnapshot();
            var rehydrated = MiningJob.FromSnapshot(snapshot);

            Assert.Equal(job.Status, rehydrated.Status);
            Assert.Equal(job.WorkedCount, rehydrated.WorkedCount);
            Assert.Equal(job.SkippedCount, rehydrated.SkippedCount);
            Assert.Equal(PlotOutcome.Worked, rehydrated.OutcomeOf(P00));
            Assert.Equal(PlotOutcome.Skipped, rehydrated.OutcomeOf(P10));
            Assert.Equal(job.SkipCountsByCategory()[SkipCategory.Property], rehydrated.SkipCountsByCategory()[SkipCategory.Property]);
        }

        [Fact]
        public void AFreshJobOverAnAlreadyMinedArea_CompletesImmediatelyWithNothingWorked()
        {
            // The player's "is there anything left here?" check: assign a mining drone to an area
            // it already worked out and let it answer. Nothing is surveyed-fresh, so nothing is
            // offered, and the job is finished on its first tick rather than idle with no target.
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();

            Assert.True(job.TryComplete(NoneSurveyed));
            Assert.Equal(MiningJobStatus.Complete, job.Status);
            Assert.Equal(0, job.WorkedCount);
            Assert.Equal(0, job.SkippedCount);
            Assert.Null(job.NextPlot(NoneSurveyed));
        }

        private static bool NoneSurveyed(PlotCoord plot) => false;

        // ------------------------------------------------------------------
        // U3: the exclusion ledger and its reach (R18, R19, R27).
        //
        // Two docks are named throughout: DockA hits the refusal, DockB has access.
        // The ledger is the whole decidable half of this unit -- which categories are
        // attempt facts, how a suppression set is unioned per dock, and when a survey
        // lifts an exclusion (KTD5, KTD12).
        // ------------------------------------------------------------------

        private const string DockA = "dock-a";
        private const string DockB = "dock-b";

        [Fact]
        public void EverySkipCategoryIsAnAttemptFact_NoneBindsAnotherDock()
        {
            // R18's reach split, stated over the whole enum rather than the values that
            // happen to exist today: a mining pass records NO ground facts. Obstructed is
            // the classifier's catch-all for a refusal that was neither law nor property --
            // R7's single-column obstruction, which never stops a plot reaching bedrock --
            // and bedrock never reaches this ledger at all, because MiningStrategy filters
            // NotRemovable positions out before submission and advances the layer without
            // recording a skip. The one ground fact is the area's at-bedrock observation (U4).
            foreach (SkipCategory category in System.Enum.GetValues(typeof(SkipCategory)))
                Assert.Equal(ExclusionReach.Attempt, MiningExclusion.ReachOf(category));
        }

        [Fact]
        public void SettlementLawRefusalByOneDock_DoesNotSuppressThatPlotForAnotherDock()
        {
            var ledger = new MiningExclusionLedger();
            ledger.Record(new MiningExclusion(DockA, P00, SkipCategory.SettlementLaw, "Refused under settlement law."));

            Assert.True(ledger.SuppressesFor(DockA, P00));
            Assert.False(ledger.SuppressesFor(DockB, P00));
        }

        [Fact]
        public void AGroundFact_SuppressesThatPlotForEveryDock()
        {
            // The at-bedrock observation U4 writes per column onto the area (KTD4). It is
            // holderless by construction -- no dock recorded it, the ground did.
            var ledger = new MiningExclusionLedger();
            ledger.RecordGroundFact(P01);

            Assert.True(ledger.SuppressesFor(DockA, P01));
            Assert.True(ledger.SuppressesFor(DockB, P01));
        }

        [Fact]
        public void CoversAE10_OneSettlementLawPlot_IsAccountedForRegardlessOfHolder_AndNamesItsReason()
        {
            // The half of AE10 this unit owns: the exclusion is shared information, so the
            // area's status reads it whoever recorded it, and it carries the wording R27
            // reports on the mining tab. Turning "an exclusion accounts for this plot" into
            // [cleared] rather than [empty] is U5's derivation, which reads exactly these
            // two answers.
            var ledger = new MiningExclusionLedger();
            ledger.RecordGroundFact(P00);
            ledger.RecordGroundFact(P10);
            ledger.Record(new MiningExclusion(DockA, P01, SkipCategory.SettlementLaw, "Refused under settlement law."));

            // Read with no filter on holder -- what the one shared status is derived from.
            var accounted = ledger.AttemptFacts;
            Assert.Equal(P01, Assert.Single(accounted).Plot);
            Assert.Equal(SkipCategory.SettlementLaw, accounted[0].Category);
            Assert.Equal("Refused under settlement law.", accounted[0].Detail);

            // ... and the plot is genuinely still accounted for, rather than exhausted.
            Assert.True(ledger.IsAccountedForByAttempt(P01));
            Assert.False(ledger.IsAccountedForByAttempt(P00));
        }

        [Fact]
        public void CoversAE15_ReassigningTheAreaToThatDock_ClearsItsExclusions_AndNothingElseDoes()
        {
            // R45: an attempt-fact exclusion is lifted by assigning the area to that mining
            // dock again, and by nothing else. Only a mining drone can learn a refusal -- it
            // attempts the action in place and captures the reason -- so only a mining attempt
            // can learn the refusal has gone. A survey cannot test settlement law or property,
            // and material proves nothing about a permit refusal: the refusal leaves the
            // material exactly where it was. The player is the signal, and assigning is how
            // they give it.
            var ledger = new MiningExclusionLedger();
            ledger.Record(new MiningExclusion(DockA, P00, SkipCategory.SettlementLaw, "Refused under settlement law."));
            ledger.Record(new MiningExclusion(DockB, P10, SkipCategory.Property, "Refused under private property."));
            ledger.RecordGroundFact(P01);

            // Resurveying in between changes nothing: there is no survey-driven lift to call,
            // and reading the ledger any number of times leaves every record standing.
            Assert.True(ledger.SuppressesFor(DockA, P00));
            Assert.True(ledger.SuppressesFor(DockA, P00));
            Assert.Equal(2, ledger.AttemptFacts.Count);

            // The player assigns the area to DockA again. That, and only that, is the retry.
            var lifted = ledger.ClearAttemptFactsFor(DockA);

            Assert.Equal(P00, Assert.Single(lifted).Plot);
            Assert.False(ledger.SuppressesFor(DockA, P00));

            // Scoped to the assigning dock: DockB's own refusal is its own knowledge, and
            // someone else's assignment does not clear it.
            Assert.True(ledger.SuppressesFor(DockB, P10));
            Assert.Equal(P10, Assert.Single(ledger.AttemptFacts).Plot);

            // And the ground fact needs no lift: the survey re-derives at-bedrock from the
            // ground on every pass, so an assignment must not touch it.
            Assert.True(ledger.SuppressesFor(DockA, P01));
            Assert.True(ledger.SuppressesFor(DockB, P01));
        }

        [Fact]
        public void ClearingOneDocksExclusions_WhenItHasNone_IsANoOp()
        {
            var ledger = new MiningExclusionLedger();
            ledger.Record(new MiningExclusion(DockB, P10, SkipCategory.Property, "property"));

            Assert.Empty(ledger.ClearAttemptFactsFor(DockA));
            Assert.Single(ledger.AttemptFacts);
        }

        [Fact]
        public void AnExclusionSetFromTwoDocks_ReadsAsOneRecord_ButYieldsTwoDifferentOfferLists()
        {
            // R19's whole point, and R26's: what varies per dock is which plots it is
            // OFFERED, never what the area says it is.
            var ledger = new MiningExclusionLedger();
            ledger.Record(new MiningExclusion(DockA, P00, SkipCategory.SettlementLaw, "law"));
            ledger.Record(new MiningExclusion(DockB, P10, SkipCategory.Property, "property"));
            ledger.RecordGroundFact(P01);

            // One shared record, both entries, whoever asks.
            Assert.Equal(2, ledger.AttemptFacts.Count);

            var area = new[] { P00, P10, P01 };
            Assert.Equal(new[] { P10 }, area.Where(p => !ledger.SuppressesFor(DockA, p)).ToArray());
            Assert.Equal(new[] { P00 }, area.Where(p => !ledger.SuppressesFor(DockB, p)).ToArray());
        }

        [Fact]
        public void AJobThatSkippedNothing_LeavesNoExclusionBehind()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.MarkWorked(P00);
            job.MarkWorked(P10);
            job.TryComplete(AllSurveyed);

            Assert.Empty(job.SkippedPlots());

            var ledger = new MiningExclusionLedger();
            foreach (var skip in job.SkippedPlots())
                ledger.Record(new MiningExclusion(DockA, skip.Plot, skip.Category, skip.Detail));

            Assert.Empty(ledger.AttemptFacts);
            Assert.False(ledger.SuppressesFor(DockA, P00));
        }

        [Fact]
        public void TheJobsSkippedLedgerCarriesThePlotTheCategoryAndTheRefusalDetail()
        {
            // KTD5: no second refusal vocabulary is introduced -- the exclusion the dock
            // persists is exactly what the job already recorded, detail included.
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.MarkSkipped(P00, SkipCategory.SettlementLaw, "Refused under settlement law.");
            job.MarkWorked(P10);

            var skip = Assert.Single(job.SkippedPlots());
            Assert.Equal(P00, skip.Plot);
            Assert.Equal(SkipCategory.SettlementLaw, skip.Category);
            Assert.Equal("Refused under settlement law.", skip.Detail);
        }

        [Fact]
        public void ASkippedPlotSurvivesTheJobSnapshotRoundTrip_DetailIncluded()
        {
            // The exclusion is written from the job's ledger, and a job is rehydrated from
            // its snapshot after a restart, so the detail has to survive the projection.
            var job = new MiningJob(new[] { P00 });
            job.Dispatch();
            job.MarkSkipped(P00, SkipCategory.Property, "You do not have permission here.");

            var restored = MiningJob.FromSnapshot(job.ToSnapshot());

            var skip = Assert.Single(restored.SkippedPlots());
            Assert.Equal(SkipCategory.Property, skip.Category);
            Assert.Equal("You do not have permission here.", skip.Detail);
        }

        [Fact]
        public void CoversAE10_TheExclusionNamesItsRefusalWhereReasonsAreAlreadyReported()
        {
            // R27's half of AE10: the reason survives the job that hit it, worded with the same
            // category vocabulary the skip line uses and carrying the engine's own words. An
            // area whose [cleared] rests on this must be able to say what would unblock it.
            var ledger = new MiningExclusionLedger();
            ledger.Record(new MiningExclusion(DockA, P00, SkipCategory.SettlementLaw, "Refused by settlement law 'No Digging'."));

            var line = MiningReadout.FormatExclusionLine(ledger.AttemptFacts);

            Assert.Contains("not authorized (settlement law)", line);
            Assert.Contains("No Digging", line);

            // And it names the remedy that actually works (R45). Saying "resurvey" here would
            // send the player to do the one thing that cannot lift this.
            Assert.Contains("reassigned", line);
            Assert.DoesNotContain("resurvey", line);
        }

        [Fact]
        public void AnAreaExcludingNothing_RendersNoExclusionLineAtAll()
        {
            Assert.Equal(string.Empty, MiningReadout.FormatExclusionLine(new MiningExclusionLedger().AttemptFacts));
        }

        [Fact]
        public void TryComplete_NoOp_WhenNotWorking()
        {
            var job = new MiningJob(new[] { P00 });
            Assert.False(job.TryComplete(AllSurveyed)); // still Idle

            job.Dispatch();
            job.MarkWorked(P00);
            job.TryComplete(AllSurveyed);
            Assert.False(job.TryComplete(AllSurveyed)); // already Complete
        }

        // --- U9/R21: an edit ends a job only when it removes plots the job still has to work ---

        [Fact]
        public void PendingPlots_AreTheOnesNeitherWorkedNorSkipped()
        {
            var job = new MiningJob(new[] { P00, P10, P01 });
            job.Dispatch();
            job.MarkWorked(P00);
            job.MarkSkipped(P10, SkipCategory.SettlementLaw);

            Assert.Equal(new[] { P01 }, job.PendingPlots());
        }

        /// <summary>
        /// R21. An edit that only adds plots leaves a running job alive: every plot it was built
        /// against is still there, so there is nothing it can no longer finish. This is what makes
        /// R20's preservation something the player can actually see rather than a fact about
        /// storage.
        /// </summary>
        [Fact]
        public void AnEditThatOnlyAddsPlots_LeavesTheJobRunning()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.MarkWorked(P00);

            var after = new[] { P00, P10, P01, new PlotCoord(1, 1) };

            Assert.False(AreaEdit.RemovesPendingWork(job.PendingPlots(), after));
            Assert.Equal(
                AreaResolutionOutcome.Reacquired,
                AreaResolutionPolicy.Resolve(AreaLookupSignal.Found, "1:0", "1:1", editRemovedPendingWork: () => false));
        }

        /// <summary>
        /// R21. An edit that removes a plot the job has NOT reached yet takes work the job can
        /// never finish and never account for, so the job ends with the redraw reason.
        /// </summary>
        [Fact]
        public void AnEditRemovingAPlotTheJobStillHasToWork_EndsTheJobAsRedrawn()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.MarkWorked(P00);

            Assert.True(AreaEdit.RemovesPendingWork(job.PendingPlots(), new[] { P00 }));
            Assert.Equal(
                AreaResolutionOutcome.Invalidated,
                AreaResolutionPolicy.Resolve(AreaLookupSignal.Found, "1:0", "1:1", editRemovedPendingWork: () => true));

            job.End(MiningEndReason.AreaRedrawn);
            Assert.Equal(MiningJobStatus.Ended, job.Status);
            Assert.Equal(MiningEndReason.AreaRedrawn, job.EndReason);
        }

        /// <summary>
        /// R21. A plot the job has ALREADY worked leaving the area takes nothing with it: the
        /// outcome is recorded and the job is done with it, so the job runs on.
        /// </summary>
        [Fact]
        public void AnEditRemovingAPlotTheJobAlreadyWorked_DoesNotEndIt()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.MarkWorked(P00);

            Assert.False(AreaEdit.RemovesPendingWork(job.PendingPlots(), new[] { P10 }));
        }

        /// <summary>
        /// R21. The same for a plot the job SKIPPED. A skip is an outcome, not an omission --
        /// it is exactly what the exclusion ledger is built from (U3) -- so the job has no more
        /// business with that plot either.
        /// </summary>
        [Fact]
        public void AnEditRemovingAPlotTheJobSkipped_DoesNotEndIt()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.MarkSkipped(P00, SkipCategory.SettlementLaw);
            job.MarkWorked(P10);

            Assert.False(AreaEdit.RemovesPendingWork(job.PendingPlots(), new[] { P10 }));
        }

        /// <summary>
        /// R21. The narrowing is on the change token only. An area CONFIRMED GONE still ends the
        /// job whatever it has left to do -- there is no area to run on -- which is why a vanished
        /// area and a reshaped one stay different answers.
        /// </summary>
        [Fact]
        public void AVanishedArea_StillEndsTheJob_EvenWithNothingPendingToRemove()
        {
            Assert.Equal(
                AreaResolutionOutcome.Invalidated,
                AreaResolutionPolicy.Resolve(AreaLookupSignal.ConfirmedGone, "1:0", null, editRemovedPendingWork: () => false));
        }

        /// <summary>
        /// R21. An unedited area is untouched by the narrowing: a matching token is still
        /// straightforwardly valid, and a tick that did not resolve is still retried silently.
        /// </summary>
        [Fact]
        public void TheNarrowedResolve_LeavesTheUneditedOutcomesAlone()
        {
            Assert.Equal(
                AreaResolutionOutcome.StillValid,
                AreaResolutionPolicy.Resolve(AreaLookupSignal.Found, "1:0", "1:0", editRemovedPendingWork: () => true));
            Assert.Equal(
                AreaResolutionOutcome.NotYetResolved,
                AreaResolutionPolicy.Resolve(AreaLookupSignal.NotYetResolved, "1:0", "1:0", editRemovedPendingWork: () => true));
        }

        /// <summary>
        /// U9 step 5. A job ending under R21 changes nothing about the ASSIGNMENT, and therefore
        /// nothing about the claim the assignment carries (KTD6): assignment already outlives a
        /// job, which is why an unassign has to end the job explicitly and a job's own end does
        /// not unassign anything.
        /// </summary>
        [Fact]
        public void EndingAJobUnderR21_IsNotAnUnassign()
        {
            var job = new MiningJob(new[] { P00, P10 });
            job.Dispatch();
            job.End(MiningEndReason.AreaRedrawn);

            // The redraw reason is its own; nothing here reports the assignment as cleared.
            Assert.Equal(MiningEndReason.AreaRedrawn, job.EndReason);
            Assert.NotEqual(MiningEndReason.Unassigned, job.EndReason);
        }
    }
}
