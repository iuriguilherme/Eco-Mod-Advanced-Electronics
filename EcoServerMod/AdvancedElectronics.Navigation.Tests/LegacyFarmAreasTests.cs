using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// The pre-0.5.0 -> 0.5.0 save fold for farm areas (U1, R17, R18, KTD3, KTD4).
    ///
    /// <para>
    /// Until this change a dock kept its farms in their own collection, numbered by their own
    /// counter, and a mine in another — so a farm and a mine on the same dock routinely carried
    /// the same id and neither could see the other's ground. Folding the farms into the one area
    /// collection is what lets the claim machinery that is already written stop being blind to
    /// them. This class pins the arithmetic of that fold, which is the one piece of the migration
    /// provable before a save is touched.
    /// </para>
    /// <para>
    /// Three properties carry the weight. Only the FARMS are renumbered (KTD3) — a survey id is
    /// referenced from persisted state elsewhere and moving one would strand those references,
    /// while a farm id is referenced by nothing. Every folded row states the farming kind
    /// OUTRIGHT (KTD4), because <see cref="AreaKind.Mining"/> is <c>0</c> and a row that lets the
    /// type default decide loads as a mine — the highest-consequence failure available here, since
    /// a neighbour's mining dock may then claim a farm's ground. And the fold is idempotent
    /// through the marker it writes: each folded area remembers the legacy id it consumed, so a
    /// second run recognises its own earlier work instead of minting fresh ids and duplicating
    /// every farm.
    /// </para>
    /// <para>
    /// What is NOT proved here is the save round-trip. The fold holds no Eco type on purpose, so
    /// it is testable at all; the serializer, the stand-in member and the call site are U3's and
    /// are proven in the live session.
    /// </para>
    /// </summary>
    public class LegacyFarmAreasTests
    {
        /// <summary>
        /// Every <c>[Serialized]</c> member of <c>FarmAreaEntry</c>, as
        /// <see cref="LegacyFarmRow"/> spells it. Enumerated from
        /// <c>EcoServerMod/AdvancedElectronics/DroneDock.Farming.cs</c> at the time of the fold;
        /// eighteen of them, not the seventeen the plan counted — <c>LevelBankedSpoil</c> is the
        /// one an eye slides over, and it is the material a half-finished level pass is holding.
        ///
        /// This list is the cross-assembly link the compiler cannot make: the test project cannot
        /// reference the Eco-side project, so nothing checks mechanically that the legacy type and
        /// this row still agree. Adding a member to <c>FarmAreaEntry</c> without adding it here
        /// drops it from every folded farm in silence, which is why the count and the names are
        /// asserted outright rather than inferred.
        /// </summary>
        private static readonly string[] LegacySerializedMembers =
        {
            nameof(LegacyFarmRow.Id),
            nameof(LegacyFarmRow.Name),
            nameof(LegacyFarmRow.PlotCoords),
            nameof(LegacyFarmRow.Epoch),
            nameof(LegacyFarmRow.Crop),
            nameof(LegacyFarmRow.LevelFirst),
            nameof(LegacyFarmRow.Assigned),
            nameof(LegacyFarmRow.LastStallReason),
            nameof(LegacyFarmRow.LastNextAction),
            nameof(LegacyFarmRow.LastNextDueHours),
            nameof(LegacyFarmRow.LastDueAtWorldSeconds),
            nameof(LegacyFarmRow.LastHeldPlotCount),
            nameof(LegacyFarmRow.LastUnfitCondition),
            nameof(LegacyFarmRow.LastMissingMaterial),
            nameof(LegacyFarmRow.LastFlat),
            nameof(LegacyFarmRow.LevelPassStarted),
            nameof(LegacyFarmRow.LevelTargetHeight),
            nameof(LegacyFarmRow.LevelBankedSpoil),
        };

        /// <summary>A plain legacy row with everything at its resting value but the id and plots.</summary>
        private static LegacyFarmRow Row(int id, params int[] plotCoords) => new LegacyFarmRow(
            id: id,
            name: "Farm " + id,
            plotCoords: plotCoords,
            epoch: 0,
            crop: null,
            levelFirst: false,
            assigned: false,
            lastStallReason: -1,
            lastNextAction: -1,
            lastNextDueHours: -1,
            lastDueAtWorldSeconds: 0,
            lastHeldPlotCount: 0,
            lastUnfitCondition: null,
            lastMissingMaterial: null,
            lastFlat: false,
            levelPassStarted: false,
            levelTargetHeight: 0,
            levelBankedSpoil: 0);

        /// <summary>
        /// A legacy row with every one of the eighteen members holding a value distinguishable
        /// from its type default, so that a member the fold forgets to carry shows up as a
        /// default rather than blending into a value that happens to match.
        /// </summary>
        private static LegacyFarmRow FullyPopulatedRow(int id) => new LegacyFarmRow(
            id: id,
            name: "Terraced Beans",
            plotCoords: new[] { 4, 5, 4, 6 },
            epoch: 7,
            crop: "Beans",
            levelFirst: true,
            assigned: true,
            lastStallReason: 3,
            lastNextAction: 2,
            lastNextDueHours: 12.5,
            lastDueAtWorldSeconds: 98765.25,
            lastHeldPlotCount: 1,
            lastUnfitCondition: "TooDry",
            lastMissingMaterial: "Bean Seed",
            lastFlat: true,
            levelPassStarted: true,
            levelTargetHeight: 64,
            levelBankedSpoil: 19);

        private static readonly int[] NothingFoldedYet = new int[0];

        // --- R18: the farms are renumbered and the survey areas are not (KTD3) ---

        [Fact]
        public void Three_legacy_farms_on_a_dock_with_no_survey_areas_take_ids_one_two_three()
        {
            var fold = LegacyFarmAreas.Fold(
                new[] { Row(1, 0, 0), Row(2, 1, 0), Row(3, 2, 0) }, NothingFoldedYet, nextAreaId: 1);

            Assert.Equal(new[] { 1, 2, 3 }, fold.Areas.Select(a => a.AreaId));
            Assert.Equal(4, fold.NextAreaId);
        }

        [Fact]
        public void Farms_are_numbered_after_the_survey_areas_and_no_survey_id_is_returned_at_all()
        {
            // KTD3: the dock already holds survey areas 1 and 2, so its nextAreaId is 3. The two
            // legacy farms also call themselves 1 and 2 — ids that collide by construction,
            // because the two counters were independent and both started at 1. The farms move;
            // the survey areas are not even in the result, which is the strongest form of "their
            // ids are untouched" a fold that returns only changes can offer.
            var fold = LegacyFarmAreas.Fold(
                new[] { Row(1, 0, 0), Row(2, 1, 0) }, NothingFoldedYet, nextAreaId: 3);

            Assert.Equal(new[] { 3, 4 }, fold.Areas.Select(a => a.AreaId));
            Assert.Equal(new[] { 1, 2 }, fold.Areas.Select(a => a.FoldedFromLegacyFarmId));
            Assert.Equal(5, fold.NextAreaId);
        }

        [Fact]
        public void A_legacy_id_above_nextAreaId_does_not_drag_nextAreaId_backwards_or_forwards()
        {
            // The legacy id is a value from a DIFFERENT counter. It is recorded as the marker and
            // it decides nothing about allocation: letting it seed nextAreaId would either strand
            // a range of ids or, worse, hand the farm an id a survey area already answers to.
            var fold = LegacyFarmAreas.Fold(new[] { Row(97, 0, 0) }, NothingFoldedYet, nextAreaId: 2);

            Assert.Equal(2, Assert.Single(fold.Areas).AreaId);
            Assert.Equal(97, fold.Areas[0].FoldedFromLegacyFarmId);
            Assert.Equal(3, fold.NextAreaId);
        }

        [Fact]
        public void A_nextAreaId_below_one_still_mints_from_one()
        {
            // Id 0 is the "no area" value every reference on the dock reads as absent, so it is
            // not an id the fold may hand out however the counter arrived at zero.
            var fold = LegacyFarmAreas.Fold(new[] { Row(1, 0, 0) }, NothingFoldedYet, nextAreaId: 0);

            Assert.Equal(1, Assert.Single(fold.Areas).AreaId);
            Assert.Equal(2, fold.NextAreaId);
        }

        // --- R18/KTD4: the kind is written, never defaulted ---

        [Fact]
        public void Every_folded_row_states_the_farming_kind_rather_than_letting_the_default_decide()
        {
            // The one that matters most. AreaKind.Mining is 0, so a folded farm whose kind is left
            // at the type default comes back after the update as a MINE: it runs the lifecycle
            // ladder, it is offered to mining docks, and a neighbour can claim the ground a
            // player's crop is standing on.
            var fold = LegacyFarmAreas.Fold(
                new[] { Row(1, 0, 0), Row(2, 1, 0) }, NothingFoldedYet, nextAreaId: 1);

            Assert.All(fold.Areas, area => Assert.Equal(AreaKind.Farming, area.Kind));
            Assert.All(fold.Areas, area => Assert.NotEqual(default(AreaKind), area.Kind));
        }

        // --- R18: nothing the legacy row recorded is dropped ---

        [Fact]
        public void An_assigned_legacy_row_folds_to_a_row_flagged_assigned()
        {
            // Assignment is what makes an area hold its plots. A folded farm that loses it stops
            // holding the ground it was working the moment the world loads.
            var fold = LegacyFarmAreas.Fold(
                new[] { FullyPopulatedRow(1), Row(2, 1, 0) }, NothingFoldedYet, nextAreaId: 1);

            Assert.True(fold.Areas[0].Assigned);
            Assert.False(fold.Areas[1].Assigned);
        }

        [Fact]
        public void Every_serialized_member_of_the_legacy_row_lands_on_the_folded_area()
        {
            var area = Assert.Single(
                LegacyFarmAreas.Fold(new[] { FullyPopulatedRow(6) }, NothingFoldedYet, nextAreaId: 1).Areas);

            // The id splits in two: the area takes a fresh id, and the legacy id is kept as the
            // marker rather than discarded, which is what the second run reads.
            Assert.Equal(1, area.AreaId);
            Assert.Equal(6, area.FoldedFromLegacyFarmId);

            Assert.Equal("Terraced Beans", area.Name);
            Assert.Equal(new[] { 4, 5, 4, 6 }, area.PlotCoords);
            Assert.Equal(7, area.Epoch);
            Assert.Equal("Beans", area.Crop);
            Assert.True(area.LevelFirst);
            Assert.True(area.Assigned);
            Assert.Equal(3, area.LastStallReason);
            Assert.Equal(2, area.LastNextAction);
            Assert.Equal(12.5, area.LastNextDueHours);
            Assert.Equal(98765.25, area.LastDueAtWorldSeconds);
            Assert.Equal(1, area.LastHeldPlotCount);
            Assert.Equal("TooDry", area.LastUnfitCondition);
            Assert.Equal("Bean Seed", area.LastMissingMaterial);
            Assert.True(area.LastFlat);
            Assert.True(area.LevelPassStarted);
            Assert.Equal(64, area.LevelTargetHeight);
            Assert.Equal(19, area.LevelBankedSpoil);
        }

        [Fact]
        public void LevelFirst_survives_on_its_own_because_it_decides_what_the_drone_does()
        {
            // Called out separately from the sweep above because it is not a readout: it is the
            // toggle FarmingComponent reads to decide whether the drone levels the ground before
            // it plants. Dropping it changes behaviour with nothing on screen to say so.
            var kept = Assert.Single(
                LegacyFarmAreas.Fold(
                    new[]
                    {
                        new LegacyFarmRow(
                            id: 1, name: "Hillside", plotCoords: new[] { 0, 0 }, epoch: 0, crop: "Corn",
                            levelFirst: true, assigned: false, lastStallReason: -1, lastNextAction: -1,
                            lastNextDueHours: -1, lastDueAtWorldSeconds: 0, lastHeldPlotCount: 0,
                            lastUnfitCondition: null, lastMissingMaterial: null, lastFlat: false,
                            levelPassStarted: false, levelTargetHeight: 0, levelBankedSpoil: 0),
                    },
                    NothingFoldedYet,
                    nextAreaId: 1).Areas);

            Assert.True(kept.LevelFirst);
        }

        [Fact]
        public void The_folded_area_declares_a_carrier_for_every_member_the_legacy_row_declares()
        {
            // Written so that adding a member to the legacy row and forgetting to carry it fails
            // HERE rather than being discovered as a lost setting after someone's world is
            // upgraded. Two halves: the legacy row still declares exactly the eighteen members
            // FarmAreaEntry serializes, and the folded area answers every one of them by name and
            // type — with Id the single deliberate exception, since it becomes the marker.
            var rowMembers = typeof(LegacyFarmRow)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(p => p.Name, p => p.PropertyType);

            Assert.Equal(
                LegacySerializedMembers.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                rowMembers.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal(18, rowMembers.Count);

            var areaMembers = typeof(FoldedFarmArea)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(p => p.Name, p => p.PropertyType);

            foreach (var member in rowMembers.Where(m => m.Key != nameof(LegacyFarmRow.Id)))
            {
                Assert.True(
                    areaMembers.ContainsKey(member.Key),
                    $"FoldedFarmArea carries no '{member.Key}', so folding a farm would drop it.");
                Assert.Equal(member.Value, areaMembers[member.Key]);
            }

            Assert.Equal(typeof(int), areaMembers[nameof(FoldedFarmArea.FoldedFromLegacyFarmId)]);
        }

        // --- R18: running it again is not a second fold ---

        [Fact]
        public void Re_running_against_the_original_rows_plus_the_first_runs_areas_changes_nothing()
        {
            // The real second load: the legacy rows are still on disk (U3 removes them one at a
            // time as they merge, and a run that died between two of them leaves the rest), and
            // the dock now also holds what the first run produced. What makes the difference
            // observable is the marker — the fold sees ids and nextAreaId, so without it a second
            // run cannot tell a folded farm from a farm still to fold, and duplicates every one.
            var legacy = new[] { Row(1, 0, 0), Row(2, 1, 0), Row(3, 2, 0) };

            var first = LegacyFarmAreas.Fold(legacy, NothingFoldedYet, nextAreaId: 1);
            var second = LegacyFarmAreas.Fold(
                legacy, first.Areas.Select(a => a.FoldedFromLegacyFarmId), first.NextAreaId);

            Assert.Empty(second.Areas);
            Assert.Equal(first.NextAreaId, second.NextAreaId);
        }

        [Fact]
        public void A_run_that_stopped_half_way_folds_only_the_rows_that_never_made_it()
        {
            // U3 removes each legacy row as it merges, so a fold interrupted part-way leaves a
            // dock holding both: areas for the rows that landed, and rows for those that did not.
            // Only the unmarked rows may move, and they take the ids after the ones already used.
            var legacy = new[] { Row(1, 0, 0), Row(2, 1, 0), Row(3, 2, 0) };

            var resumed = LegacyFarmAreas.Fold(legacy, new[] { 1, 2 }, nextAreaId: 3);

            var area = Assert.Single(resumed.Areas);
            Assert.Equal(3, area.FoldedFromLegacyFarmId);
            Assert.Equal(3, area.AreaId);
            Assert.Equal(4, resumed.NextAreaId);
        }

        [Fact]
        public void An_area_that_was_never_folded_from_a_farm_marks_nothing_as_done()
        {
            // A dock's survey areas carry the marker at its zero default, meaning "not folded from
            // anything". Reading that as "legacy farm 0 is already folded" would be harmless only
            // until a legacy counter ever produced a zero; treating it as no marker at all is what
            // keeps the two facts apart.
            var fold = LegacyFarmAreas.Fold(new[] { Row(1, 0, 0) }, new[] { 0, 0, 0 }, nextAreaId: 4);

            Assert.Equal(1, Assert.Single(fold.Areas).FoldedFromLegacyFarmId);
            Assert.Equal(4, fold.Areas[0].AreaId);
        }

        // --- Edge cases: what the fold refuses to decide ---

        [Fact]
        public void A_dock_with_no_legacy_rows_changes_nothing()
        {
            var fromEmpty = LegacyFarmAreas.Fold(new LegacyFarmRow[0], NothingFoldedYet, nextAreaId: 5);
            var fromNull = LegacyFarmAreas.Fold(null, NothingFoldedYet, nextAreaId: 5);

            Assert.Empty(fromEmpty.Areas);
            Assert.Empty(fromNull.Areas);
            Assert.Equal(5, fromEmpty.NextAreaId);
            Assert.Equal(5, fromNull.NextAreaId);
        }

        [Fact]
        public void A_null_marker_list_reads_as_nothing_folded_rather_than_throwing()
        {
            var fold = LegacyFarmAreas.Fold(new[] { Row(1, 0, 0) }, null, nextAreaId: 1);

            Assert.Equal(1, Assert.Single(fold.Areas).FoldedFromLegacyFarmId);
        }

        [Fact]
        public void A_legacy_row_with_no_plots_is_kept_for_the_caller_to_decide()
        {
            // The fold does not judge geometry. An area with no plots is a real state a player can
            // reach, and discarding it here would delete a named farm during an update with
            // nothing said about it. Whether to prune belongs to whoever can tell the player.
            var area = Assert.Single(
                LegacyFarmAreas.Fold(new[] { Row(4) }, NothingFoldedYet, nextAreaId: 1).Areas);

            Assert.Empty(area.PlotCoords);
            Assert.Equal(4, area.FoldedFromLegacyFarmId);
        }

        [Fact]
        public void An_odd_plot_list_is_carried_across_verbatim_rather_than_reshaped()
        {
            // Both sides flatten plots the same way, as consecutive (x, z) pairs, so a copy is
            // lossless and a trailing half-pair means exactly what it meant before: not a plot.
            // Trimming it here would be the fold inventing a geometry decision it is not making.
            var area = Assert.Single(
                LegacyFarmAreas.Fold(new[] { Row(1, 3, 4, 5) }, NothingFoldedYet, nextAreaId: 1).Areas);

            Assert.Equal(new[] { 3, 4, 5 }, area.PlotCoords);
        }

        [Fact]
        public void The_folded_plot_list_is_a_copy_so_a_later_edit_of_the_legacy_row_cannot_reach_it()
        {
            var plots = new List<int> { 0, 0 };
            var row = new LegacyFarmRow(
                id: 1, name: "Plot", plotCoords: plots, epoch: 0, crop: null, levelFirst: false,
                assigned: false, lastStallReason: -1, lastNextAction: -1, lastNextDueHours: -1,
                lastDueAtWorldSeconds: 0, lastHeldPlotCount: 0, lastUnfitCondition: null,
                lastMissingMaterial: null, lastFlat: false, levelPassStarted: false,
                levelTargetHeight: 0, levelBankedSpoil: 0);

            var area = Assert.Single(LegacyFarmAreas.Fold(new[] { row }, NothingFoldedYet, nextAreaId: 1).Areas);
            plots.Add(9);

            Assert.Equal(new[] { 0, 0 }, area.PlotCoords);
        }

        // --- Error paths: malformed input is ignored, never fatal ---

        [Fact]
        public void A_null_row_in_the_list_is_passed_over_rather_than_thrown_on()
        {
            // The sibling fold ignores a half-written record rather than throwing, and for the
            // same reason: this runs at world load, where an exception costs the load itself and
            // leaves no way for a player to reach the dock and repair it.
            var fold = LegacyFarmAreas.Fold(
                new[] { Row(1, 0, 0), null, Row(2, 1, 0) }, NothingFoldedYet, nextAreaId: 1);

            Assert.Equal(2, fold.Areas.Count);
            Assert.Equal(new[] { 1, 2 }, fold.Areas.Select(a => a.FoldedFromLegacyFarmId));
            Assert.Equal(3, fold.NextAreaId);
        }

        [Fact]
        public void A_row_with_a_null_plot_list_folds_to_an_empty_one()
        {
            var row = new LegacyFarmRow(
                id: 1, name: null, plotCoords: null, epoch: 0, crop: null, levelFirst: false,
                assigned: false, lastStallReason: -1, lastNextAction: -1, lastNextDueHours: -1,
                lastDueAtWorldSeconds: 0, lastHeldPlotCount: 0, lastUnfitCondition: null,
                lastMissingMaterial: null, lastFlat: false, levelPassStarted: false,
                levelTargetHeight: 0, levelBankedSpoil: 0);

            var area = Assert.Single(LegacyFarmAreas.Fold(new[] { row }, NothingFoldedYet, nextAreaId: 1).Areas);

            Assert.Empty(area.PlotCoords);
            Assert.Null(area.Name);
        }

        [Fact]
        public void A_row_whose_legacy_id_is_not_positive_still_folds_rather_than_being_dropped()
        {
            // The dock mints farm ids from 1, so this should not exist; if it does, losing the
            // player's farm is the worse answer. It folds, and its marker is honestly zero —
            // meaning U3's removal of the merged row, not the marker, is what stops it folding
            // twice. That is why U3 removes rows one at a time instead of trusting the marker.
            var fold = LegacyFarmAreas.Fold(new[] { Row(0, 1, 1) }, NothingFoldedYet, nextAreaId: 1);

            var area = Assert.Single(fold.Areas);
            Assert.Equal(1, area.AreaId);
            Assert.Equal(0, area.FoldedFromLegacyFarmId);
            Assert.Equal(AreaKind.Farming, area.Kind);
        }
    }
}
