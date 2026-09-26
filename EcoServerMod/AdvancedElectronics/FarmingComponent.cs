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
        // MaxFarmAreas used to be declared here, at 8. There is ONE per-dock area limit now
        // (R19, KTD7) and it lives in AreaCapacity, in the Eco-free assembly, because both tabs
        // and both pickers have to meet the same number and it is the one part of this unit that
        // can be unit-tested. Ten rather than eight: lowering the ceiling would have put existing
        // docks over a limit for a reason the player never chose.

        /// <summary>Plot cap per farm area, matching the survey side's own cap.</summary>
        public const int MaxAreaPlots = 25;

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

        /// <summary>
        /// Selection cursor, by position in the list above. View-only -- it assigns nothing.
        ///
        /// <para>
        /// The range reaches every area a pre-fold dock can carry, not just the ten a dock may
        /// ADD (U10, KTD7). A dock folded above the limit keeps all its areas, and a cursor
        /// bounded by the cap would leave the ones past the tenth listed and unreachable — the
        /// player could read a farm's row and never select, crop, level, assign or delete it.
        /// The live bound is the clamp below, against the count of FARMS rather than of the
        /// dock's whole collection, because this tab shows one kind.
        /// </para>
        /// </summary>
        [Serialized, Eco, Range(0, AreaCapacity.MaxAddressablePositions), UITypeName("Int32")]
        public int SelectArea
        {
            get => this.browseIndex + 1;
            set
            {
                if (!this.ready) return;
                if (this.browseIndex == value - 1) return;

                var count = this.Parent is DroneDockObject dock ? dock.FarmingAreas.Count() : 0;
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
        /// <remarks>
        /// Full access, not the bare <c>[Eco]</c> default. A bare attribute requires only
        /// consumer access (<c>EcoAttribute.RequiredAccess</c>: "If not specified then by
        /// default requires Consumer Access"), and this toggle starts a pass that removes
        /// standing ground across the area and back-fills from the owner's storage. The
        /// dock re-checks the same gate, because an attribute guards the RPC and not the
        /// state operation behind it.
        /// </remarks>
        [Serialized, Eco(AccessType.FullAccess), UITypeName("Boolean")]
        public bool LevelFirst
        {
            get => this.SelectedArea()?.LevelFirst ?? false;
            set
            {
                if (!this.ready) return;
                if (this.Parent is not DroneDockObject dock) return;

                var area = this.SelectedArea();
                if (area == null || area.LevelFirst == value) return;

                // No acting player reaches a property setter, so a request to turn the
                // toggle ON is carried by the button below, which has one. Turning it off
                // is always allowed.
                if (value) return;

                dock.SetFarmAreaLevelFirst(area.Id, false);
                this.RefreshAll();
            }
        }

        // ---------------------------------------------------------------
        // Buttons. Declared last because that is where they render anyway.
        // ---------------------------------------------------------------

        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            // Re-checked here as well as on the attribute: the editor can redraw or delete
            // an ASSIGNED area, which re-aims a drone working under someone else's stamp.
            if (!dock.HasFullAccess(player?.User))
            {
                player?.MsgLocStr("You need full access on this drone dock to edit its areas.", NotificationStyle.Error);
                return;
            }

            await FarmAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.RefreshAll();
        }

        /// <summary>
        /// Records the picked crop against the selected area. A remote call rather than the
        /// picker's own setter, because the setter carries no acting player and the write
        /// has to name one -- and because a crop chosen by accident while scrolling is not
        /// a decision.
        /// </summary>
        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Set Crop for Selected Area")]
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
                if (!dock.SetFarmAreaCrop(area.Id, null, player?.User))
                {
                    player?.MsgLocStr("You need full access on this drone dock to clear its crop.", NotificationStyle.Error);
                    return;
                }

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
                // A chance drop (bean sprouts, beet greens) names the crop it comes from, so
                // the citizen is told what to pick instead.
                var source = CropCatalog.ByAnyYield(picked);
                player?.MsgLocStr(
                    source != null
                        ? $"{picked.DisplayName} only drops by chance from {source.UniqueName}, so the drone cannot plant it. Pick {source.UniqueName} instead."
                        : $"{picked.DisplayName} is not something the drone can plant -- no seed grows it.",
                    NotificationStyle.Error);
                return;
            }

            if (!dock.SetFarmAreaCrop(area.Id, crop.Key, player?.User))
            {
                player?.MsgLocStr("You need full access on this drone dock to set its crop.", NotificationStyle.Error);
                return;
            }

            this.RefreshAll();
            player?.MsgLocStr($"'{area.Name}' now grows {crop.DisplayName}.", NotificationStyle.Info);
        }

        /// <summary>
        /// Requests the level pass for the selected area (R17). A button rather than the
        /// checkbox alone, because only a remote call carries the acting player, and a pass
        /// that removes the owner's ground should name who asked for it.
        /// </summary>
        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Level Selected Area First")]
        public void RequestLevelPass(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var area = this.SelectedArea();
            if (area == null)
            {
                player?.MsgLocStr("No area is selected.", NotificationStyle.Error);
                return;
            }

            if (!dock.SetFarmAreaLevelFirst(area.Id, true, player?.User))
            {
                player?.MsgLocStr("You need full access on this drone dock to level its ground.", NotificationStyle.Error);
                return;
            }

            this.RefreshAll();
            player?.MsgLocStr($"'{area.Name}' will be levelled before it is farmed.", NotificationStyle.Info);
        }

        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Assign Selected Area")]
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

        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Unassign Selected Area")]
        public void UnassignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var area = this.SelectedArea();

            // The CLAIM is the assignment (U10, R17). This read the legacy row's Assigned flag,
            // and that row is emptied by the fold -- so on a migrated dock Unassign refused
            // every area as "not assigned" while its drone was out working one.
            if (!dock.IsFarmAssignmentOfMine(area))
            {
                player?.MsgLocStr("That area is not assigned.", NotificationStyle.Warning);
                return;
            }

            if (!dock.AssignFarmArea(area.Id, false, player?.User, out var unassignRefusal))
            {
                player?.MsgLocStr($"Could not unassign -- {unassignRefusal}.", NotificationStyle.Error);
                return;
            }

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

            // Materialised once for the whole refresh. FarmingAreas is a deferred filter over
            // the dock's whole collection, and this ran off the dock's tick walking it three
            // times -- once for the count, once inside the states and once more inside the job.
            // The ceiling ledger is built once here for the same reason: the two readers below
            // each rebuilt it from the dock's ceiling rows.
            var farms = dock.FarmingAreas.ToList();
            var farmCount = farms.Count;
            this.browseIndex = DockReadout.ClampCursor(this.browseIndex, farmCount);

            var ledger = dock.ReadCropCeilings();
            var states = dock.ReadFarmJobStates(farms, ledger);
            var job = dock.ReadFarmJob(farms, ledger);

            this.AreasDisplay = DockReadout.AtReadableSize(
                farmCount == 0
                    ? "No farm areas drawn. Use Manage Areas on Map to draw one."
                    : string.Join("\n", states.Select((s, i) => FormatArea(i + 1, s))));

            var travel = TravelPhrase(dock);
            var status = FarmReadout.FormatJobStatus(job.Status, job.WakeAtHours);

            // Every area stopped on something only a player can clear: the one line on the
            // tab that must not read like routine.
            if (job.Status == FarmJobStatus.Blocked)
                status = $"<color={FarmReadout.NeedsYouColor}>{status} -- see the areas below</color>";
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

            // Working, waiting on something that clears by itself, or needing the player --
            // each in its own colour, and the last one says what to do.
            var detail = FarmReadout.FormatDetail(readout.State);

            return string.IsNullOrEmpty(detail) ? line : $"{line}\n    {detail}";
        }

        /// <summary>
        /// The area the cursor names, or null when this dock farms nothing (U10, R17).
        ///
        /// <para>
        /// Indexed into the FARMING-kind areas, in the collection's own order, which is the
        /// order <see cref="DroneDockObject.ReadFarmJobStates"/> renders and therefore the order
        /// the player is counting rows in. It used to index the legacy farm collection, which
        /// the fold empties — so on a migrated dock every button on this tab reported "no area
        /// is selected" whatever the list showed.
        /// </para>
        /// <para>
        /// <b>Seam.</b> Eco-coupled: the parent is a world object and the entry is its
        /// serialized state. The cursor arithmetic is <see cref="DockReadout.ClampCursor"/>,
        /// applied against the count of FARMS rather than of the dock's whole collection, and
        /// unit-tested in the navigation assembly.
        /// </para>
        /// </summary>
        private SurveyAreaEntry SelectedArea()
        {
            if (this.Parent is not DroneDockObject dock) return null;

            var farms = dock.FarmingAreas.ToList();
            return this.browseIndex >= 0 && this.browseIndex < farms.Count
                ? farms[this.browseIndex]
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
