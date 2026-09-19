using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Simulation;
using Eco.Shared.Math;
using Eco.Shared.Voxel;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    /// <summary>What one bounded chunk of levelling produced.</summary>
    public enum LevelPassOutcome
    {
        /// <summary>The toggle is off, so there is no pass to run.</summary>
        NotRequested,

        /// <summary>Work was done and there is more; dispatch again.</summary>
        Working,

        /// <summary>The area is level. The toggle is cleared and the area is marked flat.</summary>
        Complete,

        /// <summary>The pass cannot proceed, with a reason the tab reports.</summary>
        Blocked
    }

    /// <summary>One chunk's result and, when blocked, why.</summary>
    public readonly struct LevelPassResult
    {
        public LevelPassOutcome Outcome { get; }

        /// <summary>The stall to report, set only when <see cref="Outcome"/> is blocked.</summary>
        public FarmStallReason? Stall { get; }

        public string Detail { get; }

        private LevelPassResult(LevelPassOutcome outcome, FarmStallReason? stall, string detail)
        {
            this.Outcome = outcome;
            this.Stall = stall;
            this.Detail = detail;
        }

        public static LevelPassResult NotRequested() => new(LevelPassOutcome.NotRequested, null, null);
        public static LevelPassResult Working() => new(LevelPassOutcome.Working, null, null);
        public static LevelPassResult Complete() => new(LevelPassOutcome.Complete, null, null);

        public static LevelPassResult Blocked(FarmStallReason stall, string detail) =>
            new(LevelPassOutcome.Blocked, stall, detail);
    }

    /// <summary>
    /// The optional levelling job (U12, R17 through R21): brings an area's surface to one
    /// height so it can be farmed by hand or by tractor, not just by the drone that can
    /// handle irregular ground anyway.
    ///
    /// Bounded per dispatch (KTD12) and re-entrant. Each call re-samples the ground, rebuilds
    /// the plan against the pinned target, and does at most one chunk of work -- so an
    /// interrupted pass resumes against what it has actually dug rather than against what it
    /// expected to have dug, and other areas' block work runs in between.
    /// </summary>
    public sealed class LevelPassDriver
    {
        /// <summary>
        /// Blocks moved per dispatch (KTD12). A starting point to observe rather than a
        /// derived value: large enough that a hillside makes visible progress per trip,
        /// small enough that one area cannot monopolise a drone with several assignments.
        /// </summary>
        public const int BlocksPerDispatch = 24;

        private readonly DroneDockObject dock;
        private readonly FarmAreaEntry area;
        private readonly IWorldSampler sampler;
        private readonly MiningRemovalService removal;
        private readonly BlockPlacementService placement;
        private readonly Item miningArm;
        private readonly Item harvestArm;
        private readonly Inventory hold;
        private readonly LinkComponent link;

        public LevelPassDriver(
            DroneDockObject dock,
            FarmAreaEntry area,
            IWorldSampler sampler,
            MiningRemovalService removal,
            BlockPlacementService placement,
            Item miningArm,
            Item harvestArm,
            Inventory hold,
            LinkComponent link)
        {
            this.dock = dock;
            this.area = area;
            this.sampler = sampler;
            this.removal = removal;
            this.placement = placement;
            this.miningArm = miningArm;
            this.harvestArm = harvestArm;
            this.hold = hold;
            this.link = link;
        }

        /// <summary>Runs one bounded chunk of the pass.</summary>
        public LevelPassResult Tick()
        {
            var check = this.Check(out var columns);
            if (check.Outcome == LevelPassOutcome.Complete) return this.CompleteAsFlat();
            if (check.Outcome != LevelPassOutcome.Working) return check;

            var citizen = this.dock.StampedCitizen;

            if (!this.area.LevelPassStarted)
            {
                this.area.LevelTargetHeight = LevelPlan.Build(columns).TargetHeight;
                this.area.LevelPassStarted = true;
                this.area.LevelBankedSpoil = 0;
            }

            // Rebuilt against the ground as it is now, with the target pinned: what the plan
            // reports is therefore what is LEFT to do, not what the pass started with.
            var plan = LevelPlan.ForTarget(columns, this.area.LevelTargetHeight);

            if (plan.RemovalVolume > 0) return this.Remove(plan, citizen);

            // Reached only once removal is exhausted -- the plan itself refuses otherwise
            // (R20), so the ordering is not this driver's discipline to keep.
            var fillPhase = plan.BeginFill(removalBlocksCompleted: 0, bankedSpoil: this.area.LevelBankedSpoil);
            if (fillPhase.Fills.Count > 0) return this.Fill(fillPhase, citizen);

            return this.CompleteAsFlat();
        }

        /// <summary>
        /// R21: the area is level. The toggle clears itself, so the next dispatch does
        /// ordinary farming rather than re-levelling flat ground forever.
        /// </summary>
        private LevelPassResult CompleteAsFlat()
        {
            this.area.LastFlat = true;
            this.area.LevelFirst = false;
            this.area.ClearLevelPass();
            return LevelPassResult.Complete();
        }

        /// <summary>
        /// What the pass would do right now, read from the world without changing it:
        /// NotRequested, Blocked with its reason, Complete when the ground is already level,
        /// or Working when there is levelling to do. The strategy asks this when choosing
        /// where to go, so an area whose block has since cleared is offered again instead
        /// of staying skipped on a stall recorded under different conditions.
        /// </summary>
        public LevelPassResult Check() => this.Check(out _);

        private LevelPassResult Check(out List<SurfaceColumn> columns)
        {
            columns = null;
            if (!this.area.LevelFirst) return LevelPassResult.NotRequested();

            // Full access, re-checked here: the pass is destructive and runs for many
            // dispatches, so the citizen it acts as must still be allowed to act.
            if (!this.dock.FarmStampIsValid())
                return LevelPassResult.Blocked(
                    FarmStallReason.PropertyRefusal, "the stamped citizen no longer has access to this dock");

            columns = this.SampleColumns();
            if (columns.Count == 0)
                return LevelPassResult.Blocked(FarmStallReason.LevelPassBlocked, "the area covers no ground");

            // Flat ground has nothing to level, so it completes before any other check:
            // a plant on ground the pass would never touch is no reason to refuse (R21).
            // An unstarted pass is judged against the target it would pin; a started one
            // against the target it already pinned.
            var targetNow = this.area.LevelPassStarted
                ? this.area.LevelTargetHeight
                : LevelPlan.Build(columns).TargetHeight;
            if (LevelPlan.ForTarget(columns, targetNow).IsLevel)
                return LevelPassResult.Complete();

            // Only the columns the pass will change matter, and they are known before any
            // plant is looked at: a column already at the target is never dug or filled.
            // A plant on a changed column is cleared the way a machete clears it -- the
            // dig and place services destroy it with the engine's own harvest action and
            // no yield, so a law against it still applies. A tree is the exception: a
            // machete cannot clear one, so the pass stops and names it instead of felling
            // it. Re-checked every dispatch, since the columns are re-sampled anyway.
            if (this.AnyTreeStanding(columns.Where(c => c.SurfaceY != targetNow)))
                return LevelPassResult.Blocked(
                    FarmStallReason.LevelPassBlocked, "a tree stands on ground that must be levelled; fell it first");

            return LevelPassResult.Working();
        }

        private LevelPassResult Remove(LevelPlan plan, User citizen)
        {
            // The hold's own rule, unchanged from mining: a full hold sends the drone home
            // and the pass resumes after an unload. Spoil that reaches linked storage is
            // still this pass's material, which is what LevelBankedSpoil counts.
            var positions = plan.Removals
                .SelectMany(r => r.Positions)
                .Take(BlocksPerDispatch)
                .Select(p => (Position: p, Classification: BlockClassification.Excavatable))
                .ToList();

            var dirtBefore = this.DirtInHold();

            var result = this.removal.Remove(
                positions,
                citizen,
                this.miningArm,
                this.hold,
                new YieldTable(minableYield: 1, excavatableYield: 1),
                new EcoBlockClassifier());

            if (result.Outcome == RemovalOutcome.Refused)
                return LevelPassResult.Blocked(StallFor(result.RefusalStage), result.Message);

            // Banked spoil is DIRT, counted from what the hold actually gained -- not a
            // tally of blocks removed. A stone hillside yields stone, and counting those
            // as banked dirt made the fill phase believe it held material it never had,
            // then request no new dirt and stall the pass with no way out.
            this.area.LevelBankedSpoil += Math.Max(0, this.DirtInHold() - dirtBefore);
            return LevelPassResult.Working();
        }

        private LevelPassResult Fill(LevelFillPhase phase, User citizen)
        {
            var positions = phase.Fills
                .SelectMany(f => f.Positions)
                .Take(BlocksPerDispatch)
                .ToList();

            var result = this.placement.Place(
                positions,
                typeof(DirtBlock),
                typeof(DirtItem),
                citizen,
                this.harvestArm,
                this.SourceInventory(citizen));

            if (result.Outcome == PlacementOutcome.Refused)
                return LevelPassResult.Blocked(StallFor(result.RefusalStage), result.Message);

            // Spent from what the pass banked first, and only then from new dirt (R20). The
            // count is what tells the two apart on the next dispatch.
            this.area.LevelBankedSpoil = Math.Max(0, this.area.LevelBankedSpoil - positions.Count);
            return LevelPassResult.Working();
        }

        /// <summary>
        /// Where the fill draws its dirt: the dock's linked storage (R22), resolved through
        /// the stamped citizen so the pass can only spend from containers that citizen could
        /// reach anyway. Falls back to the hold, which is where spoil sits before an unload.
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

        /// <summary>
        /// Every column the area covers, with its current surface height (R19). Sampled
        /// here rather than read from the area, because the median a survey writes is
        /// zeroed when findings are cleared and a farm area is never surveyed at all.
        /// </summary>
        private List<SurfaceColumn> SampleColumns()
        {
            var columns = new List<SurfaceColumn>();
            var size = PlotUtil.PropertyPlotLength;

            foreach (var plot in this.area.ToArea().EnumeratePlots())
            {
                var baseX = plot.X * size;
                var baseZ = plot.Z * size;

                for (var dx = 0; dx < size; dx++)
                for (var dz = 0; dz < size; dz++)
                {
                    var x = baseX + dx;
                    var z = baseZ + dz;
                    columns.Add(new SurfaceColumn(x, z, (int)this.sampler.GroundHeightAt(x, z)));
                }
            }

            return columns;
        }

        /// <summary>How much dirt the drone is carrying, which is what the fill phase can spend.</summary>
        private int DirtInHold() =>
            this.hold.NonEmptyStacks
                .Where(stack => stack.Item?.Type == typeof(DirtItem))
                .Sum(stack => stack.Quantity);

        /// <summary>Whether anything is growing anywhere in the area (R18's entry check).</summary>
        private bool AnyTreeStanding(IEnumerable<SurfaceColumn> columns) =>
            columns.Any(c =>
                WrappedWorldPosition3i.TryCreate(new Vector3i(c.X, c.SurfaceY + 1, c.Z), out var above)
                && EcoSim.PlantSim.GetPlant(above)?.Species is Eco.Simulation.Types.TreeSpecies);

        /// <summary>
        /// The refusal stage as the reason the tab reports. Law and property stay distinct
        /// all the way through (R36) -- a settlement forbidding the dig is a different
        /// problem from the dock's owner lacking access, and they are fixed differently.
        /// </summary>
        private static FarmStallReason StallFor(RemovalRefusalStage stage) => stage switch
        {
            RemovalRefusalStage.SettlementLaw => FarmStallReason.LawRefusal,
            RemovalRefusalStage.Property => FarmStallReason.PropertyRefusal,

            // Anything else is the pass's own problem -- an occupied cell, a block that
            // changed, an empty dirt store -- and is reported in the pass's words rather
            // than dressed up as a supply stall it may not be.
            _ => FarmStallReason.LevelPassBlocked
        };
    }
}
