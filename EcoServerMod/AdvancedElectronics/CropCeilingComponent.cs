using System.ComponentModel;
using System.Linq;
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
    /// The dock's "Crop Ceilings" tab (U16, R2, R25): a cap per crop, above which that crop
    /// stops being harvested.
    ///
    /// A tab of its own rather than rows on the Farming tab, because a ceiling is a
    /// different kind of setting from an area's: it belongs to the crop, so every area
    /// growing that crop answers to the same one. Putting it beside per-area controls would
    /// suggest otherwise.
    ///
    /// The list shows every crop the game defines and every configured ceiling at once, and
    /// it scrolls. Scrolling is the right cost here: the alternative is stepping a cursor
    /// through crops one at a time to read each one's ceiling, which takes longer than
    /// scrolling and hides the very comparison a player opened the tab to make. Eco's own
    /// Store tab already sets per-item limits this way.
    /// </summary>
    /// <remarks>
    /// A crop with no ceiling is harvested without limit -- that is the default every crop
    /// starts at, not an unconfigured state to report (R26) -- and setting a ceiling to
    /// zero is how a citizen removes one (R25). So the list is short by design: only the
    /// crops someone has actually capped occupy a row in the dock's storage, while the
    /// catalog below names them all.
    /// </remarks>
    [Serialized, CreateComponentTabLoc("Crop Ceilings", true), HasIcon]
    public class CropCeilingComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        private bool ready;

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string CeilingsHeader { get; private set; } = "Harvest ceilings";

        /// <summary>
        /// Every crop and its ceiling, one per line: the crop's name on the left and its
        /// cap on the right, with "no limit" for the crops nobody has capped. Also shows
        /// what linked storage currently holds, since the ceiling is meaningless without
        /// the number it is compared against.
        /// </summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string CeilingsDisplay { get; private set; } = string.Empty;

        /// <summary>
        /// The crop to set a ceiling for, from the same tag-filtered picker the Farming tab
        /// uses. Scoped to the stock "Crop" tag for the reason recorded on the survey tab:
        /// the client filters against the tag set built while the controller manager is
        /// constructed, so a tag associated at runtime never reaches a picker.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(BlockTags.Crop)]
        [LocDescription("The crop to set a harvest ceiling for.")]
        public GamePickerList<FoodItem> Crop { get; set; } = new();

        /// <summary>
        /// The ceiling to apply, at or above which that crop stops being harvested.
        ///
        /// Zero removes the ceiling rather than forbidding every harvest (R25), which is
        /// also why zero is the safe resting value of this field: a player who opens the
        /// tab and applies without thinking restores the default rather than stopping their
        /// farm.
        /// </summary>
        [Serialized, Eco, UITypeName("Int32")]
        [LocDescription("Stop harvesting this crop once linked storage holds this many. Zero means no limit.")]
        public int Ceiling { get; set; }

        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Apply Ceiling")]
        public void ApplyCeiling(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var pickedType = this.Crop?.GetTypes()?.LastOrDefault();
            var picked = pickedType == null ? null : Item.Get(pickedType);
            if (picked == null)
            {
                player?.MsgLocStr("Pick a crop first.", NotificationStyle.Error);
                return;
            }

            var crop = CropCatalog.ByProduce(picked);
            if (crop == null)
            {
                player?.MsgLocStr(
                    $"{picked.DisplayName} is not something the drone can grow, so a ceiling on it would do nothing.",
                    NotificationStyle.Error);
                return;
            }

            var ceiling = this.Ceiling < 0 ? 0 : this.Ceiling;
            if (!dock.SetCropCeiling(crop.Key, ceiling, player?.User))
            {
                player?.MsgLocStr("You need full access on this drone dock to set its ceilings.", NotificationStyle.Error);
                return;
            }

            this.RefreshAll();

            player?.MsgLocStr(
                ceiling == 0
                    ? $"{crop.DisplayName} now has no ceiling and is harvested without limit."
                    : $"{crop.DisplayName} stops being harvested at {ceiling} in linked storage.",
                NotificationStyle.Info);
        }

        /// <summary>
        /// Removes every ceiling at once, putting all crops back to the default. Separate
        /// from applying a zero because clearing thirty caps one at a time through the
        /// picker is not a thing anyone would do.
        /// </summary>
        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Clear All Ceilings")]
        public void ClearAllCeilings(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var cleared = dock.CropCeilings.Select(c => c.Crop).ToList();
            if (cleared.Count == 0)
            {
                player?.MsgLocStr("No ceilings are set.", NotificationStyle.Warning);
                return;
            }

            if (!dock.HasFullAccess(player?.User))
            {
                player?.MsgLocStr("You need full access on this drone dock to clear its ceilings.", NotificationStyle.Error);
                return;
            }

            foreach (var crop in cleared)
                dock.SetCropCeiling(crop, 0, player?.User);

            this.RefreshAll();
            player?.MsgLocStr($"Cleared {cleared.Count} ceilings. Every crop is harvested without limit.", NotificationStyle.Info);
        }

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        public void RefreshAll()
        {
            if (!this.ready) return;
            if (this.Parent is not DroneDockObject dock) return;

            var catalog = CropCatalog.All;

            this.CeilingsDisplay = DockReadout.AtReadableSize(
                catalog.Count == 0
                    ? "No plantable crops were found."
                    : string.Join("\n", catalog.Select(crop => FormatRow(dock, crop))));

            this.Changed(nameof(this.CeilingsDisplay));
        }

        /// <summary>
        /// One crop's row: its name, its ceiling, and what storage holds. The stored count
        /// is what makes the row readable -- "300" alone does not say whether the crop is
        /// being harvested right now, and that is the question the tab exists to answer.
        /// </summary>
        private static string FormatRow(DroneDockObject dock, CropEntry crop)
        {
            var ceiling = dock.CropCeilingFor(crop.Key);
            var stored = dock.CountInLinkedStorage(crop.Key);

            if (ceiling == 0)
                return $"{crop.DisplayName} -- no limit ({stored} stored)";

            var holding = stored >= ceiling ? "  <- holding" : string.Empty;
            return $"{crop.DisplayName} -- {ceiling} ({stored} stored){holding}";
        }
    }
}
