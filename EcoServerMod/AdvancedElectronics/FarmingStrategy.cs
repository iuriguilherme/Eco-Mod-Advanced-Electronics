using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Shared.Math;
using Eco.Shared.Time;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;
using Eco.Simulation;
using Eco.Simulation.Agents;
using Eco.Simulation.Time;
using Eco.Simulation.WorldLayers.Pushers;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The Farm Drone's job strategy (U13): one tick's work at a parked plot is read each
    /// column, derive the action from what the block currently is (R13, R14), and perform
    /// it -- place dirt, plow, sow, or harvest -- until the plot has nothing left, then ask
    /// for the next.
    ///
    /// Unlike mining, a farm never finishes. Farmland has no terminal state to discover, so
    /// this strategy is never exhausted: it runs out of work for now, goes home, and comes
    /// back when growth comes due or storage changes (R30, R31). It also works several
    /// areas at once and rotates past a blocked one rather than retrying it (R28).
    ///
    /// Never unit-tested -- every collaborator is an Eco type. Its decisions live in U1
    /// (<see cref="FarmBlockDecision"/>), U2 (<see cref="CropCeilingLedger"/>) and U8
    /// (<see cref="FarmPlotDecision"/>), which are.
    /// </summary>
    public sealed class FarmingStrategy : IJobStrategy
    {
        private readonly DroneDockObject homeDock;
        private readonly IWorldSampler sampler;
        private readonly IGroundFitness fitness;
        private readonly FarmingActionService farming;
        private readonly BlockPlacementService placement;
        private readonly MiningRemovalService removal;
        private readonly Item harvestArm;
        private readonly Item miningArm;
        private readonly Inventory hold;
        private readonly LinkComponent link;
        private readonly int holdCapacity;

        /// <summary>The area and plot the lifecycle was last sent to, so parked work knows where it is.</summary>
        private int currentAreaId;
        private PlotCoord? currentPlot;

        /// <summary>
        /// Plots already handed out this sweep. A sweep is one pass over every assigned
        /// area; when it empties the strategy starts another, because a farm's work is
        /// continuous rather than a list that runs out.
        /// </summary>
        private readonly HashSet<(int AreaId, PlotCoord Plot)> visitedThisSweep = new();

        /// <summary>
        /// True from the tick the hold reaches capacity until it is fully unloaded (R23).
        /// Copied from the mining strategy unchanged, deliberately: the full-hold rule is
        /// not farming-specific and two versions of it would drift.
        /// </summary>
        private bool holdFull;

        /// <summary>The dock's wake token as of the last scan that found nothing (R30).</summary>
        private int lastScanToken = -1;

        /// <summary>When the earliest crop comes due, in world seconds. MaxValue means nothing is growing.</summary>
        private double nextScanAtWorldSeconds;

        public FarmingStrategy(
            DroneDockObject homeDock,
            IWorldSampler sampler,
            IGroundFitness fitness,
            FarmingActionService farming,
            BlockPlacementService placement,
            MiningRemovalService removal,
            Item harvestArm,
            Item miningArm,
            Inventory hold,
            LinkComponent link,
            int holdCapacity)
        {
            this.homeDock = homeDock;
            this.sampler = sampler;
            this.fitness = fitness;
            this.farming = farming;
            this.placement = placement;
            this.removal = removal;
            this.harvestArm = harvestArm;
            this.miningArm = miningArm;
            this.hold = hold;
            this.link = link;
            this.holdCapacity = holdCapacity;
        }

        /// <summary>
        /// A farm is never done. There is no fact about farmland for a drone to discover
        /// that would mean "nothing more, ever" -- an area with nothing to do right now
        /// comes due again on the crop's own clock. The lifecycle therefore keeps this
        /// strategy across idle trips instead of rebuilding it.
        /// </summary>
        public bool IsExhausted => !this.homeDock.AssignedFarmAreas.Any();

        /// <summary>Nothing to offer this tick: either the hold is full, or every area is waiting or blocked.</summary>
        public bool IsComplete => this.IsExhausted || this.holdFull || !this.HasAnyTarget();

        public bool TryGetNextTarget(out PlotCoord plot)
        {
            plot = default;

            // A full hold outranks offering more work: the drone goes home, unloads, and
            // resumes. Produce decays in the hold, so carrying it around is a real cost.
            if (this.holdFull) return false;

            if (this.homeDock.StampedCitizen == null) return false;

            var target = this.NextTarget();
            if (target == null) return false;

            this.currentAreaId = target.Value.AreaId;
            this.currentPlot = target.Value.Plot;
            this.visitedThisSweep.Add(target.Value);

            plot = target.Value.Plot;
            return true;
        }

        public ParkedWorkOutcome TickParkedWork()
        {
            var area = this.homeDock.FarmArea(this.currentAreaId);
            if (area == null || this.currentPlot == null) return ParkedWorkOutcome.PlotFailed;

            // The level pass, when requested, runs before any block work on this area
            // (R17). It is bounded per dispatch and clears its own toggle when it finishes.
            if (area.LevelFirst)
            {
                var level = this.LevelDriver(area).Tick();
                switch (level.Outcome)
                {
                    case LevelPassOutcome.Working:
                        this.CheckHold();
                        return this.holdFull ? ParkedWorkOutcome.PlotDone : ParkedWorkOutcome.StillWorking;

                    case LevelPassOutcome.Blocked:
                        this.RecordStall(area, level.Stall ?? FarmStallReason.MissingMaterial, level.Detail);
                        return ParkedWorkOutcome.PlotFailed;

                    // Complete or NotRequested both fall through to ordinary block work.
                }
            }

            if (string.IsNullOrEmpty(area.Crop))
            {
                this.RecordStall(area, FarmStallReason.NoCropSelected, null);
                return ParkedWorkOutcome.PlotFailed;
            }

            return this.WorkOneColumn(area, this.currentPlot.Value);
        }

        /// <summary>
        /// One action on one column. A column at a time rather than a whole plot, so the
        /// lifecycle keeps its grip: a plot is 25 columns, and doing them all in one tick
        /// would let a single plot hold the drone through a dispatch a citizen could not
        /// interrupt.
        /// </summary>
        private ParkedWorkOutcome WorkOneColumn(FarmAreaEntry area, PlotCoord plot)
        {
            var citizen = this.homeDock.StampedCitizen;
            var ledger = this.homeDock.ReadCropCeilings();
            var stored = this.homeDock.CountInLinkedStorage(area.Crop);

            foreach (var column in ColumnsIn(plot))
            {
                var outcome = this.Evaluate(area, column, ledger, stored);
                if (outcome.Action == FarmAction.LeaveAlone)
                {
                    if (outcome.WasRefusedForFitness)
                        this.RecordStall(area, FarmStallReason.UnfitGround, outcome.UnfitCondition);
                    continue;
                }

                var surfaceY = (int)this.sampler.GroundHeightAt(column.X, column.Z);
                var ground = new BlockPos(column.X, surfaceY, column.Z);
                var above = new BlockPos(column.X, surfaceY + 1, column.Z);

                var performed = this.Perform(outcome.Action, area, ground, above, citizen);
                if (performed != null)
                {
                    // R15: a block the drone cannot work is SKIPPED, and does not fail the
                    // area around it. Only a refusal that would repeat everywhere is worth
                    // stopping for -- a law, a property boundary, an empty store -- and the
                    // rest is one awkward block among hundreds.
                    if (!this.IsAreaWide(performed.Value, area, citizen)) continue;

                    this.RecordStall(area, performed.Value.Stall, performed.Value.Detail);
                    return ParkedWorkOutcome.PlotFailed;
                }

                area.LastNextAction = (int)outcome.Action;
                area.LastStallReason = -1;

                this.CheckHold();
                return this.holdFull ? ParkedWorkOutcome.PlotDone : ParkedWorkOutcome.StillWorking;
            }

            // Nothing in this plot wants doing. Whatever is growing here is the reason, and
            // R31 wants the least-grown plant's due time rather than a sweep.
            this.RecordGrowthWait(area, plot);
            return ParkedWorkOutcome.PlotDone;
        }

        /// <summary>
        /// Whether a refusal would repeat on every other block in the area, and so is worth
        /// stopping the area for rather than skipping one block over (R15, R34).
        ///
        /// Law and property answer for the whole settlement or plot, so they are area-wide
        /// by construction. A material shortfall is only area-wide if the material really is
        /// gone: the same pretest stage also refuses a cell that happens to be occupied or a
        /// block that changed underfoot, and reading those as an empty store would stop a
        /// whole field over one blocked square. So the store is asked directly rather than
        /// its wording being matched.
        /// </summary>
        private bool IsAreaWide((FarmStallReason Stall, string Detail) refusal, FarmAreaEntry area, User citizen)
        {
            switch (refusal.Stall)
            {
                case FarmStallReason.LawRefusal:
                case FarmStallReason.PropertyRefusal:
                    return true;

                case FarmStallReason.MissingMaterial:
                    return !this.SourceHolds(area, refusal.Detail, citizen);

                default:
                    return false;
            }
        }

        /// <summary>Whether linked storage still holds what the refused action needed.</summary>
        private bool SourceHolds(FarmAreaEntry area, string material, User citizen)
        {
            var wantedSeed = material != null && material.EndsWith("seed", StringComparison.OrdinalIgnoreCase);
            var type = wantedSeed
                ? CropCatalog.ByKey(area.Crop)?.SeedType
                : typeof(DirtItem);

            if (type == null) return false;

            return this.SourceInventory(citizen).NonEmptyStacks
                .Any(stack => stack.Item?.Type == type);
        }

        /// <summary>Performs one action, returning null on success or the stall to record on refusal.</summary>
        private (FarmStallReason Stall, string Detail)? Perform(
            FarmAction action, FarmAreaEntry area, BlockPos ground, BlockPos above, User citizen)
        {
            switch (action)
            {
                case FarmAction.PlaceDirt:
                {
                    var result = this.placement.Place(
                        new[] { above }, typeof(DirtBlock), typeof(DirtItem),
                        citizen, this.harvestArm, this.SourceInventory(citizen));
                    return result.Outcome == PlacementOutcome.Succeeded
                        ? null
                        : (StallFor(result.RefusalStage, "dirt"), result.Message);
                }

                case FarmAction.Plow:
                {
                    var result = this.farming.Plow(ground, citizen, this.harvestArm);
                    return result.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : (StallFor(result.RefusalStage, null), result.Message);
                }

                case FarmAction.Sow:
                {
                    var result = this.farming.Sow(ground, area.Crop, citizen, this.harvestArm, this.SourceInventory(citizen));
                    return result.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : (StallFor(result.RefusalStage, $"{CropCatalog.DisplayNameFor(area.Crop)} seed"), result.Message);
                }

                case FarmAction.Harvest:
                {
                    var result = this.farming.Harvest(above, citizen, this.harvestArm, this.hold);
                    return result.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : (StallFor(result.RefusalStage, null), result.Message);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// The action for one column: R14's rules over what the block currently is, with
        /// R32's fitness gate over the sow clause.
        /// </summary>
        private FarmPlotOutcome Evaluate(FarmAreaEntry area, (int X, int Z) column, CropCeilingLedger ledger, int stored)
        {
            var surfaceY = (int)this.sampler.GroundHeightAt(column.X, column.Z);

            var surface = EcoWorld.GetBlock(new Vector3i(column.X, surfaceY, column.Z));
            var acceptsPlow = surface != null && surface.Is<Tillable>();
            var tilled = surface != null && surface.Is<Tilled>();

            var plant = WrappedWorldPosition3i.TryCreate(new Vector3i(column.X, surfaceY + 1, column.Z), out var abovePos)
                ? EcoSim.PlantSim.GetPlant(abovePos)
                : null;

            var facts = plant == null
                ? FarmBlockFacts.Empty(acceptsPlow, tilled)
                : FarmBlockFacts.Planted(
                    acceptsPlow,
                    tilled,
                    plant.Species.Name,
                    plant.Dead,
                    plant.Ripe,
                    ledger.MayHarvest(area.Crop, stored));

            return FarmPlotDecision.Decide(facts, area.Crop, this.fitness.Rate(area.Crop, column.X, surfaceY + 1, column.Z));
        }

        /// <summary>
        /// Whether any column in this plot wants an action, checked before the drone flies
        /// there. Reading the world costs nothing compared to a flight, and a farm that
        /// visited every plot every sweep would be the poll R30 forbids wearing a
        /// different name.
        /// </summary>
        private bool PlotNeedsWork(FarmAreaEntry area, PlotCoord plot, CropCeilingLedger ledger, int stored) =>
            ColumnsIn(plot).Any(c => this.Evaluate(area, c, ledger, stored).Action != FarmAction.LeaveAlone);

        /// <summary>
        /// The next (area, plot) to work, rotating past areas that cannot be worked (R28).
        /// A blocked area is skipped rather than retried: the drone moves to one that can
        /// be worked, and the tab says why the first is stopped.
        ///
        /// Gated on there being a reason to look (R30). A farm that found nothing to do does
        /// not look again until linked storage changes or its earliest crop comes due --
        /// re-reading every column of every area each tick to rediscover the same answer is
        /// the poll the requirement forbids, however it is spelled.
        /// </summary>
        private (int AreaId, PlotCoord Plot)? NextTarget()
        {
            if (!this.ShouldScan()) return null;

            var found = this.Scan();
            if (found != null) return found;

            this.SettleUntilSomethingChanges();
            return null;
        }

        /// <summary>Whether anything has happened that could have given the farm work.</summary>
        private bool ShouldScan() =>
            this.homeDock.FarmWakeToken != this.lastScanToken
            || WorldTime.Seconds >= this.nextScanAtWorldSeconds;

        /// <summary>
        /// Records that this scan found nothing, and when to look again: the earliest time
        /// any area reported a crop coming due (R31). With nothing growing, only a storage
        /// change wakes the farm, so the next scheduled look is pushed out of the way rather
        /// than set to some arbitrary interval that would be a poll under another name.
        /// </summary>
        private void SettleUntilSomethingChanges()
        {
            this.lastScanToken = this.homeDock.FarmWakeToken;

            double? earliest = null;
            foreach (var area in this.homeDock.AssignedFarmAreas)
            {
                if (area.LastStallReason != (int)FarmStallReason.WaitingOnGrowth) continue;
                if (area.LastNextDueHours < 0) continue;
                if (earliest == null || area.LastNextDueHours < earliest) earliest = area.LastNextDueHours;
            }

            this.nextScanAtWorldSeconds = earliest == null
                ? double.MaxValue
                : WorldTime.Seconds + (earliest.Value * TimeUtil.SecondsPerHour);
        }

        private (int AreaId, PlotCoord Plot)? Scan()
        {
            for (var sweep = 0; sweep < 2; sweep++)
            {
                var ledgerCache = this.homeDock.ReadCropCeilings();

                foreach (var area in this.homeDock.AssignedFarmAreas.ToList())
                {
                    if (string.IsNullOrEmpty(area.Crop) && !area.LevelFirst)
                    {
                        this.RecordStall(area, FarmStallReason.NoCropSelected, null);
                        continue;
                    }

                    // A levelling area is worked wherever it is uneven, and the ceiling has
                    // nothing to say about moving dirt.
                    var stored = string.IsNullOrEmpty(area.Crop) ? 0 : this.homeDock.CountInLinkedStorage(area.Crop);

                    if (!area.LevelFirst && !string.IsNullOrEmpty(area.Crop)
                        && !ledgerCache.MayHarvest(area.Crop, stored)
                        && !this.AnythingButHarvestToDo(area, ledgerCache, stored))
                    {
                        this.RecordStall(area, FarmStallReason.CeilingReached, null);
                        continue;
                    }

                    foreach (var plot in area.ToArea().EnumeratePlots())
                    {
                        if (this.visitedThisSweep.Contains((area.Id, plot))) continue;
                        if (!area.LevelFirst && !this.PlotNeedsWork(area, plot, ledgerCache, stored)) continue;

                        return (area.Id, plot);
                    }
                }

                // Every assigned plot has been offered once. A farm's work is continuous, so
                // the sweep starts again -- but only once, and only if the second pass finds
                // something, or this would spin.
                if (sweep == 0 && this.visitedThisSweep.Count > 0) this.visitedThisSweep.Clear();
                else break;
            }

            return null;
        }

        /// <summary>
        /// Whether a ceiling-reached area still has non-harvest work. A full store stops
        /// the harvest, not the farm: the ground still wants plowing and sowing, and
        /// stopping the whole area would leave it fallow until someone ate the surplus.
        /// </summary>
        private bool AnythingButHarvestToDo(FarmAreaEntry area, CropCeilingLedger ledger, int stored) =>
            area.ToArea().EnumeratePlots()
                .SelectMany(ColumnsIn)
                .Select(c => this.Evaluate(area, c, ledger, stored).Action)
                .Any(a => a != FarmAction.LeaveAlone && a != FarmAction.Harvest);

        /// <summary>
        /// Records that this area is waiting on growth, with the least-grown plant's
        /// remaining time (R31). That time is what the drone schedules its return from,
        /// rather than sweeping its assignments.
        /// </summary>
        private void RecordGrowthWait(FarmAreaEntry area, PlotCoord plot)
        {
            double? earliest = null;

            foreach (var column in ColumnsIn(plot))
            {
                var surfaceY = (int)this.sampler.GroundHeightAt(column.X, column.Z);
                if (!WrappedWorldPosition3i.TryCreate(new Vector3i(column.X, surfaceY + 1, column.Z), out var abovePos))
                    continue;

                var plant = EcoSim.PlantSim.GetPlant(abovePos);
                if (plant == null || plant.Dead || plant.Ripe) continue;

                var remaining = RemainingGrowthHours(plant);
                if (earliest == null || remaining < earliest) earliest = remaining;
            }

            if (earliest == null) return;

            area.LastStallReason = (int)FarmStallReason.WaitingOnGrowth;
            area.LastNextDueHours = earliest.Value;
            area.LastNextAction = -1;
        }

        /// <summary>
        /// Hours until this plant is ripe. There is no ripeness event to subscribe to, so
        /// the wake has to be timed rather than awaited (R31).
        ///
        /// The engine keeps its own estimate on the plant, recomputed as it ticks under
        /// whatever conditions it is actually growing in -- that is the number worth
        /// waking on. It reads zero on a plant that has not been ticked for growth yet, and
        /// the fallback is the engine's own: the perfect-conditions time, which is what the
        /// plant's tooltip shows in the same case. Optimistic on purpose, since waking early
        /// costs a wasted trip while waking late costs a crop standing ripe for hours.
        /// </summary>
        private static double RemainingGrowthHours(Plant plant)
        {
            if (plant.TimeTillMatureHours > 0) return plant.TimeTillMatureHours;

            return TimeUtil.DaysToHours(
                (1 - plant.GrowthPercent) * plant.Species.MaturityAgeDays / PlantGrower.GrowthRateModifier);
        }

        private void RecordStall(FarmAreaEntry area, FarmStallReason stall, string detail)
        {
            area.LastStallReason = (int)stall;
            area.LastNextAction = -1;

            switch (stall)
            {
                case FarmStallReason.MissingMaterial:
                    area.LastMissingMaterial = detail;
                    break;
                case FarmStallReason.UnfitGround:
                    area.LastUnfitCondition = detail;
                    break;
            }
        }

        public void OnArrivalFailed()
        {
            // The plot could not be reached. Nothing to record against it -- farming keeps
            // no per-plot ledger (R7) -- and it stays marked visited for this sweep, so the
            // drone tries the next one instead of circling the same unreachable ground.
            this.currentPlot = null;
        }

        public void OnArrivedHome()
        {
            var plan = CargoUnloader.TryUnload(this.hold, this.link, this.homeDock.StampedCitizen);
            this.holdFull = plan.Outcome != UnloadOutcome.Full;
        }

        public void OnEnded(string reason)
        {
            // No ledger of its own to close. The per-area reports stay as they were, which
            // is what the tab keeps showing after the drone stops.
        }

        /// <summary>Sends the drone home once the hold is full (R23), the mining rule unchanged.</summary>
        private void CheckHold()
        {
            var quantity = this.hold.NonEmptyStacks.Sum(s => s.Quantity);
            if (!HoldLedger.HasRoomFor(quantity, this.holdCapacity, 1))
                this.holdFull = true;
        }

        private bool HasAnyTarget()
        {
            if (this.homeDock.StampedCitizen == null) return false;
            return this.NextTarget() != null;
        }

        private LevelPassDriver LevelDriver(FarmAreaEntry area) => new LevelPassDriver(
            this.homeDock, area, this.sampler, this.removal, this.placement,
            this.miningArm, this.harvestArm, this.hold, this.link);

        /// <summary>
        /// Where materials are drawn from: linked storage (R22), resolved through the
        /// stamped citizen so the drone can only spend from containers that citizen could
        /// reach anyway. The hold leads the collection so seed the drone is already
        /// carrying is used before more is drawn.
        /// </summary>
        private Inventory SourceInventory(User citizen)
        {
            var linked = this.link?.GetSortedLinkedEnabledStorages(citizen)
                .Where(storage => storage.Parent is not DroneDockObject)
                .Select(storage => storage.Inventory)
                .ToList();

            if (linked == null || linked.Count == 0) return this.hold;

            return new InventoryCollection(new[] { this.hold }.Concat(linked));
        }

        private static IEnumerable<(int X, int Z)> ColumnsIn(PlotCoord plot)
        {
            var size = PlotUtil.PropertyPlotLength;
            var baseX = plot.X * size;
            var baseZ = plot.Z * size;

            for (var dx = 0; dx < size; dx++)
            for (var dz = 0; dz < size; dz++)
                yield return (baseX + dx, baseZ + dz);
        }

        /// <summary>
        /// The refusal stage as the reason the tab reports (R36). Law and property stay
        /// distinct all the way to the string, because a settlement forbidding the action
        /// and the dock's owner lacking access are fixed in different places.
        /// </summary>
        private static FarmStallReason StallFor(RemovalRefusalStage stage, string material) => stage switch
        {
            RemovalRefusalStage.SettlementLaw => FarmStallReason.LawRefusal,
            RemovalRefusalStage.Property => FarmStallReason.PropertyRefusal,
            _ => material == null ? FarmStallReason.PropertyRefusal : FarmStallReason.MissingMaterial
        };
    }
}
