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
            if (!this.area.LevelFirst) return LevelPassResult.NotRequested();

            var citizen = this.dock.StampedCitizen;
            if (citizen == null)
                return LevelPassResult.Blocked(FarmStallReason.PropertyRefusal, "no stamped citizen");

            var columns = this.SampleColumns();
            if (columns.Count == 0)
                return LevelPassResult.Blocked(FarmStallReason.MissingMaterial, "the area covers no ground");

            if (!this.area.LevelPassStarted)
            {
                // R18: the entry check, once per pass rather than per block. Levelling an
                // area with a crop standing in it would bury the crop to flatten the ground
                // it is growing in, which is never what a citizen meant by "level this".
                if (this.AnyPlantStanding(columns))
                    return LevelPassResult.Blocked(
                        FarmStallReason.MissingMaterial, "the level pass will not start with plants standing here");

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

            // R21: the area is level. The toggle clears itself, so the next dispatch does
            // ordinary farming rather than re-levelling flat ground forever.
            this.area.LastFlat = true;
            this.area.LevelFirst = false;
            this.area.ClearLevelPass();
            return LevelPassResult.Complete();
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

            var result = this.removal.Remove(
                positions,
                citizen,
                this.miningArm,
                this.hold,
                new YieldTable(minableYield: 1, excavatableYield: 1),
                new EcoBlockClassifier());

            if (result.Outcome == RemovalOutcome.Refused)
                return LevelPassResult.Blocked(StallFor(result.RefusalStage), result.Message);

            this.area.LevelBankedSpoil += positions.Count;
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
            this.area.LevelBankedSpoil = System.Math.Max(0, this.area.LevelBankedSpoil - positions.Count);
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

        /// <summary>Whether anything is growing anywhere in the area (R18's entry check).</summary>
        private bool AnyPlantStanding(IEnumerable<SurfaceColumn> columns) =>
            columns.Any(c =>
                WrappedWorldPosition3i.TryCreate(new Vector3i(c.X, c.SurfaceY + 1, c.Z), out var above)
                && EcoSim.PlantSim.GetPlant(above) != null);

        /// <summary>
        /// The refusal stage as the reason the tab reports. Law and property stay distinct
        /// all the way through (R36) -- a settlement forbidding the dig is a different
        /// problem from the dock's owner lacking access, and they are fixed differently.
        /// </summary>
        private static FarmStallReason StallFor(RemovalRefusalStage stage) => stage switch
        {
            RemovalRefusalStage.SettlementLaw => FarmStallReason.LawRefusal,
            RemovalRefusalStage.Property => FarmStallReason.PropertyRefusal,
            _ => FarmStallReason.MissingMaterial
        };
    }
}
