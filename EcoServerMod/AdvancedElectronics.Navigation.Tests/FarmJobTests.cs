using System;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// R28's blocked areas, R29's exclusion of waiting-on-growth, and R31's wake time.
    /// </summary>
    public class FarmJobTests
    {
        private const string North = "north field";
        private const string South = "south field";
        private const string Corn = "corn";
        private const string Wheat = "wheat";

        [Fact]
        public void OneAreaShortOfSeedAndOneGrowing_ReportsEachOnItsOwnTermsAndIdlesWithAWake()
        {
            // AE7. The blocked area does not make the job blocked overall: the growing area
            // still has a time it comes due, and that is what the drone waits on.
            var job = new FarmJob(new[]
            {
                FarmAreaState.ShortOfMaterial(North, Corn, "corn seed"),
                FarmAreaState.WaitingOnGrowth(South, Wheat, nextDueHours: 6.5)
            });

            Assert.True(job.Areas[0].IsBlocked);
            Assert.False(job.Areas[1].IsBlocked);
            Assert.Equal(FarmJobStatus.Idle, job.Status);
            Assert.Equal(6.5, job.WakeAtHours);
            Assert.True(job.IdleForWantOfMaterial);
        }

        [Fact]
        public void WaitingOnGrowth_DoesNotCountTowardIdleForWantOfMaterial()
        {
            // R29 asserted directly rather than assumed: growth running is the system
            // working, not a stall to chase.
            var job = new FarmJob(new[] { FarmAreaState.WaitingOnGrowth(South, Wheat, nextDueHours: 2) });

            Assert.False(job.IdleForWantOfMaterial);
            Assert.False(job.Areas[0].IsBlocked);
            Assert.Equal(FarmJobStatus.Idle, job.Status);
        }

        [Fact]
        public void CeilingReached_DoesNotReportAsBlocked()
        {
            // The ceiling is the citizen's own instruction being obeyed. It clears when
            // storage drops, which R30 wakes the drone for.
            var job = new FarmJob(new[] { FarmAreaState.CeilingReached(North, Corn) });

            Assert.False(job.Areas[0].IsBlocked);
            Assert.False(job.IdleForWantOfMaterial);
            Assert.Equal(FarmJobStatus.Idle, job.Status);
            Assert.Null(job.WakeAtHours);
        }

        [Fact]
        public void ASkippedBlock_DoesNotBlockTheArea()
        {
            // R15: a block the drone could not work is passed over. Treating it as a stall
            // stopped the whole field over one occupied square.
            var job = new FarmJob(new[] { FarmAreaState.Skipped(North, Corn) });

            Assert.False(job.Areas[0].IsBlocked);
            Assert.False(job.IdleForWantOfMaterial);
        }

        [Fact]
        public void ARefusedLevelPass_DoesBlockTheArea()
        {
            // Unlike a skipped block, this one needs a citizen: the plants have to go
            // before the pass can run (R18).
            var job = new FarmJob(new[]
            {
                FarmAreaState.LevelPassBlocked(North, Corn, "plants are still standing here")
            });

            Assert.True(job.Areas[0].IsBlocked);
        }

        [Fact]
        public void EveryOtherReason_ReportsAsBlocked()
        {
            Assert.True(FarmAreaState.AwaitingCrop(North).IsBlocked);
            Assert.True(FarmAreaState.ShortOfMaterial(North, Corn, "dirt").IsBlocked);
            Assert.True(FarmAreaState.UnfitGround(North, Corn, "ground pollution").IsBlocked);
            Assert.True(FarmAreaState.RefusedByLaw(North, Corn).IsBlocked);
            Assert.True(FarmAreaState.RefusedByProperty(North, Corn).IsBlocked);
            Assert.True(FarmAreaState.HeldByOverlap(North, Corn, heldPlotCount: 3).IsBlocked);
        }

        [Fact]
        public void AnAreaWithWorkToDo_MakesTheJobWorking()
        {
            var job = new FarmJob(new[]
            {
                FarmAreaState.ShortOfMaterial(North, Corn, "corn seed"),
                FarmAreaState.Workable(South, Wheat, FarmAction.Harvest)
            });

            Assert.Equal(FarmJobStatus.Working, job.Status);
        }

        [Fact]
        public void EveryAreaBlockedWithNothingComingDue_ReportsBlockedOverall()
        {
            var job = new FarmJob(new[]
            {
                FarmAreaState.ShortOfMaterial(North, Corn, "corn seed"),
                FarmAreaState.AwaitingCrop(South)
            });

            Assert.Equal(FarmJobStatus.Blocked, job.Status);
            Assert.Null(job.WakeAtHours);
        }

        [Fact]
        public void NoAreasAtAll_IsItsOwnStatus_NotBlocked()
        {
            var job = new FarmJob(Array.Empty<FarmAreaState>());

            Assert.Equal(FarmJobStatus.NoAreas, job.Status);
            Assert.False(job.IdleForWantOfMaterial);
        }

        // --- R31: the wake time ---

        [Fact]
        public void TheWakeTimeIsTheEarliestAcrossAreas_NotTheFirstAreas()
        {
            var job = new FarmJob(new[]
            {
                FarmAreaState.WaitingOnGrowth(North, Corn, nextDueHours: 9),
                FarmAreaState.WaitingOnGrowth(South, Wheat, nextDueHours: 3)
            });

            Assert.Equal(3, job.WakeAtHours);
        }

        [Fact]
        public void AreasWithNothingComingDue_ContributeNoWakeTime()
        {
            var job = new FarmJob(new[]
            {
                FarmAreaState.CeilingReached(North, Corn),
                FarmAreaState.ShortOfMaterial(South, Wheat, "wheat seed")
            });

            Assert.Null(job.WakeAtHours);
        }

        [Fact]
        public void ANextDueTimeBelongsOnlyToAnAreaWaitingOnGrowth()
        {
            // Guards the wake channel: a due time on a blocked area would wake the drone
            // to find the same missing seed, which R30 exists to avoid.
            Assert.Null(FarmAreaState.ShortOfMaterial(North, Corn, "corn seed").NextDueHours);
            Assert.Null(FarmAreaState.CeilingReached(North, Corn).NextDueHours);
        }

        [Fact]
        public void ANegativeOrMissingDueTime_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FarmAreaState.WaitingOnGrowth(North, Corn, nextDueHours: -1));
        }

        [Fact]
        public void AnAreaAwaitingACrop_CarriesNoCrop()
        {
            var area = FarmAreaState.AwaitingCrop(North);

            Assert.Null(area.Crop);
            Assert.Equal(FarmStallReason.NoCropSelected, area.Stall);
        }

        [Fact]
        public void LawAndPropertyRefusals_StayDistinctMembers()
        {
            // R36 survives into the tab only if the two never collapse into one reason.
            Assert.Equal(FarmStallReason.LawRefusal, FarmAreaState.RefusedByLaw(North, Corn).Stall);
            Assert.Equal(FarmStallReason.PropertyRefusal, FarmAreaState.RefusedByProperty(North, Corn).Stall);
        }
    }
}
