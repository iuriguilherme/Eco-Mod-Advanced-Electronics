using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Services;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's "Mining" tab (U9): present only while a mining drone is slotted
    /// (R29, KD7), reporting the assigned area, the stamped citizen, job progress, and
    /// linked-storage headroom. Follows <see cref="SurveyComponent"/>'s structure: a
    /// readiness flag guards every setter so deserialization does not replay it as a
    /// click, values are derived strings rebuilt on refresh, and the one commit action
    /// (the assign button) is declared last because RPC methods always render after
    /// properties.
    ///
    /// Browsing and committing are separate controls (R40): a number selector pages
    /// through the survey docks the tab can see, and the single button assigns the
    /// selected one -- it is a remote call and therefore carries the acting player,
    /// which is the only way the stamp gets a citizen (the selector's setter never does).
    /// </summary>
    [Serialized, CreateComponentTabLoc("Mining", true), HasIcon]
    public class MiningComponent : WorldObjectComponent, IOperatingWorldObjectComponent
    {
        private const int MaxBrowsePositions = 40;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>True while the mining job is working (R45) -- what makes the dock Operating, so fuel and wear flow.</summary>
        public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;

        private bool ready;
        private int browseIndex;

        // Four rows, not nine. The member NAME is the row label, so these read "Assigned Area",
        // "Current Owner", "Job Status", "Progress".
        //
        // What was dropped -- stop reason, skips by category, last refusal, shaft depth with its
        // two stamps, and a static headroom note -- was a debugging surface that had grown on a
        // player-facing panel, one row per fact discovered during a live pass. Every one of them
        // is still printed by `/drone state`, which is where "why was that plot skipped" belongs.
        // Nothing was lost, and the headroom row carried no data at all: it was a fixed sentence.

        [SyncToView, Autogen, UITypeName("String")]
        public string AssignedArea { get; private set; } = string.Empty;

        /// <summary>The citizen every removal is performed as, and who is accountable for it (R18).</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string CurrentOwner { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string JobStatus { get; private set; } = string.Empty;

        /// <summary>Area progress and the current plot's shaft depth, on one line.</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string Progress { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string BrowseHeader { get; private set; } = "Available mining areas";

        /// <summary>The offered survey docks and their areas, numbered -- what the selector below refers to.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AvailableAreas { get; private set; } = string.Empty;

        /// <summary>Selection cursor, by position in the offered list. View-only -- it never assigns anything (R40).</summary>
        [Serialized, Eco, Range(0, MaxBrowsePositions), UITypeName("Int32")]
        public int SelectArea
        {
            get => this.browseIndex + 1;
            set
            {
                if (!this.ready) return;
                var offered = this.OfferedAreas().ToList();
                if (this.browseIndex == value - 1) return;

                this.browseIndex = DockReadout.ClampCursor(value - 1, offered.Count);
                this.RefreshAll();
            }
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign Selected Area")]
        public void AssignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var offered = this.OfferedAreas().ToList();
            if (this.browseIndex < 0 || this.browseIndex >= offered.Count)
            {
                player?.MsgLocStr("No area is selected to assign.", NotificationStyle.Error);
                return;
            }

            var (sourceDock, area) = offered[this.browseIndex];
            if (!dock.AssignMiningArea(sourceDock, area, player?.User, out var refusalReason))
                player?.MsgLocStr($"Could not assign -- {refusalReason}.", NotificationStyle.Error);

            this.RefreshAll();
        }

        /// <summary>
        /// Clears this dock's mining assignment (R7). Until this existed the only way to stop a
        /// drone was to point it at another area, which is not a stop -- it is a different job, and
        /// it meant an assignment survived every restart and re-ran itself on load.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Unassign Area")]
        public void UnassignArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            if (dock.AssignedMiningArea == null)
            {
                player?.MsgLocStr("This dock has no mining area assigned.", NotificationStyle.Warning);
                return;
            }

            dock.UnassignMiningArea();
            this.RefreshAll();
            player?.MsgLocStr("Mining area unassigned. The drone returns to its dock.", NotificationStyle.Info);
        }

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        /// <summary>
        /// The survey docks this dock may consume areas from: same owner, holding at least one
        /// area (R2, R3, KD15).
        ///
        /// The owner filter is the answer to "a drone dock should not interact with every other
        /// drone dock in the world -- it is a private world object like any other". Until it
        /// existed, this listed every dock on the server, including ones the player had no access
        /// to, and the assign path would happily take one.
        ///
        /// It is a rule rather than a player action, which is its known weakness: co-owned or
        /// company land still shares broadly, because Owners is a Title and two docks on the same
        /// deed legitimately share it. That is the accepted cost of not adding a pairing UI.
        /// Access filtering (R39) still happens at assign time, where the acting player is known
        /// -- ownership answers "whose docks are these", authorization answers "may YOU use them",
        /// and both are needed.
        /// </summary>
        private IEnumerable<(DroneDockObject Dock, SurveyAreaEntry Area)> OfferedAreas()
        {
            if (this.Parent is not DroneDockObject self) return Enumerable.Empty<(DroneDockObject, SurveyAreaEntry)>();

            return ServiceHolder<IWorldObjectManager>.Obj.All
                .OfType<DroneDockObject>()
                .Where(d => !d.IsDestroyed && d.HasComponent<SurveyComponent>() && SharesOwnerWith(self, d))
                .SelectMany(d => d.SurveyAreas.Select(a => (Dock: d, Area: a)));
        }

        /// <summary>
        /// Whether two docks belong to the same owner. An unowned dock matches only another
        /// unowned one -- deliberately, since "nobody owns it" is not a household, and treating
        /// null as a wildcard would put every unclaimed dock in the world back on the list.
        /// </summary>
        private static bool SharesOwnerWith(DroneDockObject self, DroneDockObject other) =>
            ReferenceEquals(self, other) || Equals(self.Owners, other.Owners);

        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            var offered = this.OfferedAreas().ToList();
            this.browseIndex = DockReadout.ClampCursor(this.browseIndex, offered.Count);

            var assigned = dock.AssignedMiningArea;

            this.AvailableAreas = DockReadout.AtReadableSize(offered.Count == 0
                ? "No survey docks with an area were found."
                : string.Join("\n", offered.Select((o, i) => MiningReadout.FormatOfferedAreaLine(
                    i + 1, o.Dock.Name, o.Area.Name, o.Area.PlotCount,
                    isAssigned: assigned != null
                                && assigned.OwningDockId == o.Dock.ObjectID
                                && assigned.AreaId == o.Area.Id,
                    isMined: IsMinedOut(dock, o.Area)))));

            var reference = assigned;
            this.AssignedArea = reference == null ? "none" : this.DescribeAssignment(dock, reference);

            var citizen = dock.StampedCitizen;
            this.CurrentOwner = citizen == null ? "unstamped" : citizen.Name;

            var job = dock.MiningJob;

            var travel = TravelPhrase(dock);

            if (job != null)
            {
                // The halt refuses dispatch before a job exists, so a blocked reason has to be
                // able to speak with no job present -- setting it only inside this branch is what
                // made a halted dock silent. Folded into the status line now that the tab has no
                // row of its own for it.
                this.JobStatus = MiningReadout.FormatJobStatus(job.Status, job.WorkedCount, reference != null, travel);

                // The shaft half only while one is actually being cut. A finished or abandoned job
                // keeps its last layer counts, and reporting them next to "returning to dock" reads
                // as a shaft still in progress.
                var cutting = job.Status == MiningJobStatus.Working || job.Status == MiningJobStatus.WaitingToUnload;

                // And nothing at all once the area is gone. The counts describe work on an area
                // this dock is no longer pointed at, so leaving them up meant an unassigned dock
                // still reporting a plot tally from the job it had just been taken off.
                this.Progress = reference == null
                    ? string.Empty
                    : MiningReadout.FormatProgress(
                        job.PlotCount, job.WorkedCount, job.SkippedCount,
                        cutting ? job.ShaftLayersDone : 0,
                        cutting ? job.ShaftLayersTotal : 0);
            }
            else
            {
                this.JobStatus = string.IsNullOrEmpty(travel) ? "no drone docked" : travel;
                this.Progress = string.Empty;
            }

            var blocked = MiningReadout.FormatBlockedReason(MiningHalt.IsHalted, job?.EndReason);
            if (!string.IsNullOrWhiteSpace(blocked))
                this.JobStatus = blocked;

            this.Changed(nameof(this.AssignedArea));
            this.Changed(nameof(this.CurrentOwner));
            this.Changed(nameof(this.JobStatus));
            this.Changed(nameof(this.Progress));
            this.Changed(nameof(this.AvailableAreas));
            this.Changed(nameof(this.SelectArea));
        }

        /// <summary>Where this dock's drone is, or empty when there is no drone to ask.</summary>
        private static string TravelPhrase(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed) return string.Empty;

            return drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                ? DockReadout.FormatTravel(lifecycle.Status, lifecycle.TravelTarget)
                : string.Empty;
        }

        /// <summary>
        /// Whether THIS dock has nothing left to mine in <paramref name="area"/>: every plot mined
        /// at least once, none re-surveyed since.
        ///
        /// Read per dock, not per area, because the mined stamps live on the mining dock while the
        /// surveyed stamps live on the area (KTD12). Two mining docks pointed at one survey area
        /// therefore answer this differently, and each is right about itself.
        /// </summary>
        private static bool IsMinedOut(DroneDockObject dock, SurveyAreaEntry area)
        {
            // A completed job is the stronger signal and has to be checked first, because a plot
            // the job SKIPPED never gets a mined stamp -- it was refused by law, or by property, or
            // could not be reached, so nothing was removed and nothing was recorded. Judging by
            // stamps alone, an area finished with one skip stays unmarked forever, which is the
            // opposite of what the marker is for: the job's own verdict was "nothing left here".
            if (dock.MiningJob?.Status == MiningJobStatus.Complete && dock.MiningJobAreaId == area.Id)
                return true;

            // Otherwise fall back to the stamps, which is what answers for an area this dock
            // worked under an earlier job it no longer holds.
            var surveyed = area.ReadSurveyedStamps();
            var mined = dock.ReadMinedStamps();

            return PlotFreshness.IsMinedOut(
                area.ToSurveyArea().EnumeratePlots(), surveyed.StampFor, mined.StampFor);
        }

        private string DescribeAssignment(DroneDockObject dock, MiningAreaRef reference)
        {
            var signal = reference.Resolve(out var sourceDock, out var area);
            return signal switch
            {
                AreaLookupSignal.Found => $"{sourceDock.Name} -- {area.Name}",
                AreaLookupSignal.NotYetResolved => "resolving...",
                _ => "gone"
            };
        }
    }
}
