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
            var declared = new HashSet<AreaLifecycleStatus>(
                Enum.GetValues(typeof(AreaLifecycleStatus)).Cast<AreaLifecycleStatus>());
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
    }
}
