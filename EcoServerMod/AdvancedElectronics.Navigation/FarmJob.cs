using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Every reason an assigned area does nothing right now (R35). One enumeration rather
    /// than a set of flags, because an area reports the one thing a citizen would act on.
    ///
    /// Two members are deliberately not stalls. <see cref="WaitingOnGrowth"/> is the
    /// system working (R29) and <see cref="CeilingReached"/> is the citizen's own
    /// instruction being obeyed (R27); neither is something to go and fix, and neither
    /// counts toward the idle-for-want-of-material condition.
    /// </summary>
    public enum FarmStallReason
    {
        /// <summary>No crop chosen for the area, so nothing is done to it at all (R26).</summary>
        NoCropSelected,

        /// <summary>Linked storage is short of the dirt or seed the next action needs (R28).</summary>
        MissingMaterial,

        /// <summary>The crop is at or above its ceiling, so its ripe plants stay standing (R27).</summary>
        CeilingReached,

        /// <summary>Everything left is growing. Not a stall (R29).</summary>
        WaitingOnGrowth,

        /// <summary>The engine refuses this crop on this ground (R32, R38).</summary>
        UnfitGround,

        /// <summary>A settlement law refused the action (R36).</summary>
        LawRefusal,

        /// <summary>Property authorization refused the action (R36).</summary>
        PropertyRefusal,

        /// <summary>Plots are held because another dock's area covers the same ground (R37).</summary>
        HeldByOverlap
    }

    /// <summary>Whether the job has work, is waiting, or has hit something a citizen must clear.</summary>
    public enum FarmJobStatus
    {
        /// <summary>The dock holds no farm areas.</summary>
        NoAreas,

        /// <summary>At least one area has an action to take now.</summary>
        Working,

        /// <summary>
        /// Nothing to do this moment, but something will change on its own or on a storage
        /// change. This is the ordinary resting state of a working farm, not a fault.
        /// </summary>
        Idle,

        /// <summary>Nothing to do and nothing coming due: every area needs a citizen.</summary>
        Blocked
    }

    /// <summary>
    /// One assigned area's current state: its crop, what it will do next, or the one
    /// reason it will do nothing.
    /// </summary>
    public sealed class FarmAreaState
    {
        public string AreaName { get; }

        /// <summary>The area's chosen crop, or null when none is selected (R24, R26).</summary>
        public string Crop { get; }

        /// <summary>The reason nothing happens here, or null when there is work to do.</summary>
        public FarmStallReason? Stall { get; }

        /// <summary>The action the drone takes next, set only when there is no stall.</summary>
        public FarmAction? NextAction { get; }

        /// <summary>
        /// Hours until this area's least-grown plant comes due (R31). Set only when the
        /// area is waiting on growth: a due time on a blocked area would wake the drone to
        /// find the same missing seed, which is exactly the poll R30 forbids.
        /// </summary>
        public double? NextDueHours { get; }

        /// <summary>Plots held by an overlap, set only for <see cref="FarmStallReason.HeldByOverlap"/>.</summary>
        public int HeldPlotCount { get; }

        /// <summary>The engine's own word for why the ground is unfit (R38), or null.</summary>
        public string UnfitCondition { get; }

        /// <summary>What linked storage is short of, or null.</summary>
        public string MissingMaterial { get; }

        private FarmAreaState(
            string areaName,
            string crop,
            FarmStallReason? stall,
            FarmAction? nextAction = null,
            double? nextDueHours = null,
            int heldPlotCount = 0,
            string unfitCondition = null,
            string missingMaterial = null)
        {
            if (string.IsNullOrEmpty(areaName))
                throw new ArgumentException("An area always has a name.", nameof(areaName));

            AreaName = areaName;
            Crop = crop;
            Stall = stall;
            NextAction = nextAction;
            NextDueHours = nextDueHours;
            HeldPlotCount = heldPlotCount;
            UnfitCondition = unfitCondition;
            MissingMaterial = missingMaterial;
        }

        /// <summary>Whether this area needs a citizen to do something (R28, R29).</summary>
        public bool IsBlocked =>
            Stall.HasValue
            && Stall != FarmStallReason.WaitingOnGrowth
            && Stall != FarmStallReason.CeilingReached;

        public bool IsWorkable => !Stall.HasValue;

        public static FarmAreaState Workable(string areaName, string crop, FarmAction nextAction) =>
            new FarmAreaState(areaName, RequireCrop(crop), stall: null, nextAction: nextAction);

        /// <summary>R26: an area with no crop is worked not at all, and says so.</summary>
        public static FarmAreaState AwaitingCrop(string areaName) =>
            new FarmAreaState(areaName, crop: null, stall: FarmStallReason.NoCropSelected);

        public static FarmAreaState ShortOfMaterial(string areaName, string crop, string material)
        {
            if (string.IsNullOrEmpty(material))
                throw new ArgumentException("A material stall names what is short.", nameof(material));

            return new FarmAreaState(
                areaName, RequireCrop(crop), FarmStallReason.MissingMaterial, missingMaterial: material);
        }

        public static FarmAreaState CeilingReached(string areaName, string crop) =>
            new FarmAreaState(areaName, RequireCrop(crop), FarmStallReason.CeilingReached);

        public static FarmAreaState WaitingOnGrowth(string areaName, string crop, double nextDueHours)
        {
            if (nextDueHours < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(nextDueHours), nextDueHours, "A crop does not come due in the past.");

            return new FarmAreaState(
                areaName, RequireCrop(crop), FarmStallReason.WaitingOnGrowth, nextDueHours: nextDueHours);
        }

        public static FarmAreaState UnfitGround(string areaName, string crop, string condition)
        {
            if (string.IsNullOrEmpty(condition))
                throw new ArgumentException("An unfit plot names the engine's own condition (R38).", nameof(condition));

            return new FarmAreaState(
                areaName, RequireCrop(crop), FarmStallReason.UnfitGround, unfitCondition: condition);
        }

        public static FarmAreaState RefusedByLaw(string areaName, string crop) =>
            new FarmAreaState(areaName, RequireCrop(crop), FarmStallReason.LawRefusal);

        public static FarmAreaState RefusedByProperty(string areaName, string crop) =>
            new FarmAreaState(areaName, RequireCrop(crop), FarmStallReason.PropertyRefusal);

        public static FarmAreaState HeldByOverlap(string areaName, string crop, int heldPlotCount)
        {
            if (heldPlotCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(heldPlotCount), heldPlotCount, "An overlap that holds no plots is not holding the area.");

            return new FarmAreaState(
                areaName, RequireCrop(crop), FarmStallReason.HeldByOverlap, heldPlotCount: heldPlotCount);
        }

        private static string RequireCrop(string crop)
        {
            if (string.IsNullOrEmpty(crop))
                throw new ArgumentException(
                    "Only an area awaiting a crop has none; every other state names one.", nameof(crop));

            return crop;
        }
    }

    /// <summary>
    /// What the Farm Drone's assignments add up to (R28, R29, R30, R31). Pure, so the
    /// idle rule and the wake time are provable without a server (KTD6).
    /// </summary>
    public sealed class FarmJob
    {
        public IReadOnlyList<FarmAreaState> Areas { get; }

        public FarmJob(IEnumerable<FarmAreaState> areas)
        {
            Areas = (areas ?? throw new ArgumentNullException(nameof(areas))).ToList();
        }

        /// <summary>
        /// The earliest moment any area comes due, or null when nothing is growing (R31).
        /// The drone schedules its return from this rather than sweeping its assignments.
        /// </summary>
        public double? WakeAtHours =>
            Areas.Any(a => a.NextDueHours.HasValue)
                ? Areas.Where(a => a.NextDueHours.HasValue).Min(a => a.NextDueHours.Value)
                : (double?)null;

        /// <summary>
        /// R29's condition: the drone has nothing to do AND at least one area needs a
        /// citizen. An area merely waiting on growth or sitting at its ceiling does not
        /// make the farm idle for want of material.
        /// </summary>
        public bool IdleForWantOfMaterial => !Areas.Any(a => a.IsWorkable) && Areas.Any(a => a.IsBlocked);

        public FarmJobStatus Status
        {
            get
            {
                if (Areas.Count == 0) return FarmJobStatus.NoAreas;
                if (Areas.Any(a => a.IsWorkable)) return FarmJobStatus.Working;

                // A wake outranks a blocked area: something will change on its own, so the
                // farm is resting rather than stuck, even while one area needs a citizen.
                if (WakeAtHours.HasValue) return FarmJobStatus.Idle;

                return Areas.Any(a => a.IsBlocked) ? FarmJobStatus.Blocked : FarmJobStatus.Idle;
            }
        }
    }
}
