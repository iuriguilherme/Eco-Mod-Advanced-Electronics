using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Services;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's "Farming" tab (U10, R2): present only while a Farm Drone is slotted.
    /// Lists every area the dock farms with its markers and its crop, and answers the one
    /// question a player actually opens this tab with -- why is nothing happening (R35).
    ///
    /// Crop ceilings are not here. They are a different kind of setting: a ceiling belongs
    /// to the crop rather than to an area, so every area growing that crop answers to the
    /// same one, and mixing them into a per-area list would say otherwise. They live in
    /// <see cref="CropCeilingComponent"/>'s own tab (R2, U16).
    ///
    /// Follows <see cref="MiningComponent"/>'s structure: a readiness flag guards every
    /// setter so deserialization does not replay it as a click, derived strings are rebuilt
    /// on refresh, and the commit actions are declared last because RPC methods render
    /// after properties anyway.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Farming", true), HasIcon]
    public class FarmingComponent : WorldObjectComponent, IOperatingWorldObjectComponent
    {
        /// <summary>
        /// How many areas one dock may farm. A cap on the map editor rather than on the
        /// readout: a dock that owns more areas than it can show is a dock a player cannot
        /// drive.
        /// </summary>
        public const int MaxFarmAreas = 8;

        /// <summary>Plot cap per farm area, matching the survey side's own cap.</summary>
        public const int MaxAreaPlots = 25;

        private const int MaxBrowsePositions = MaxFarmAreas;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>True while the farm job has work in hand -- what makes the dock Operating, so fuel and wear flow.</summary>
        public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;

        private bool ready;
        private int browseIndex;

        [SyncToView, Autogen, UITypeName("String")]
        public string JobStatus { get; private set; } = string.Empty;

        /// <summary>The citizen every farming action is performed as, and who is accountable for it.</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string CurrentOwner { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string AreasHeader { get; private set; } = "Farm areas";

        /// <summary>
        /// One line per area: its name, its crop, its markers, and either what the drone
        /// does there next or the one reason it does nothing (R35, R36, R37, R38).
        /// </summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        /// <summary>Selection cursor, by position in the list above. View-only -- it assigns nothing.</summary>
        [Serialized, Eco, Range(0, MaxBrowsePositions), UITypeName("Int32")]
        public int SelectArea
        {
            get => this.browseIndex + 1;
            set
            {
                if (!this.ready) return;
                if (this.browseIndex == value - 1) return;

                var count = this.Parent is DroneDockObject dock ? dock.FarmAreas.Count : 0;
                this.browseIndex = DockReadout.ClampCursor(value - 1, count);
                this.RefreshAll();
            }
        }

        /// <summary>
        /// The crop the selected area grows (R24), chosen from the same kind of tag-filtered
        /// item picker the survey tab uses for its material targets.
        ///
        /// Scoped to the stock "Crop" tag, which 37 vanilla foods carry. A stock tag rather
        /// than one of this mod's own for the reason the survey tab records at length: the
        /// client filters a picker against the tag set built while the controller manager is
        /// constructed, so a tag associated at runtime is invisible to it.
        ///
        /// The picker is a list and the area holds one crop, so the setter takes the last
        /// entry and drops the rest -- choosing a crop replaces the previous choice rather
        /// than adding to it (R24). It is bound to whichever area the cursor names, so
        /// stepping the cursor shows that area's own crop and a choice sticks to the area it
        /// was made on.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(BlockTags.Crop)]
        [LocDescription("The crop this area grows. One per area: choosing a new one replaces the old.")]
        public GamePickerList<FoodItem> AreaCrop { get; set; } = new();

        /// <summary>
        /// The selected area's level-first toggle (R17), off by default. Levelling runs only
        /// while it is on, and clears itself when the pass completes (R21) -- so this is a
        /// request rather than a mode.
        /// </summary>
        [Serialized, Eco, UITypeName("Checkbox")]
        public bool LevelFirst
        {
            get => this.SelectedArea()?.LevelFirst ?? false;
            set
            {
                if (!this.ready) return;
                if (this.Parent is not DroneDockObject dock) return;

                var area = this.SelectedArea();
                if (area == null || area.LevelFirst == value) return;

                dock.SetFarmAreaLevelFirst(area.Id, value);
                this.RefreshAll();
            }
        }

        // ---------------------------------------------------------------
        // Buttons. Declared last because that is where they render anyway.
        // ---------------------------------------------------------------

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await FarmAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.RefreshAll();
        }

        /// <summary>
        /// Records the picked crop against the selected area. A remote call rather than the
        /// picker's own setter, because the setter carries no acting player and the write
        /// has to name one -- and because a crop chosen by accident while scrolling is not
        /// a decision.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Set Crop for Selected Area")]
        public void SetCropForSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var area = this.SelectedArea();
            if (area == null)
            {
                player?.MsgLocStr("No area is selected.", NotificationStyle.Error);
                return;
            }

            // Empty clears the crop, which puts the area back to awaiting one (R26) rather
            // than leaving it growing something the player thought they had removed.
            var pickedType = this.AreaCrop?.GetTypes()?.LastOrDefault();
            var picked = pickedType == null ? null : Item.Get(pickedType);
            if (picked == null)
            {
                dock.SetFarmAreaCrop(area.Id, null);
                this.RefreshAll();
                player?.MsgLocStr($"'{area.Name}' now has no crop and will be left alone.", NotificationStyle.Info);
                return;
            }

            var crop = CropCatalog.ByProduce(picked);
            if (crop == null)
            {
                // Tagged as a crop but nothing plantable yields it -- a mushroom picked
                // wild, say. Refused with the reason rather than stored as a crop that
                // would then stall the area with no seed forever.
                player?.MsgLocStr(
                    $"{picked.DisplayName} is not something the drone can plant -- no seed grows it.",
                    NotificationStyle.Error);
                return;
            }

            dock.SetFarmAreaCrop(area.Id, crop.Key);
            this.RefreshAll();
            player?.MsgLocStr($"'{area.Name}' now grows {crop.DisplayName}.", NotificationStyle.Info);
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign Selected Area")]
        public void AssignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var area = this.SelectedArea();
            if (area == null)
            {
                player?.MsgLocStr("No area is selected to assign.", NotificationStyle.Error);
                return;
            }

            if (!dock.AssignFarmArea(area.Id, true, player?.User, out var refusalReason))
            {
                player?.MsgLocStr($"Could not assign -- {refusalReason}.", NotificationStyle.Error);
                return;
            }

            this.RefreshAll();
            player?.MsgLocStr($"Farm area '{area.Name}' assigned.", NotificationStyle.Info);
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Unassign Selected Area")]
        public void UnassignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var area = this.SelectedArea();
            if (area == null || !area.Assigned)
            {
                player?.MsgLocStr("That area is not assigned.", NotificationStyle.Warning);
                return;
            }

            dock.AssignFarmArea(area.Id, false, player?.User, out _);
            this.RefreshAll();
            player?.MsgLocStr($"Farm area '{area.Name}' unassigned.", NotificationStyle.Info);
        }

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            this.browseIndex = DockReadout.ClampCursor(this.browseIndex, dock.FarmAreas.Count);

            var states = dock.ReadFarmJobStates();
            var job = new FarmJob(states.Select(s => s.State));

            this.AreasDisplay = DockReadout.AtReadableSize(
                dock.FarmAreas.Count == 0
                    ? "No farm areas drawn. Use Manage Areas on Map to draw one."
                    : string.Join("\n", states.Select((s, i) => FormatArea(i + 1, s))));

            var travel = TravelPhrase(dock);
            var status = FarmReadout.FormatJobStatus(job.Status, job.WakeAtHours);
            this.JobStatus = string.IsNullOrEmpty(travel) ? status : $"{status} -- {travel}";

            var citizen = dock.StampedCitizen;
            this.CurrentOwner = citizen == null ? "unstamped" : citizen.Name;

            this.Changed(nameof(this.JobStatus));
            this.Changed(nameof(this.CurrentOwner));
            this.Changed(nameof(this.AreasDisplay));
            this.Changed(nameof(this.SelectArea));
            this.Changed(nameof(this.LevelFirst));
        }

        /// <summary>
        /// One area's block in the list: the headline with its markers, then what it will
        /// do or why it will not. Two lines rather than one because a stall reason is a
        /// sentence, and folding it into the headline pushes the crop off the end.
        /// </summary>
        private static string FormatArea(int position, FarmAreaReadout readout)
        {
            var line = FarmReadout.FormatAreaLine(position, readout.State, readout.IsFlat);

            var stall = FarmReadout.FormatStall(readout.State);
            var detail = string.IsNullOrEmpty(stall) ? FarmReadout.FormatNextAction(readout.State) : stall;

            return string.IsNullOrEmpty(detail) ? line : $"{line}\n    {detail}";
        }

        private FarmAreaEntry SelectedArea()
        {
            if (this.Parent is not DroneDockObject dock) return null;
            return this.browseIndex >= 0 && this.browseIndex < dock.FarmAreas.Count
                ? dock.FarmAreas[this.browseIndex]
                : null;
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
    }
}
