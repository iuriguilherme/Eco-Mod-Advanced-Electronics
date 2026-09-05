using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// The status ladder (U5, R3-R8, R26, R44). Every roster line and every offer decision
    /// reads the one function these tests pin.
    /// </summary>
    public class AreaLifecycleTests
    {
        private const string DockA = "dock-a";
        private const string DockB = "dock-b";

        private static PlotCoord Plot(int x) => new PlotCoord(x, 0);

        private static IEnumerable<PlotCoord> Plots(int count) =>
            Enumerable.Range(0, count).Select(Plot).ToList();

        /// <summary>A stamp lookup from an explicit (plot index -> stamp) map; anything absent reads 0 ("never").</summary>
        private static Func<PlotCoord, long> Stamps(params long[] byIndex) =>
            plot => plot.X >= 0 && plot.X < byIndex.Length ? byIndex[plot.X] : 0L;

        private static Func<PlotCoord, long> All(long stamp) => _ => stamp;

        private static MiningExclusionLedger Ledger(
            IEnumerable<PlotCoord> bedrock = null,
            IEnumerable<MiningExclusion> attempts = null)
        {
            var ledger = new MiningExclusionLedger();
            ledger.RecordGroundFacts(bedrock);
            foreach (var attempt in attempts ?? Enumerable.Empty<MiningExclusion>())
                ledger.Record(attempt);
            return ledger;
        }

        private static MiningExclusion Refused(string dockId, PlotCoord plot) =>
            new MiningExclusion(dockId, plot, SkipCategory.SettlementLaw, "settlement law forbids it");

        // ---------------------------------------------------------------- the ramp

        /// <summary>Covers AE1, R6. Surveyed at 100, mined at 200: nothing mineable is left.</summary>
        [Fact]
        public void SurveyedThenMined_NothingMineableLeft_IsMined()
        {
            Assert.Equal(
                AreaLifecycleStatus.Mined,
                AreaLifecycle.DeriveStatus(Plots(3), All(100), All(200), Ledger()));
        }

        /// <summary>R3: some plots mined since their survey, others still mineable.</summary>
        [Fact]
        public void SomeMinedSinceSurvey_OthersStillMineable_IsDigging()
        {
            Assert.Equal(
                AreaLifecycleStatus.Digging,
                AreaLifecycle.DeriveStatus(
                    Plots(3),
                    All(100),
                    Stamps(200, 0, 0),
                    Ledger()));
        }

        /// <summary>R3: every plot mineable and untouched.</summary>
        [Fact]
        public void EveryPlotMineableAndUntouched_IsSurveyed()
        {
            Assert.Equal(
                AreaLifecycleStatus.Surveyed,
                AreaLifecycle.DeriveStatus(Plots(3), All(100), All(0), Ledger()));
        }

        /// <summary>Covers AE4, R7. Every plot finished, and one exclusion accounts for what is left.</summary>
        [Fact]
        public void EveryPlotAtBedrock_OneExclusionAccountsForWhatIsLeft_IsCleared()
        {
            var plots = Plots(3).ToList();

            Assert.Equal(
                AreaLifecycleStatus.Cleared,
                AreaLifecycle.DeriveStatus(
                    plots,
                    All(100),
                    All(200),
                    Ledger(bedrock: plots, attempts: new[] { Refused(DockA, Plot(2)) })));
        }

        /// <summary>
        /// Covers AE10, R7, R26. The refused plot is NOT at bedrock -- the drone was turned away
        /// with material still standing -- and the area still reads finished, because R7 finishes
        /// a plot at bedrock OR under a recorded exclusion. It reads `[cleared]`, never `[empty]`:
        /// blocked ground is not spent ground.
        /// </summary>
        [Fact]
        public void EveryPlotAtBedrockExceptOneRefused_IsCleared_NotEmpty()
        {
            var status = AreaLifecycle.DeriveStatus(
                Plots(3),
                All(100),
                All(200),
                Ledger(
                    bedrock: new[] { Plot(0), Plot(1) },
                    attempts: new[] { Refused(DockA, Plot(2)) }));

            Assert.Equal(AreaLifecycleStatus.Cleared, status);
            Assert.NotEqual(AreaLifecycleStatus.Empty, status);
        }

        /// <summary>Covers AE11, R26. Every plot at bedrock and nothing excluded: the ground is spent.</summary>
        [Fact]
        public void EveryPlotAtBedrock_NoExclusion_IsEmpty()
        {
            var plots = Plots(3).ToList();

            Assert.Equal(
                AreaLifecycleStatus.Empty,
                AreaLifecycle.DeriveStatus(plots, All(100), All(200), Ledger(bedrock: plots)));
        }

        /// <summary>
        /// Covers AE5, R8. A `[cleared]` area a player filled in: the resurvey re-derives
        /// at-bedrock from the ground and finds none of it, so the area rejoins the ramp at
        /// `[surveyed]` -- `[cleared]` was never a stored flag to clear.
        /// </summary>
        [Fact]
        public void ClearedArea_GroundNowStandsAboveBedrock_RejoinsTheRamp()
        {
            var plots = Plots(3).ToList();

            Assert.Equal(
                AreaLifecycleStatus.Cleared,
                AreaLifecycle.DeriveStatus(
                    plots,
                    All(100),
                    All(200),
                    Ledger(bedrock: plots, attempts: new[] { Refused(DockA, Plot(0)) })));

            // Filled in, then resurveyed at 300: the pass records no bedrock plot at all.
            Assert.Equal(
                AreaLifecycleStatus.Surveyed,
                AreaLifecycle.DeriveStatus(
                    plots,
                    All(300),
                    All(200),
                    Ledger(attempts: new[] { Refused(DockA, Plot(0)) })));
        }

        /// <summary>R5: a resurvey of partly-dug ground returns the area to the top of the ramp.</summary>
        [Fact]
        public void ResurveyOfPartlyDugGround_IsSurveyed()
        {
            Assert.Equal(
                AreaLifecycleStatus.Surveyed,
                AreaLifecycle.DeriveStatus(
                    Plots(3),
                    All(300),                  // the fresh pass
                    Stamps(200, 200, 0),       // two plots dug before it
                    Ledger()));
        }

        // ------------------------------------------------- the unsurveyed guard comes first

        /// <summary>R3: an area with no surveyed plot at all reads unsurveyed, whatever its mined stamps say.</summary>
        [Fact]
        public void NoSurveyedPlots_IsUnsurveyed_WhateverTheMinedStampsSay()
        {
            Assert.Equal(
                AreaLifecycleStatus.Unsurveyed,
                AreaLifecycle.DeriveStatus(Plots(3), All(0), All(200), Ledger()));
        }

        /// <summary>
        /// R3, and the trap this ladder is built around. Nine mined-out plots plus one plot an
        /// edit added reads UNSURVEYED, not `[mined]`. Coding the ladder naively returns
        /// `[mined]` here: the added plot is not mineable (both its stamps are 0), and
        /// `[mined]`'s own test is "no plot is mineable" -- so the very thing that makes the
        /// added plot need a pass is what would satisfy the finished status.
        /// </summary>
        [Fact]
        public void NineMinedPlotsPlusOneEditAddedPlot_IsUnsurveyed_NotMined()
        {
            var surveyed = Stamps(100, 100, 100, 100, 100, 100, 100, 100, 100, 0);
            var mined = Stamps(200, 200, 200, 200, 200, 200, 200, 200, 200, 0);

            var status = AreaLifecycle.DeriveStatus(Plots(10), surveyed, mined, Ledger());

            Assert.Equal(AreaLifecycleStatus.Unsurveyed, status);
            Assert.NotEqual(AreaLifecycleStatus.Mined, status);
        }

        /// <summary>R3: the guard outranks the bedrock branch too -- an unobserved plot proves nothing about the floor.</summary>
        [Fact]
        public void OneUnsurveyedPlot_EveryOtherPlotAtBedrock_IsUnsurveyed()
        {
            var status = AreaLifecycle.DeriveStatus(
                Plots(3),
                Stamps(100, 100, 0),
                Stamps(200, 200, 0),
                Ledger(bedrock: new[] { Plot(0), Plot(1), Plot(2) }));

            Assert.Equal(AreaLifecycleStatus.Unsurveyed, status);
        }

        /// <summary>An area with no plots has no surveyed plot, so it reads unsurveyed rather than vacuously spent.</summary>
        [Fact]
        public void AreaWithNoPlots_IsUnsurveyed()
        {
            Assert.Equal(
                AreaLifecycleStatus.Unsurveyed,
                AreaLifecycle.DeriveStatus(Array.Empty<PlotCoord>(), All(0), All(0), Ledger()));
        }

        // ------------------------------------------------------- one status, every dock's facts

        /// <summary>
        /// R26. Two docks were each refused a different plot. The status reads BOTH refusals,
        /// because the exclusion is shared information even though the offer it drives is not,
        /// so both docks and the owning survey dock read one `[cleared]`.
        /// </summary>
        [Fact]
        public void ExclusionsRecordedByTwoDocks_BothCountTowardTheOneStatus()
        {
            var plots = Plots(2).ToList();
            var bothDocks = new[] { Refused(DockA, Plot(0)), Refused(DockB, Plot(1)) };

            Assert.Equal(
                AreaLifecycleStatus.Cleared,
                AreaLifecycle.DeriveStatus(plots, All(100), All(200), Ledger(attempts: bothDocks)));
        }

        /// <summary>
        /// R26, stated as the bug it prevents. Passing only the READING dock's exclusions leaves
        /// the other dock's plot unaccounted for, and the same area reads `[mined]` to one dock
        /// and `[cleared]` to the other -- exactly the cross-dock disagreement this plan removes.
        /// The assembled union is what the caller must pass.
        /// </summary>
        [Fact]
        public void OnlyTheReadingDocksExclusions_MakesTheStatusDisagreeAcrossDocks()
        {
            var plots = Plots(2).ToList();

            var dockAOnly = AreaLifecycle.DeriveStatus(
                plots, All(100), All(200), Ledger(attempts: new[] { Refused(DockA, Plot(0)) }));
            var dockBOnly = AreaLifecycle.DeriveStatus(
                plots, All(100), All(200), Ledger(attempts: new[] { Refused(DockB, Plot(1)) }));
            var assembled = AreaLifecycle.DeriveStatus(
                plots, All(100), All(200),
                Ledger(attempts: new[] { Refused(DockA, Plot(0)), Refused(DockB, Plot(1)) }));

            Assert.Equal(AreaLifecycleStatus.Mined, dockAOnly);
            Assert.Equal(AreaLifecycleStatus.Mined, dockBOnly);
            Assert.Equal(AreaLifecycleStatus.Cleared, assembled);
        }

        /// <summary>The union assembler is the named source of the exclusion set (U5 approach step 5).</summary>
        [Fact]
        public void AssembleExclusions_UnionsTheAreasBedrockWithEveryDocksAttemptFacts()
        {
            var assembled = AreaLifecycle.AssembleExclusions(
                bedrockPlots: new[] { Plot(0) },
                everyDockAttemptFacts: new[] { Refused(DockA, Plot(1)), Refused(DockB, Plot(2)) });

            Assert.Contains(Plot(0), assembled.GroundFacts);
            Assert.Equal(2, assembled.AttemptFacts.Count);
            Assert.True(assembled.IsAccountedForByAttempt(Plot(1)));
            Assert.True(assembled.IsAccountedForByAttempt(Plot(2)));

            Assert.Equal(
                AreaLifecycleStatus.Cleared,
                AreaLifecycle.DeriveStatus(Plots(3), All(100), All(200), assembled));
        }

        [Fact]
        public void AssembleExclusions_ToleratesNothingRecorded()
        {
            var assembled = AreaLifecycle.AssembleExclusions(null, null);

            Assert.True(assembled.IsEmpty);
        }

        // --------------------------------------------------------------------- the offer test

        /// <summary>R44: a `[cleared]` or `[empty]` area is not offered to a mining dock; every other status is.</summary>
        [Theory]
        [InlineData(AreaLifecycleStatus.Unsurveyed, true)]
        [InlineData(AreaLifecycleStatus.Surveyed, true)]
        [InlineData(AreaLifecycleStatus.Digging, true)]
        [InlineData(AreaLifecycleStatus.Mined, true)]
        [InlineData(AreaLifecycleStatus.Cleared, false)]
        [InlineData(AreaLifecycleStatus.Empty, false)]
        public void OfferToMiningDock_IsRefusedOnlyForClearedAndEmpty(AreaLifecycleStatus status, bool offerable)
        {
            Assert.Equal(offerable, AreaLifecycle.IsOfferableToMiningDock(status));
        }

        /// <summary>R44 and R3 read the same function, so the offer test covers every status the ladder can produce.</summary>
        [Fact]
        public void EveryStatusTheLadderCanProduce_HasAnOfferAnswer()
        {
            foreach (AreaLifecycleStatus status in Enum.GetValues(typeof(AreaLifecycleStatus)))
                AreaLifecycle.IsOfferableToMiningDock(status);   // total: no status throws
        }

        // ------------------------------------------------------- kind selects the status slot

        [Fact]
        public void AFarmingArea_NeverConsultsTheMiningLadder()
        {
            // Structural, not a precedence rule. The ladder is a delegate and is simply never
            // invoked for farmland -- there is no moment at which a mining status and [farm] both
            // exist and only the render order keeps them apart.
            var status = AreaLifecycle.StatusFor(
                AreaKind.Farming,
                () => throw new InvalidOperationException("the mining ladder must never run for farmland"));

            Assert.Equal(AreaLifecycleStatus.Farm, status);
        }

        [Fact]
        public void AMiningArea_ReadsWhateverItsLadderReturns()
        {
            foreach (var rung in AreaLifecycle.MiningRamp)
                Assert.Equal(rung, AreaLifecycle.StatusFor(AreaKind.Mining, () => rung));
        }

        [Fact]
        public void TheRampIsTheEnumMinusFarm_SoTheVocabularyNeedsNoHandWrittenList()
        {
            Assert.DoesNotContain(AreaLifecycleStatus.Farm, AreaLifecycle.MiningRamp);
            Assert.False(AreaLifecycle.IsMiningRung(AreaLifecycleStatus.Farm));
            Assert.Equal(
                Enum.GetValues(typeof(AreaLifecycleStatus)).Cast<AreaLifecycleStatus>().Count() - 1,
                AreaLifecycle.MiningRamp.Count);
        }

        [Fact]
        public void AMiningAreaWithNoLadder_IsACallerBug_ButFarmlandNeedingNoneIsNormal()
        {
            Assert.Throws<ArgumentNullException>(() => AreaLifecycle.StatusFor(AreaKind.Mining, null));
            Assert.Equal(AreaLifecycleStatus.Farm, AreaLifecycle.StatusFor(AreaKind.Farming, null));
        }

        [Fact]
        public void RepurposedGround_StillCarryingEveryMiningInput_ReadsFarmOnceItsKindIsFarming()
        {
            // The case a later unit creates, when kind lives on the survey area and an exhausted
            // mining area becomes farmland. Its mined stamps and bedrock observations are still
            // there and still truthful about its past -- so the ladder would happily answer, and
            // the kind is the only thing that stops it being asked.
            var plots = Plots(2).ToList();
            var exhausted = Ledger(bedrock: plots);

            Func<AreaLifecycleStatus> ladder =
                () => AreaLifecycle.DeriveStatus(plots, All(100), All(200), exhausted);

            Assert.Equal(AreaLifecycleStatus.Empty, AreaLifecycle.StatusFor(AreaKind.Mining, ladder));
            Assert.Equal(AreaLifecycleStatus.Farm, AreaLifecycle.StatusFor(AreaKind.Farming, ladder));
        }

        [Fact]
        public void TheLadderNeverProducesFarm_BecauseFarmIsNotARungOfIt()
        {
            var plots = Plots(2).ToList();

            foreach (var surveyed in new long[] { 0, 100, 300 })
            foreach (var mined in new long[] { 0, 200 })
            foreach (var bedrock in new[] { null, plots })
            {
                var status = AreaLifecycle.DeriveStatus(
                    plots, All(surveyed), All(mined), Ledger(bedrock: bedrock));

                Assert.NotEqual(AreaLifecycleStatus.Farm, status);
                Assert.Contains(status, AreaLifecycle.MiningRamp);
            }
        }

        // --------------------------------------------------- U11: kind on the area itself

        /// <summary>
        /// R30, KTD11. Kind defaults to mining, and the default is the CLR default rather than a
        /// value someone has to remember to write. That is what makes the upgrade silent: an
        /// existing save holds only mining areas and has no kind field at all, so the persisted
        /// ordinal loads as 0 and the area reads mining without anything being set.
        /// </summary>
        [Fact]
        public void NothingSet_IsMiningGround_WhichIsWhatEveryExistingSaveHolds()
        {
            Assert.Equal(AreaKind.Mining, default(AreaKind));
            Assert.Equal(AreaKind.Mining, (AreaKind)0);
        }

        /// <summary>
        /// The ordinals are the persisted wire format (the survey area stores kind as
        /// <c>(int)</c>, the way every other enum this mod persists is stored), so reordering the
        /// enum would silently repurpose saved areas. Pinned here rather than trusted.
        /// </summary>
        [Fact]
        public void TheKindOrdinals_ArePinned_BecauseTheyAreWhatIsPersisted()
        {
            Assert.Equal(0, (int)AreaKind.Mining);
            Assert.Equal(1, (int)AreaKind.Farming);

            // A kind added later must claim its own ordinal rather than displacing one of these.
            foreach (AreaKind kind in Enum.GetValues(typeof(AreaKind)))
                Assert.Equal(kind, (AreaKind)(int)kind);
        }

        /// <summary>
        /// <b>R46, structurally.</b> U11 is what makes the collision possible: R31 lets an
        /// exhausted mining area be repurposed as farmland, and the area keeps its mined stamps
        /// and bedrock observations. Every input the ladder reads is still sitting on the area and
        /// still true about its past, so the ladder would answer <c>[empty]</c> if asked.
        ///
        /// <para>
        /// The ladder here THROWS. A farming area that returns <c>[farm]</c> is therefore proof
        /// the ladder was never consulted -- not proof that its answer was outranked. Deriving a
        /// mining status and then overwriting it would pass a precedence test and fail this one.
        /// </para>
        /// </summary>
        [Fact]
        public void RepurposedGround_NeverConsultsTheLadder_EvenThoughEveryMiningInputSurvives()
        {
            var plots = Plots(2).ToList();
            var exhausted = Ledger(bedrock: plots);

            var consulted = 0;
            Func<AreaLifecycleStatus> ladder = () =>
            {
                consulted++;
                return AreaLifecycle.DeriveStatus(plots, All(100), All(200), exhausted);
            };

            Assert.Equal(AreaLifecycleStatus.Farm, AreaLifecycle.StatusFor(AreaKind.Farming, ladder));
            Assert.Equal(0, consulted);

            // And the inputs really did survive the repurposing: turned back to mining ground the
            // same area still reads [empty] off the same stamps and observations (R31, R32). The
            // farm status is not achieved by discarding what the mine recorded.
            Assert.Equal(AreaLifecycleStatus.Empty, AreaLifecycle.StatusFor(AreaKind.Mining, ladder));
            Assert.Equal(1, consulted);
        }

        /// <summary>
        /// The slot holds one value of one type, so "a mining status AND <c>[farm]</c>" is not a
        /// state the render can be handed -- for any kind, over the whole small input space.
        /// </summary>
        [Fact]
        public void TheStatusSlotIsSingleValued_ForEveryKindAndEveryLadderInput()
        {
            var plots = Plots(2).ToList();

            foreach (AreaKind kind in Enum.GetValues(typeof(AreaKind)))
            foreach (var surveyed in new long[] { 0, 100, 300 })
            foreach (var mined in new long[] { 0, 200 })
            foreach (var bedrock in new[] { null, plots })
            {
                var status = AreaLifecycle.StatusFor(
                    kind,
                    () => AreaLifecycle.DeriveStatus(plots, All(surveyed), All(mined), Ledger(bedrock: bedrock)));

                if (kind == AreaKind.Mining)
                {
                    Assert.Contains(status, AreaLifecycle.MiningRamp);
                    Assert.NotEqual(AreaLifecycleStatus.Farm, status);
                }
                else
                {
                    Assert.Equal(AreaLifecycleStatus.Farm, status);
                    Assert.DoesNotContain(status, AreaLifecycle.MiningRamp);
                }
            }
        }

        // ------------------------------------------------------------- guards and totality

        [Fact]
        public void NullArguments_AreRefused()
        {
            var plots = Plots(1).ToList();

            Assert.Throws<ArgumentNullException>(
                () => AreaLifecycle.DeriveStatus(null, All(0), All(0), Ledger()));
            Assert.Throws<ArgumentNullException>(
                () => AreaLifecycle.DeriveStatus(plots, null, All(0), Ledger()));
            Assert.Throws<ArgumentNullException>(
                () => AreaLifecycle.DeriveStatus(plots, All(0), null, Ledger()));

            // A null ledger is refused rather than read as "nothing excluded": that silent
            // reading is what turns a `[cleared]` area into `[empty]` (R26).
            Assert.Throws<ArgumentNullException>(
                () => AreaLifecycle.DeriveStatus(plots, All(0), All(0), null));
        }

        /// <summary>
        /// The ladder is total and single-valued: across the whole small state space every
        /// input returns exactly one declared status, the same one on every call, and the
        /// status returned satisfies its own defining test.
        /// </summary>
        [Fact]
        public void EveryInputCombination_ReturnsExactlyOneDeclaredStatus()
        {
            // The ladder's OWN vocabulary, not the whole enum: [farm] shares the status slot but
            // is not a rung, and the kind is what selects it (R30). A status added later counts
            // as a rung by default, so it lands here as unreachable and whoever added it has to
            // say which slot it belongs to -- which is the point of failing that way round.
            var declared = new HashSet<AreaLifecycleStatus>(AreaLifecycle.MiningRamp);
            var seen = new HashSet<AreaLifecycleStatus>();
            long[] surveyedValues = { 0, 100, 300 };
            long[] minedValues = { 0, 200 };

            foreach (var plotCount in new[] { 1, 2 })
            {
                var plots = Plots(plotCount).ToList();
                var states = Enumerate(plotCount, surveyedValues, minedValues).ToList();

                foreach (var state in states)
                {
                    var surveyed = Stamps(state.Surveyed);
                    var mined = Stamps(state.Mined);
                    var ledger = Ledger(
                        bedrock: plots.Where(p => state.Bedrock[p.X]),
                        attempts: plots.Where(p => state.Excluded[p.X]).Select(p => Refused(DockA, p)));

                    var status = AreaLifecycle.DeriveStatus(plots, surveyed, mined, ledger);
                    var again = AreaLifecycle.DeriveStatus(plots, surveyed, mined, ledger);

                    Assert.Contains(status, declared);
                    Assert.Equal(status, again);
                    seen.Add(status);

                    var anyUnsurveyed = plots.Any(p => surveyed(p) <= 0);
                    var allFinished = plots.All(p => state.Bedrock[p.X] || state.Excluded[p.X]);
                    var anyExcluded = plots.Any(p => state.Excluded[p.X]);

                    if (anyUnsurveyed)
                        Assert.Equal(AreaLifecycleStatus.Unsurveyed, status);
                    else if (allFinished)
                        Assert.Equal(
                            anyExcluded ? AreaLifecycleStatus.Cleared : AreaLifecycleStatus.Empty,
                            status);
                    else if (plots.All(p => PlotFreshness.IsMineable(surveyed(p), mined(p))))
                        Assert.Equal(AreaLifecycleStatus.Surveyed, status);
                    else if (plots.Any(p => PlotFreshness.IsMineable(surveyed(p), mined(p))))
                        Assert.Equal(AreaLifecycleStatus.Digging, status);
                    else
                        Assert.Equal(AreaLifecycleStatus.Mined, status);
                }
            }

            // Every status in the ramp is reachable from a constructed area state.
            Assert.Equal(declared, seen);
        }

        private sealed class AreaState
        {
            public long[] Surveyed;
            public long[] Mined;
            public bool[] Bedrock;
            public bool[] Excluded;
        }

        private sealed class PlotState
        {
            public long Surveyed;
            public long Mined;
            public bool Bedrock;
            public bool Excluded;
        }

        /// <summary>The full cross product of per-plot stamp, bedrock and exclusion states, walked as an odometer.</summary>
        private static IEnumerable<AreaState> Enumerate(int plotCount, long[] surveyedValues, long[] minedValues)
        {
            var perPlot = (from s in surveyedValues
                           from m in minedValues
                           from b in new[] { false, true }
                           from x in new[] { false, true }
                           select new PlotState { Surveyed = s, Mined = m, Bedrock = b, Excluded = x }).ToList();

            var digits = new int[plotCount];
            while (true)
            {
                var chosen = digits.Select(d => perPlot[d]).ToList();
                yield return new AreaState
                {
                    Surveyed = chosen.Select(p => p.Surveyed).ToArray(),
                    Mined = chosen.Select(p => p.Mined).ToArray(),
                    Bedrock = chosen.Select(p => p.Bedrock).ToArray(),
                    Excluded = chosen.Select(p => p.Excluded).ToArray(),
                };

                var i = plotCount - 1;
                for (; i >= 0; i--)
                {
                    digits[i]++;
                    if (digits[i] < perPlot.Count) break;
                    digits[i] = 0;
                }

                if (i < 0) yield break;
            }
        }

        // ================================================================
        // U8: reacting to ground the mod did not change (R16, R17, R43, AE16).
        // ================================================================

        private const int PlotSide = 8;
        private const int AreaOne = 1;
        private const int AreaTwo = 2;
        private const string Iron = "IronOre";
        private const string Gold = "GoldOre";

        /// <summary>A strip of plots (0,0)..(n-1,0), spanning world x 0..8n-1.</summary>
        private static IReadOnlyList<PlotCoord> Strip(int count) =>
            Enumerable.Range(0, count).Select(i => new PlotCoord(i, 0)).ToList();

        private static GroundWriteAttribution MiningDroneOn(string ownerDockId, int areaId) =>
            GroundWriteAttribution.ByDrone(ownerDockId, areaId, AreaKind.Mining);

        private static GroundWriteAttribution FarmingDroneOn(string ownerDockId, int areaId) =>
            GroundWriteAttribution.ByDrone(ownerDockId, areaId, AreaKind.Farming);

        // ---- which plots a changed column reaches ----

        /// <summary>
        /// Covers AE16, R16. One hand-dug block inside a large area maps to exactly one of its
        /// plots. The mapping is the ordinary world-column-to-plot fold, so a column on a plot
        /// boundary belongs to the plot that starts there.
        /// </summary>
        [Fact]
        public void OneChangedColumn_ReachesOnlyThePlotThatContainsIt()
        {
            var affected = GroundChange.AffectedPlots(Strip(4), new[] { (X: 9, Z: 3) }, PlotSide);

            Assert.Equal(new[] { new PlotCoord(1, 0) }, affected);
        }

        /// <summary>R16. A map-editor edit spanning three plots reaches exactly those three.</summary>
        [Fact]
        public void AnEditSpanningThreePlots_ReachesExactlyThoseThree()
        {
            var affected = GroundChange.AffectedPlots(
                Strip(6),
                new[] { (X: 1, Z: 0), (X: 9, Z: 1), (X: 10, Z: 2), (X: 20, Z: 0) },
                PlotSide);

            Assert.Equal(3, affected.Count);
            Assert.Contains(new PlotCoord(0, 0), affected);
            Assert.Contains(new PlotCoord(1, 0), affected);
            Assert.Contains(new PlotCoord(2, 0), affected);
        }

        /// <summary>R16. A change outside every plot of the area reaches nothing at all.</summary>
        [Fact]
        public void AChangeOutsideEveryPlot_ReachesNothing()
        {
            var affected = GroundChange.AffectedPlots(Strip(4), new[] { (X: 900, Z: 900) }, PlotSide);

            Assert.Empty(affected);
        }

        // ---- attributing the change before choosing the reset ----

        /// <summary>
        /// Covers AE1, AE2, R1. A write the mod cannot attribute to one of its own drones -- a
        /// player digging, an administrator command, a map-editor paste, a rebuild of the engine's
        /// block caches -- is ignored entirely.
        ///
        /// <para>
        /// This test previously asserted that such a write reset the plots it touched. That is the
        /// behaviour the narrowed requirement removes, so the expectation is inverted here rather
        /// than the test being deleted: the suite should show that the rule changed, not merely
        /// stop mentioning the old one.
        /// </para>
        /// </summary>
        [Fact]
        public void AnUnattributedWrite_IsIgnoredEntirely()
        {
            Assert.Equal(
                GroundChangeVerdict.IgnoredNotOurs,
                GroundChange.VerdictFor(GroundWriteAttribution.Outside, DockA, AreaOne, AreaKind.Mining));

            Assert.False(GroundChange.RequiresReaction(
                GroundChange.VerdictFor(GroundWriteAttribution.Outside, DockA, AreaOne, AreaKind.Mining)));
        }

        /// <summary>
        /// R17, R43. A mining drone digging its own dock's area records that area's own state --
        /// the mined stamps already say the survey is stale, and the findings stay so the player
        /// can still see what was taken.
        /// </summary>
        [Fact]
        public void AMiningDroneOnItsOwnArea_IsRecordedAsOwnWork_NotAReset()
        {
            Assert.Equal(
                GroundChangeVerdict.RecordedAsOwnWork,
                GroundChange.VerdictFor(MiningDroneOn(DockA, AreaOne), DockA, AreaOne, AreaKind.Mining));
        }

        /// <summary>
        /// R43's second half. A drone changing ground that belongs to a DIFFERENT dock's area of
        /// the same kind still falls to R16 there: whether a survey still holds is a question
        /// about the ground, not about who changed it.
        /// </summary>
        [Fact]
        public void AMiningDroneOnAnotherDocksMiningArea_StillResetsThatArea()
        {
            Assert.Equal(
                GroundChangeVerdict.ResetToUnsurveyed,
                GroundChange.VerdictFor(MiningDroneOn(DockA, AreaOne), DockB, AreaOne, AreaKind.Mining));
        }

        /// <summary>R43. Same dock, different area of the same kind: still that area's ground changing under it.</summary>
        [Fact]
        public void AMiningDroneOnADifferentAreaOfTheSameDock_StillResetsThatArea()
        {
            Assert.Equal(
                GroundChangeVerdict.ResetToUnsurveyed,
                GroundChange.VerdictFor(MiningDroneOn(DockA, AreaOne), DockA, AreaTwo, AreaKind.Mining));
        }

        /// <summary>
        /// R43. A write is attributed to the area whose KIND it serves. Ground two areas cover at
        /// once -- handed-over plots are exactly that -- records the state of the work actually
        /// being done on it, so a farming write does not unsurvey the mining area underneath.
        /// </summary>
        [Fact]
        public void AFarmingWriteOnFarmland_DoesNotUnsurveyTheMiningAreaCoveringTheSameGround()
        {
            Assert.Equal(
                GroundChangeVerdict.RecordedAsOwnWork,
                GroundChange.VerdictFor(FarmingDroneOn(DockA, AreaOne), DockA, AreaOne, AreaKind.Farming));

            Assert.Equal(
                GroundChangeVerdict.NotThisKindsWork,
                GroundChange.VerdictFor(FarmingDroneOn(DockA, AreaOne), DockA, AreaTwo, AreaKind.Mining));

            Assert.False(GroundChange.RequiresReset(
                GroundChange.VerdictFor(FarmingDroneOn(DockA, AreaOne), DockB, AreaTwo, AreaKind.Mining)));
        }

        /// <summary>Only one verdict asks for a reset, and the reset path keys off exactly that.</summary>
        [Fact]
        public void OnlyTheResetVerdict_RequiresAReset()
        {
            foreach (var verdict in Enum.GetValues(typeof(GroundChangeVerdict)).Cast<GroundChangeVerdict>())
                Assert.Equal(verdict == GroundChangeVerdict.ResetToUnsurveyed, GroundChange.RequiresReset(verdict));
        }

        // ---- the scope that marks the mod's own writes ----

        /// <summary>
        /// The engine's block-write event does not name its writer, so the mod marks its own
        /// writes as it makes them. Outside the scope there is no attribution, which is what makes
        /// every other writer in the world read as not ours.
        ///
        /// <para>
        /// R1. A write the mod cannot attribute to one of its own drones is ignored completely.
        /// The mod does not monitor the world, so it does not know what such a write did and does
        /// not pretend to. This assertion previously expected the verdict that resets plots; that
        /// expectation encoded the behaviour this requirement removes.
        /// </para>
        /// </summary>
        [Fact]
        public void OutsideAnyScope_TheCurrentAttribution_IsNotOursAndIsIgnored()
        {
            Assert.False(ModGroundWrite.Current.IsModsOwn);
            Assert.Equal(GroundChangeVerdict.IgnoredNotOurs,
                GroundChange.VerdictFor(ModGroundWrite.Current, DockA, AreaOne, AreaKind.Mining));
        }

        /// <summary>The scope attributes writes while it is open and releases the attribution when it closes.</summary>
        [Fact]
        public void AScope_AttributesWhileOpen_AndReleasesOnDispose()
        {
            using (ModGroundWrite.Attribute(MiningDroneOn(DockA, AreaOne)))
            {
                Assert.True(ModGroundWrite.Current.IsModsOwn);
                Assert.Equal(AreaOne, ModGroundWrite.Current.ServedAreaId);
                Assert.Equal(DockA, ModGroundWrite.Current.ServedAreaOwnerId);
                Assert.Equal(AreaKind.Mining, ModGroundWrite.Current.ServedKind);
            }

            Assert.False(ModGroundWrite.Current.IsModsOwn);
        }

        /// <summary>A nested scope restores the one it replaced rather than clearing the attribution outright.</summary>
        [Fact]
        public void ANestedScope_RestoresTheOneItReplaced()
        {
            using (ModGroundWrite.Attribute(MiningDroneOn(DockA, AreaOne)))
            {
                using (ModGroundWrite.Attribute(FarmingDroneOn(DockB, AreaTwo)))
                    Assert.Equal(AreaTwo, ModGroundWrite.Current.ServedAreaId);

                Assert.True(ModGroundWrite.Current.IsModsOwn);
                Assert.Equal(AreaOne, ModGroundWrite.Current.ServedAreaId);
                Assert.Equal(AreaKind.Mining, ModGroundWrite.Current.ServedKind);
            }

            Assert.False(ModGroundWrite.Current.IsModsOwn);
        }

        // ---- the sweep cursor a reset has to rewind ----

        /// <summary>
        /// U8 step 4's other half. The sweep cursor is a monotonic index into the raster-ordered
        /// plot list, so a plot reset BEHIND the cursor would never be revisited by the pass now
        /// running -- unsurveyed, and never re-read. The reset rewinds to the earliest plot it
        /// dropped.
        /// </summary>
        [Fact]
        public void AResetBehindTheCursor_RewindsTheSweepToTheEarliestPlotItDropped()
        {
            var order = SweepOrder.RasterOrder(Strip(6));

            var rewound = SweepOrder.RewindIndex(
                order, new[] { new PlotCoord(4, 0), new PlotCoord(1, 0) }, currentPlotIndex: 5);

            Assert.Equal(1, rewound);
        }

        /// <summary>A reset AHEAD of the cursor costs nothing: the pass has not got there yet.</summary>
        [Fact]
        public void AResetAheadOfTheCursor_LeavesTheSweepWhereItIs()
        {
            var order = SweepOrder.RasterOrder(Strip(6));

            Assert.Equal(2, SweepOrder.RewindIndex(order, new[] { new PlotCoord(4, 0) }, currentPlotIndex: 2));
        }

        /// <summary>A plot the area does not contain cannot move the cursor.</summary>
        [Fact]
        public void APlotOutsideTheArea_CannotMoveTheSweepCursor()
        {
            var order = SweepOrder.RasterOrder(Strip(6));

            Assert.Equal(3, SweepOrder.RewindIndex(order, new[] { new PlotCoord(40, 40) }, currentPlotIndex: 3));
        }

        /// <summary>Raster order is by Z then X -- the order the sweep itself visits plots in.</summary>
        [Fact]
        public void RasterOrder_IsByZThenX()
        {
            var order = SweepOrder.RasterOrder(new[]
            {
                new PlotCoord(1, 1), new PlotCoord(0, 1), new PlotCoord(1, 0), new PlotCoord(0, 0),
            });

            Assert.Equal(
                new[] { new PlotCoord(0, 0), new PlotCoord(1, 0), new PlotCoord(0, 1), new PlotCoord(1, 1) },
                order);
        }

        // ---- the whole reaction, end to end over the pure half ----

        /// <summary>
        /// Covers AE1. A player digs one block inside a large surveyed area, and nothing at all
        /// happens to that area. The mod does not monitor the world for changes it did not make,
        /// so it never learns of the dig here; a mining drone discovers the discrepancy later, at
        /// the work site.
        ///
        /// <para>
        /// This test previously asserted the opposite — that the dug plot returned to unsurveyed,
        /// that the area's coverage fell, and that the area then read as unsurveyed. That was the
        /// behaviour of the requirement this work narrows, and the assertions are inverted here
        /// deliberately rather than deleted, so the change of rule stays visible in the suite.
        /// </para>
        /// </summary>
        [Fact]
        public void AHandDugBlock_IsIgnoredEntirely_AndTheAreaIsUntouched()
        {
            var plots = Strip(4);
            var area = new SurveyArea(AreaOne, "big area", plots);
            var record = new SurveyRecord(PlotSide);
            var surveyed = new PlotStampAccumulator();

            for (var i = 0; i < 4; i++)
            {
                var x = i * PlotSide;
                record.RecordSurface(AreaOne, x, 0, 64);
                record.RecordSample(x, 64, 0, i == 1 ? Gold : Iron, 0, AreaOne);
                surveyed.Record(new PlotCoord(i, 0), 100);
            }
            Assert.Equal(1f, record.Coverage(area));
            Assert.Equal(AreaLifecycleStatus.Surveyed,
                AreaLifecycle.DeriveStatus(plots, surveyed.StampFor, _ => 0L, Ledger()));

            // One block, dug by hand, inside plot (1,0). Nothing marked it, so the mod cannot
            // attribute it to one of its own drones. The plot the block falls in is still
            // computed correctly -- that arithmetic is unchanged -- but the verdict for it is to
            // do nothing at all.
            var affected = GroundChange.AffectedPlots(plots, new[] { (X: 9, Z: 3) }, PlotSide);
            Assert.Single(affected);

            var verdict = GroundChange.VerdictFor(ModGroundWrite.Current, DockA, AreaOne, AreaKind.Mining);
            Assert.Equal(GroundChangeVerdict.IgnoredNotOurs, verdict);
            Assert.False(GroundChange.RequiresReaction(verdict));

            // Because nothing reacts, every observable fact about the area is exactly what it was
            // before the block was dug: full coverage, both ore types still recorded, and the
            // status still surveyed.
            Assert.Equal(1f, record.Coverage(area));
            Assert.Contains(record.Findings(AreaOne), f => f.OreType == Gold);
            Assert.Equal(3, record.Findings(AreaOne).Count(f => f.OreType == Iron));
            Assert.Equal(
                AreaLifecycleStatus.Surveyed,
                AreaLifecycle.DeriveStatus(plots, surveyed.StampFor, _ => 0L, Ledger()));
        }

        /// <summary>
        /// R17. The mod's own mining dig marks the area `[mined]` through the stamps alone and
        /// leaves its findings and its coverage intact -- the player can still see what was there
        /// before it was taken.
        /// </summary>
        [Fact]
        public void TheModsOwnDig_MarksTheAreaMined_AndLeavesItsFindingsAndCoverageIntact()
        {
            var plots = Strip(3);
            var area = new SurveyArea(AreaOne, "mining area", plots);
            var record = new SurveyRecord(PlotSide);
            var surveyed = new PlotStampAccumulator();
            var mined = new PlotStampAccumulator();

            for (var i = 0; i < 3; i++)
            {
                record.RecordSurface(AreaOne, i * PlotSide, 0, 64);
                record.RecordSample(i * PlotSide, 64, 0, Iron, 0, AreaOne);
                surveyed.Record(new PlotCoord(i, 0), 100);
            }

            // The drone digs, with its own write marked as it is made.
            using (ModGroundWrite.Attribute(MiningDroneOn(DockA, AreaOne)))
            {
                var verdict = GroundChange.VerdictFor(ModGroundWrite.Current, DockA, AreaOne, AreaKind.Mining);
                Assert.Equal(GroundChangeVerdict.RecordedAsOwnWork, verdict);
                Assert.False(GroundChange.RequiresReset(verdict));
            }

            // What the dig does record is the mined stamp -- R17's whole mechanism.
            foreach (var plot in plots) mined.Record(plot, 200);

            Assert.Equal(1f, record.Coverage(area));
            Assert.Equal(3, record.Findings(AreaOne).Count(f => f.OreType == Iron));
            Assert.Equal(
                AreaLifecycleStatus.Mined,
                AreaLifecycle.DeriveStatus(plots, surveyed.StampFor, mined.StampFor, Ledger()));
        }
    }
}
