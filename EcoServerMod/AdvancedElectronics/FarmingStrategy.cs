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

        /// <summary>The peek's cached answer, so one tick's scan is paid for once.</summary>
        private (int AreaId, PlotCoord Plot)? peekedTarget;
        private bool peekValid;

        /// <summary>Set when a scan exhausted the sweep; consumed when a target from the restarted sweep is taken.</summary>
        private bool sweepRestartPending;

        /// <summary>The dock's wake token as it stood when the current scan began.</summary>
        private int tokenAtScanStart;

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

        /// <summary>
        /// Nothing to offer right now: the hold is full, or every area is waiting or
        /// blocked.
        ///
        /// Reads through the cached peek and never through the consuming path. The
        /// lifecycle asks this alongside <see cref="TryGetNextTarget"/> in the same tick,
        /// and a property that settled the farm's wake or restarted its sweep as a side
        /// effect of being read would park a farm nobody had actually offered work to --
        /// and pay for the whole per-column area scan twice over while doing it.
        /// </summary>
        public bool IsComplete => this.IsExhausted || this.holdFull || this.PeekTarget() == null;

        public bool TryGetNextTarget(out PlotCoord plot)
        {
            plot = default;

            // A full hold outranks offering more work: the drone goes home, unloads, and
            // resumes. Produce decays in the hold, so carrying it around is a real cost.
            if (this.holdFull) return false;

            // Re-checked on every dispatch, not once at assignment: access can be revoked
            // while the drone is out.
            if (!this.homeDock.FarmStampIsValid()) return false;

            var target = this.PeekTarget();
            if (target == null)
            {
                // The one place the farm settles: it was asked for work and had none.
                this.SettleUntilSomethingChanges();
                return false;
            }

            // The sweep restarts only when a target from the second pass is actually
            // taken, so nothing re-offers ground the drone never flew to.
            if (this.sweepRestartPending)
            {
                this.visitedThisSweep.Clear();
                this.sweepRestartPending = false;
            }

            this.currentAreaId = target.Value.AreaId;
            this.currentPlot = target.Value.Plot;
            this.visitedThisSweep.Add(target.Value);
            this.InvalidatePeek();

            plot = target.Value.Plot;
            return true;
        }

        public ParkedWorkOutcome TickParkedWork()
        {
            var area = this.homeDock.FarmArea(this.currentAreaId);
            if (area == null || this.currentPlot == null) return ParkedWorkOutcome.PlotFailed;

            // And again at the moment of work: a dispatch that cleared the check can still
            // arrive after the citizen's access is gone.
            if (!this.homeDock.FarmStampIsValid())
            {
                this.RecordStall(area, FarmStallReason.PropertyRefusal, null);
                return ParkedWorkOutcome.PlotFailed;
            }

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

            // The first per-block refusal's own words, kept so a plot refused block by
            // block can say why instead of falling through to "nothing to do here".
            string firstRefusal = null;

            foreach (var column in ColumnsIn(plot))
            {
                var outcome = this.Evaluate(area, column, ledger, stored);
                if (outcome.Action == FarmAction.LeaveAlone)
                {
                    if (outcome.WasRefusedForFitness)
                        this.RecordStall(area, FarmStallReason.UnfitGround, outcome.UnfitCondition);
                    continue;
                }

                // The height Evaluate actually decided on. Re-sampling here would let the
                // drone act on a different block than the one it judged.
                var ground = new BlockPos(column.X, outcome.SurfaceY, column.Z);
                var above = new BlockPos(column.X, outcome.SurfaceY + 1, column.Z);

                var performed = this.Perform(outcome.Action, area, ground, above, citizen);
                if (performed != null)
                {
                    // R15: a block the drone cannot work is SKIPPED, and does not fail the
                    // area around it. Only a refusal that would repeat everywhere is worth
                    // stopping for -- a law, a property boundary, an empty store.
                    if (!this.IsAreaWide(performed.Value, citizen))
                    {
                        firstRefusal ??= string.IsNullOrWhiteSpace(performed.Value.Detail)
                            ? $"{outcome.Action.ToString().ToLowerInvariant()} was refused with no reason given"
                            : performed.Value.Detail;
                        continue;
                    }

                    this.RecordStall(area, performed.Value.Stall, performed.Value.MaterialName);
                    return ParkedWorkOutcome.PlotFailed;
                }

                area.LastNextAction = (int)outcome.Action;
                area.LastStallReason = -1;

                this.InvalidatePeek();
                this.CheckHold();
                return this.holdFull ? ParkedWorkOutcome.PlotDone : ParkedWorkOutcome.StillWorking;
            }

            // Work was wanted and every attempt was refused. Passing one block over is
            // right; passing the whole plot over without a word is what left the tab
            // reading "nothing to do here" while the drone hovered over the ground.
            if (firstRefusal != null)
            {
                this.RecordStall(area, FarmStallReason.BlocksRefused, firstRefusal);
                return ParkedWorkOutcome.PlotFailed;
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
        /// by construction. A material shortfall is area-wide only when the material really
        /// is gone -- the store is asked directly, using the item TYPE the refused action
        /// needed rather than any reading of the engine's wording. Everything else is one
        /// awkward block among hundreds and is skipped.
        /// </summary>
        private bool IsAreaWide(PerformRefusal refusal, User citizen)
        {
            switch (refusal.Stall)
            {
                case FarmStallReason.LawRefusal:
                case FarmStallReason.PropertyRefusal:
                case FarmStallReason.PackRejected:
                    return true;

                case FarmStallReason.MissingMaterial:
                    return refusal.MaterialType != null && !this.SourceHolds(refusal.MaterialType, citizen);

                default:
                    return false;
            }
        }

        /// <summary>Whether linked storage still holds the item a refused action needed.</summary>
        private bool SourceHolds(Type materialType, User citizen) =>
            this.SourceInventory(citizen).NonEmptyStacks.Any(stack => stack.Item?.Type == materialType);

        /// <summary>
        /// One refused action: the reason to report, and -- when the action consumed a
        /// material -- the item type it needed and that item's name.
        ///
        /// The TYPE is what decides whether the refusal is area-wide, because only it can
        /// answer "is the store actually empty". The name is only ever displayed. An action
        /// that consumes nothing carries neither, and can therefore never be mistaken for a
        /// supply problem.
        /// </summary>
        private readonly struct PerformRefusal
        {
            public FarmStallReason Stall { get; }
            public Type MaterialType { get; }
            public string MaterialName { get; }

            /// <summary>The engine's own message for the refusal, when it gave one.</summary>
            public string Detail { get; }

            public PerformRefusal(FarmStallReason stall, Type materialType = null, string materialName = null, string detail = null)
            {
                this.Stall = stall;
                this.MaterialType = materialType;
                this.MaterialName = materialName;
                this.Detail = detail;
            }

            public PerformRefusal WithDetail(string detail) =>
                new PerformRefusal(this.Stall, this.MaterialType, this.MaterialName, detail);
        }

        /// <summary>Performs one action, returning null on success or the refusal to classify.</summary>
        /// <remarks>
        /// Every branch below writes ground, so the whole switch runs inside one attribution
        /// scope. The scope wraps the switch rather than each case so an action added later is
        /// covered without anyone remembering to wrap it.
        ///
        /// <para>
        /// What the scope is FOR changed when the ground-change listener was narrowed, and the
        /// reason recorded here before was the opposite of the current one. It used to stop a
        /// mining area covering the same plots from unsurveying itself as the farm worked, because
        /// an unmarked write was treated as an outside change and deleted survey results. An
        /// unmarked write is now ignored entirely, so forgetting the scope would delete nothing.
        /// </para>
        /// <para>
        /// The scope now exists so that a mining area covering the same ground IS told. Flattening
        /// ground a mining area covers changes that area's ground just as surely as a mining drone
        /// would have, so its plots are marked for re-reading and its findings are kept. Without
        /// the scope the farm's work is invisible to every area on the server, and a mining area
        /// underneath it goes on describing ground the farm has already levelled.
        /// </para>
        ///
        /// The farm's areas live on its own dock, so the dock that owns the served area and the
        /// dock running this strategy are the same object.
        /// </remarks>
        private PerformRefusal? Perform(
            FarmAction action, FarmAreaEntry area, BlockPos ground, BlockPos above, User citizen)
        {
            using var attribution = ModGroundWrite.Attribute(GroundWriteAttribution.ByDrone(
                this.homeDock.ObjectID.ToString(), area.Id, AreaKind.Farming));

            switch (action)
            {
                case FarmAction.PlaceDirt:
                {
                    var result = this.placement.Place(
                        new[] { above }, typeof(DirtBlock), typeof(DirtItem),
                        citizen, this.harvestArm, this.SourceInventory(citizen));
                    return result.Outcome == PlacementOutcome.Succeeded
                        ? null
                        : Refusal(result.RefusalStage, typeof(DirtItem), "dirt").WithDetail(result.Message);
                }

                case FarmAction.Plow:
                {
                    var result = this.farming.Plow(ground, citizen, this.harvestArm);
                    return result.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : Refusal(result.RefusalStage).WithDetail(result.Message);
                }

                case FarmAction.Sow:
                {
                    var seedType = CropCatalog.ByKey(area.Crop)?.SeedType;
                    var result = this.farming.Sow(ground, area.Crop, citizen, this.harvestArm, this.SourceInventory(citizen));
                    return result.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : Refusal(result.RefusalStage, seedType, $"{CropCatalog.DisplayNameFor(area.Crop)} seed").WithDetail(result.Message);
                }

                case FarmAction.Relay:
                {
                    // Dig the sand up, lay dirt back in the same cell, and plow it at once,
                    // before the biome turns it to sand again. One step failing reports
                    // that step's refusal; placing needs a dirt item, which the dig itself
                    // usually supplies, and otherwise comes from linked storage.
                    var dug = this.removal.Remove(
                        new[] { (ground, BlockClassification.Excavatable) },
                        citizen,
                        this.miningArm,
                        this.hold,
                        new YieldTable(minableYield: 1, excavatableYield: 1),
                        new EcoBlockClassifier());
                    if (dug.Outcome == RemovalOutcome.Refused)
                        return Refusal(dug.RefusalStage).WithDetail(dug.Message);

                    var laid = this.placement.Place(
                        new[] { ground }, typeof(DirtBlock), typeof(DirtItem),
                        citizen, this.harvestArm, this.SourceInventory(citizen));
                    if (laid.Outcome != PlacementOutcome.Succeeded)
                        return Refusal(laid.RefusalStage, typeof(DirtItem), "dirt").WithDetail(laid.Message);

                    var plowed = this.farming.Plow(ground, citizen, this.harvestArm);
                    return plowed.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : Refusal(plowed.RefusalStage).WithDetail(plowed.Message);
                }

                case FarmAction.Harvest:
                {
                    var result = this.farming.Harvest(above, citizen, this.harvestArm, this.hold);
                    return result.Outcome == FarmActionOutcome.Succeeded
                        ? null
                        : Refusal(result.RefusalStage).WithDetail(result.Message);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// The refusal stage as the reason the tab reports (R36).
        ///
        /// Law and property stay distinct all the way to the string, because a settlement
        /// forbidding the action and the dock's owner lacking access are fixed in different
        /// places. Everything else -- a pretest, an unrecognised refusal -- is reported as a
        /// material stall ONLY when the action actually consumed a material, and even then
        /// the store still has to be empty for it to stop the area. An action that consumes
        /// nothing gets Skipped, which is not area-wide and not shown as a stall: the block
        /// is simply passed over (R15).
        /// </summary>
        private static PerformRefusal Refusal(
            RemovalRefusalStage stage, Type materialType = null, string materialName = null) => stage switch
        {
            RemovalRefusalStage.SettlementLaw => new PerformRefusal(FarmStallReason.LawRefusal),
            RemovalRefusalStage.Property => new PerformRefusal(FarmStallReason.PropertyRefusal),
            // A tripped fail-closed invariant is not an awkward block: it means the pack
            // this mod built was malformed, and retrying it forever in silence is the one
            // outcome those guards exist to prevent.
            RemovalRefusalStage.Unrecognised => new PerformRefusal(FarmStallReason.PackRejected),

            _ => materialType == null
                ? new PerformRefusal(FarmStallReason.Skipped)
                : new PerformRefusal(FarmStallReason.MissingMaterial, materialType, materialName)
        };

        /// <summary>
        /// The action for one column: R14's rules over what the block currently is, with
        /// R32's fitness gate over the sow clause.
        /// </summary>
        private FarmPlotOutcome Evaluate(FarmAreaEntry area, (int X, int Z) column, CropCeilingLedger ledger, int stored) =>
            EvaluateColumn(this.sampler, this.fitness, area, column, ledger, stored);

        /// <summary>
        /// <see cref="Evaluate"/> with its two world readers passed in, so the /drone farm
        /// diagnostic runs the very decision the strategy runs rather than a copy of it.
        /// </summary>
        internal static FarmPlotOutcome EvaluateColumn(
            IWorldSampler sampler, IGroundFitness fitness,
            FarmAreaEntry area, (int X, int Z) column, CropCeilingLedger ledger, int stored)
        {
            var surfaceY = (int)sampler.GroundHeightAt(column.X, column.Z);

            var surface = EcoWorld.GetBlock(new Vector3i(column.X, surfaceY, column.Z));
            var acceptsPlow = surface != null && surface.Is<Tillable>();
            var tilled = surface != null && surface.Is<Tilled>();

            // Desert sand carries dirt's Tillable attribute but refuses the plow without a
            // word; dug up and laid back it is ordinary dirt until the biome turns it back.
            var mustBeRelaid = surface is DesertSandBlock;

            var plant = WrappedWorldPosition3i.TryCreate(new Vector3i(column.X, surfaceY + 1, column.Z), out var abovePos)
                ? EcoSim.PlantSim.GetPlant(abovePos)
                : null;

            var facts = plant == null
                ? FarmBlockFacts.Empty(acceptsPlow, tilled, mustBeRelaid)
                : FarmBlockFacts.Planted(
                    acceptsPlow,
                    tilled,
                    plant.Species.Name,
                    plant.Dead,
                    plant.Ripe,
                    ledger.MayHarvest(area.Crop, stored),
                    mustBeRelaid);

            return FarmPlotDecision.Decide(
                facts, area.Crop, fitness.Rate(area.Crop, column.X, surfaceY + 1, column.Z), surfaceY);
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
        /// <summary>
        /// The next target, computed at most once per tick and cached. Pure: it records no
        /// wake, restarts no sweep, and hands out no plot. The caller that actually
        /// consumes a target owns those effects.
        /// </summary>
        private (int AreaId, PlotCoord Plot)? PeekTarget()
        {
            // A cached NULL is only good while there is still no reason to look. Holding
            // one unconditionally latches the farm off for the life of the strategy: the
            // invalidation points are all things that happen when there IS work, so a farm
            // that settled once would never scan again, however much seed arrived.
            if (this.peekValid && !(this.peekedTarget == null && this.ShouldScan()))
                return this.peekedTarget;

            // Snapshot BEFORE scanning: a storage change that lands while the scan is
            // running would otherwise be recorded as already-seen, and the farm would
            // sleep through the very delivery it was waiting for.
            this.tokenAtScanStart = this.homeDock.FarmWakeToken;

            this.peekedTarget = this.ShouldScan() ? this.Scan() : null;
            this.peekValid = true;
            return this.peekedTarget;
        }

        /// <summary>Drops the cached peek after anything that could change what wants doing.</summary>
        private void InvalidatePeek()
        {
            this.peekValid = false;
            this.peekedTarget = null;
        }

        /// <summary>
        /// Whether anything has happened that could have given the farm work.
        ///
        /// The stamp is re-checked here rather than trusted from assignment time, which is
        /// what stops a citizen whose access was revoked from going on having the drone
        /// work in their name. Mining does the same through its own refusal check; farming
        /// only tested for a citizen existing, and being offline is not the same as being
        /// unauthorized.
        /// </summary>
        private bool ShouldScan() =>
            this.homeDock.FarmStampIsValid()
            && (this.homeDock.FarmWakeToken != this.lastScanToken
                || WorldTime.Seconds >= this.nextScanAtWorldSeconds);

        /// <summary>
        /// Records that this scan found nothing, and when to look again: the earliest time
        /// any area reported a crop coming due (R31). With nothing growing, only a storage
        /// change wakes the farm, so the next scheduled look is pushed out of the way rather
        /// than set to some arbitrary interval that would be a poll under another name.
        /// </summary>
        private void SettleUntilSomethingChanges()
        {
            // The token as it stood when the scan STARTED, not as it stands now -- see
            // PeekTarget. Recording the current value here would swallow any wake raised
            // during the scan.
            this.lastScanToken = this.tokenAtScanStart;

            double? earliest = null;
            foreach (var area in this.homeDock.AssignedFarmAreas)
            {
                if (area.LastStallReason != (int)FarmStallReason.WaitingOnGrowth) continue;
                if (area.LastDueAtWorldSeconds <= 0) continue;
                if (earliest == null || area.LastDueAtWorldSeconds < earliest) earliest = area.LastDueAtWorldSeconds;
            }

            // An ABSOLUTE due time, carried on the area. Re-anchoring a stored duration to
            // the current clock on every settle pushed the wake further out each time
            // anyone touched a chest, so a crop could stay ripe indefinitely.
            this.nextScanAtWorldSeconds = earliest ?? double.MaxValue;
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

                    // A level pass refused at its entry check is refused for the whole
                    // area, so offering its next plot just flies the drone out to be told
                    // the same thing 24 more times.
                    if (area.LevelFirst && area.LastStallReason == (int)FarmStallReason.LevelPassBlocked)
                        continue;

                    foreach (var plot in area.ToArea().EnumeratePlots())
                    {
                        if (this.visitedThisSweep.Contains((area.Id, plot))) continue;
                        if (!area.LevelFirst && !this.PlotNeedsWork(area, plot, ledgerCache, stored)) continue;

                        return (area.Id, plot);
                    }
                }

                // Every assigned plot has been offered once. A farm's work is continuous, so
                // the sweep starts again -- but only once, or this would spin. The restart
                // is recorded rather than performed, because this method must stay free of
                // side effects: it runs from a property read.
                if (sweep == 0 && this.visitedThisSweep.Count > 0)
                {
                    this.sweepRestartPending = true;
                    this.visitedThisSweep.Clear();
                }
                else
                {
                    break;
                }
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

            // The area's due time is the earliest across every plot of it, not whichever
            // plot happened to be walked last -- otherwise the farm sleeps past the crop
            // that was ready first.
            var dueAt = WorldTime.Seconds + (earliest.Value * TimeUtil.SecondsPerHour);

            var alreadyWaiting = area.LastStallReason == (int)FarmStallReason.WaitingOnGrowth
                && area.LastDueAtWorldSeconds > 0;

            area.LastStallReason = (int)FarmStallReason.WaitingOnGrowth;
            area.LastDueAtWorldSeconds = alreadyWaiting
                ? Math.Min(area.LastDueAtWorldSeconds, dueAt)
                : dueAt;
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
                case FarmStallReason.LevelPassBlocked:
                case FarmStallReason.BlocksRefused:
                    area.LastUnfitCondition = detail;
                    break;
            }
        }

        public void OnArrivalFailed()
        {
            this.InvalidatePeek();

            // The plot could not be reached. Nothing to record against it -- farming keeps
            // no per-plot ledger (R7) -- and it stays marked visited for this sweep, so the
            // drone tries the next one instead of circling the same unreachable ground.
            this.currentPlot = null;
        }

        public void OnArrivedHome()
        {
            var plan = CargoUnloader.TryUnload(this.hold, this.link, this.homeDock.StampedCitizen);
            this.holdFull = plan.Outcome != UnloadOutcome.Full;

            // An unload frees the hold and moves produce into the very storage the ceiling
            // is measured against, so what wants doing may have changed.
            this.InvalidatePeek();
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

        internal static IEnumerable<(int X, int Z)> ColumnsIn(PlotCoord plot)
        {
            var size = PlotUtil.PropertyPlotLength;
            var baseX = plot.X * size;
            var baseZ = plot.Z * size;

            for (var dx = 0; dx < size; dx++)
            for (var dz = 0; dz < size; dz++)
                yield return (baseX + dx, baseZ + dz);
        }

    }
}
