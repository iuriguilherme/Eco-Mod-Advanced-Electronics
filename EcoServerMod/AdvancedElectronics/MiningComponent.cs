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

        [SyncToView, Autogen, UITypeName("String")]
        public string AssignedAreaDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string StampedCitizenDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string JobStatusDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string StopReasonDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string ProgressDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string SkipLineDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string HeadroomDisplay { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string BrowseHeader { get; private set; } = "Browse a survey dock's " + DroneDockObject.DroneAreaLabel + "s";

        /// <summary>The offered survey docks and their areas, numbered -- what the position below refers to.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string BrowseAreasDisplay { get; private set; } = string.Empty;

        /// <summary>Browse cursor, by position in the offered list. View-only -- it never assigns anything (R40).</summary>
        [Serialized, Eco, Range(0, MaxBrowsePositions), UITypeName("Int32")]
        public int BrowsePosition
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

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign Browsed Area")]
        public void AssignBrowsedArea(Player player)
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

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        /// <summary>
        /// Every survey dock in the world holding at least one area (R2, R3, KD15) --
        /// access filtering (R39) happens at assign time, where the acting player is
        /// known; the browse cursor itself carries no player context to filter by.
        /// </summary>
        private IEnumerable<(DroneDockObject Dock, SurveyAreaEntry Area)> OfferedAreas() =>
            ServiceHolder<IWorldObjectManager>.Obj.All
                .OfType<DroneDockObject>()
                .Where(d => !d.IsDestroyed && d.HasComponent<SurveyComponent>())
                .SelectMany(d => d.SurveyAreas.Select(a => (Dock: d, Area: a)));

        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            var offered = this.OfferedAreas().ToList();
            this.browseIndex = DockReadout.ClampCursor(this.browseIndex, offered.Count);

            this.BrowseAreasDisplay = offered.Count == 0
                ? "No survey docks with an area were found."
                : string.Join("\n", offered.Select((o, i) => $"{i + 1}. {o.Dock.Name} -- {o.Area.Name} ({o.Area.PlotCount} plots)"));

            var reference = dock.AssignedMiningArea;
            this.AssignedAreaDisplay = reference == null ? "none" : this.DescribeAssignment(dock, reference);

            var citizen = dock.StampedCitizen;
            this.StampedCitizenDisplay = citizen == null ? "unstamped" : citizen.Name;

            var job = dock.MiningJob;

            if (job != null)
            {
                this.JobStatusDisplay = MiningReadout.FormatJobStatus(job.Status, job.WorkedCount);
                this.ProgressDisplay = $"worked {job.WorkedCount}, skipped {job.SkippedCount}";
                this.SkipLineDisplay = MiningReadout.FormatSkipLine(job.SkipCountsByCategory(), job.SkippedCount);
            }
            else
            {
                this.JobStatusDisplay = "no drone docked";
                this.ProgressDisplay = string.Empty;
                this.SkipLineDisplay = string.Empty;
            }

            // Outside the job branch on purpose: the halt refuses dispatch before a job exists, so
            // the case that most needs explaining is exactly the one with no job to read an end
            // reason from. Setting this inside the branches is what made a halted dock silent.
            this.StopReasonDisplay = MiningReadout.FormatBlockedReason(MiningHalt.IsHalted, job?.EndReason);

            this.HeadroomDisplay = "measured on unload -- see the last unload result";

            this.Changed(nameof(this.AssignedAreaDisplay));
            this.Changed(nameof(this.StampedCitizenDisplay));
            this.Changed(nameof(this.JobStatusDisplay));
            this.Changed(nameof(this.StopReasonDisplay));
            this.Changed(nameof(this.ProgressDisplay));
            this.Changed(nameof(this.SkipLineDisplay));
            this.Changed(nameof(this.HeadroomDisplay));
            this.Changed(nameof(this.BrowseAreasDisplay));
            this.Changed(nameof(this.BrowsePosition));
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
