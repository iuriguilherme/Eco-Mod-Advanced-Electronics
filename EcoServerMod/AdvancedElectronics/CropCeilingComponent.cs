using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
    /// growing that crop answers to the same one.
    ///
    /// Top to bottom: a crop picker that filters the list, one stepper row per crop, and a
    /// Clear All button. Each row's number IS that crop's ceiling, and a change applies the
    /// moment it is made -- there is no Apply step. An empty picker shows every row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why one hand-declared property per crop.</b> A row's label has to be the crop's name,
    /// and autogen takes a property row's label from its <c>LocDisplayName</c>, which is fixed
    /// when the mod is built. The runtime alternative, <c>DynamicTitle</c>, blanks the label
    /// and kills the input on a property row (proven in <see cref="UIShowcaseComponent"/>,
    /// round 2). So the rows are the vanilla crops, listed here; each binds to its crop by the
    /// engine's species name, and a row whose species this world does not have stays hidden.
    /// A crop another mod adds has no row, and reaches its ceiling through
    /// <c>/drone ceiling</c>, which the tab names when such a crop exists.
    /// </para>
    /// <para>
    /// <b>Why rows carry no stored-count readout.</b> A label cannot change while the window
    /// is open, and the ceiling-reached state already shows per area on the Farming tab.
    /// </para>
    /// <para>
    /// <b>Access.</b> Each row requires full access on the dock, enforced by Eco before the
    /// setter runs. Eco drops a refused write silently -- a property setter is given no
    /// citizen to message -- the same as vanilla's for-sale price. The row shows the true
    /// ceiling again when the window is reopened.
    /// </para>
    /// <para>
    /// A crop with no ceiling is harvested without limit, and zero is how a citizen removes
    /// one (R25). Rows hold nothing of their own: every read and write goes through the
    /// dock's ceiling ledger, so there is no second copy to drift from what the drone reads.
    /// </para>
    /// </remarks>
    [Serialized, CreateComponentTabLoc("Crop Ceilings", true), HasIcon]
    public class CropCeilingComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>
        /// The species names that have a row below. Must list exactly the keys the row
        /// members bind to; the refresh and the overflow note read it rather than reflecting
        /// over the members.
        /// </summary>
        private static readonly HashSet<string> RowKeys = new(StringComparer.Ordinal)
        {
            "Agave",
            "AmanitaMushroom",
            "ArcticWillow",
            "BarrelCactus",
            "Beans",
            "Beets",
            "BigBluestem",
            "BoleteMushroom",
            "Bullrush",
            "Bunchgrass",
            "Camas",
            "CommonGrass",
            "CookeinaMushroom",
            "Corn",
            "Cotton",
            "CreosoteBush",
            "CriminiMushroom",
            "Daisy",
            "DwarfWillow",
            "Fern",
            "FilmyFern",
            "Fireweed",
            "Flax",
            "Heliconia",
            "Huckleberry",
            "Jointfir",
            "Kelp",
            "KingFern",
            "LatticeMushroom",
            "Lupine",
            "OceanSpray",
            "Orchid",
            "Papaya",
            "Pineapple",
            "PricklyPear",
            "Pumpkin",
            "Rice",
            "RoseBush",
            "Saxifrage",
            "Seagrass",
            "Sunflower",
            "Taro",
            "Tomatoes",
            "Trillium",
            "Tulip",
            "Waterweed",
            "Wheat",
            "WhiteBursage",
        };

        private bool ready;

        /// <summary>Whether the overflow note has been worked out; see <see cref="RefreshAll"/>.</summary>
        private bool overflowResolved;

        /// <summary>The species shown on the last refresh, so visibility is re-pushed only when it changes.</summary>
        private HashSet<string> lastShown = new(StringComparer.Ordinal);

        /// <summary>The ceilings pushed on the last refresh, so a row is re-pushed only when its value changes.</summary>
        private readonly Dictionary<string, int> lastValues = new(StringComparer.Ordinal);

        /// <summary>
        /// Filters which crop rows appear; empty shows every crop. Scoped to the stock "Crop"
        /// tag for the reason recorded on the survey tab: the client filters against the tag
        /// set built while the controller manager is constructed, so a tag associated at
        /// runtime never reaches a picker. Kept per dock, so the tab reopens filtered.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(BlockTags.Crop)]
        [LocDescription("Pick crops to show only their rows below. Leave empty to show every crop.")]
        public GamePickerList<FoodItem> Crop { get; set; } = new();

        // ---------------------------------------------------------------
        // One row per vanilla crop, alphabetical by label. Generated from the game's own seed
        // and plant definitions: the label is the harvested item's name, or the plant's name
        // where several plants yield the same item (plant fibers). Trees are excluded, as
        // they are from the crop catalog.
        // ---------------------------------------------------------------

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Agave Leaves"), VisibilityParam(nameof(ShowAgave))]
        public int AgaveCeiling { get => this.CeilingOf("Agave"); set => this.SetFromRow("Agave", value); }
        [SyncToView] public bool ShowAgave() => this.IsShown("Agave");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Amanita Mushrooms"), VisibilityParam(nameof(ShowAmanitaMushroom))]
        public int AmanitaMushroomCeiling { get => this.CeilingOf("AmanitaMushroom"); set => this.SetFromRow("AmanitaMushroom", value); }
        [SyncToView] public bool ShowAmanitaMushroom() => this.IsShown("AmanitaMushroom");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Arctic Willow"), VisibilityParam(nameof(ShowArcticWillow))]
        public int ArcticWillowCeiling { get => this.CeilingOf("ArcticWillow"); set => this.SetFromRow("ArcticWillow", value); }
        [SyncToView] public bool ShowArcticWillow() => this.IsShown("ArcticWillow");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Barrel Cactus"), VisibilityParam(nameof(ShowBarrelCactus))]
        public int BarrelCactusCeiling { get => this.CeilingOf("BarrelCactus"); set => this.SetFromRow("BarrelCactus", value); }
        [SyncToView] public bool ShowBarrelCactus() => this.IsShown("BarrelCactus");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Beans"), VisibilityParam(nameof(ShowBeans))]
        public int BeansCeiling { get => this.CeilingOf("Beans"); set => this.SetFromRow("Beans", value); }
        [SyncToView] public bool ShowBeans() => this.IsShown("Beans");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Beet"), VisibilityParam(nameof(ShowBeets))]
        public int BeetsCeiling { get => this.CeilingOf("Beets"); set => this.SetFromRow("Beets", value); }
        [SyncToView] public bool ShowBeets() => this.IsShown("Beets");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Big Bluestem"), VisibilityParam(nameof(ShowBigBluestem))]
        public int BigBluestemCeiling { get => this.CeilingOf("BigBluestem"); set => this.SetFromRow("BigBluestem", value); }
        [SyncToView] public bool ShowBigBluestem() => this.IsShown("BigBluestem");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Bolete Mushrooms"), VisibilityParam(nameof(ShowBoleteMushroom))]
        public int BoleteMushroomCeiling { get => this.CeilingOf("BoleteMushroom"); set => this.SetFromRow("BoleteMushroom", value); }
        [SyncToView] public bool ShowBoleteMushroom() => this.IsShown("BoleteMushroom");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Bullrush"), VisibilityParam(nameof(ShowBullrush))]
        public int BullrushCeiling { get => this.CeilingOf("Bullrush"); set => this.SetFromRow("Bullrush", value); }
        [SyncToView] public bool ShowBullrush() => this.IsShown("Bullrush");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Bunchgrass"), VisibilityParam(nameof(ShowBunchgrass))]
        public int BunchgrassCeiling { get => this.CeilingOf("Bunchgrass"); set => this.SetFromRow("Bunchgrass", value); }
        [SyncToView] public bool ShowBunchgrass() => this.IsShown("Bunchgrass");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Camas Bulb"), VisibilityParam(nameof(ShowCamas))]
        public int CamasCeiling { get => this.CeilingOf("Camas"); set => this.SetFromRow("Camas", value); }
        [SyncToView] public bool ShowCamas() => this.IsShown("Camas");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Common Grass"), VisibilityParam(nameof(ShowCommonGrass))]
        public int CommonGrassCeiling { get => this.CeilingOf("CommonGrass"); set => this.SetFromRow("CommonGrass", value); }
        [SyncToView] public bool ShowCommonGrass() => this.IsShown("CommonGrass");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Cookeina Mushrooms"), VisibilityParam(nameof(ShowCookeinaMushroom))]
        public int CookeinaMushroomCeiling { get => this.CeilingOf("CookeinaMushroom"); set => this.SetFromRow("CookeinaMushroom", value); }
        [SyncToView] public bool ShowCookeinaMushroom() => this.IsShown("CookeinaMushroom");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Corn"), VisibilityParam(nameof(ShowCorn))]
        public int CornCeiling { get => this.CeilingOf("Corn"); set => this.SetFromRow("Corn", value); }
        [SyncToView] public bool ShowCorn() => this.IsShown("Corn");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Cotton Boll"), VisibilityParam(nameof(ShowCotton))]
        public int CottonCeiling { get => this.CeilingOf("Cotton"); set => this.SetFromRow("Cotton", value); }
        [SyncToView] public bool ShowCotton() => this.IsShown("Cotton");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Creosote Flower"), VisibilityParam(nameof(ShowCreosoteBush))]
        public int CreosoteBushCeiling { get => this.CeilingOf("CreosoteBush"); set => this.SetFromRow("CreosoteBush", value); }
        [SyncToView] public bool ShowCreosoteBush() => this.IsShown("CreosoteBush");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Crimini Mushrooms"), VisibilityParam(nameof(ShowCriminiMushroom))]
        public int CriminiMushroomCeiling { get => this.CeilingOf("CriminiMushroom"); set => this.SetFromRow("CriminiMushroom", value); }
        [SyncToView] public bool ShowCriminiMushroom() => this.IsShown("CriminiMushroom");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Daisy"), VisibilityParam(nameof(ShowDaisy))]
        public int DaisyCeiling { get => this.CeilingOf("Daisy"); set => this.SetFromRow("Daisy", value); }
        [SyncToView] public bool ShowDaisy() => this.IsShown("Daisy");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Dwarf Willow"), VisibilityParam(nameof(ShowDwarfWillow))]
        public int DwarfWillowCeiling { get => this.CeilingOf("DwarfWillow"); set => this.SetFromRow("DwarfWillow", value); }
        [SyncToView] public bool ShowDwarfWillow() => this.IsShown("DwarfWillow");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Fiddleheads"), VisibilityParam(nameof(ShowFern))]
        public int FernCeiling { get => this.CeilingOf("Fern"); set => this.SetFromRow("Fern", value); }
        [SyncToView] public bool ShowFern() => this.IsShown("Fern");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Filmy Fern"), VisibilityParam(nameof(ShowFilmyFern))]
        public int FilmyFernCeiling { get => this.CeilingOf("FilmyFern"); set => this.SetFromRow("FilmyFern", value); }
        [SyncToView] public bool ShowFilmyFern() => this.IsShown("FilmyFern");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Fireweed Shoots"), VisibilityParam(nameof(ShowFireweed))]
        public int FireweedCeiling { get => this.CeilingOf("Fireweed"); set => this.SetFromRow("Fireweed", value); }
        [SyncToView] public bool ShowFireweed() => this.IsShown("Fireweed");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Flax Stem"), VisibilityParam(nameof(ShowFlax))]
        public int FlaxCeiling { get => this.CeilingOf("Flax"); set => this.SetFromRow("Flax", value); }
        [SyncToView] public bool ShowFlax() => this.IsShown("Flax");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Heliconia"), VisibilityParam(nameof(ShowHeliconia))]
        public int HeliconiaCeiling { get => this.CeilingOf("Heliconia"); set => this.SetFromRow("Heliconia", value); }
        [SyncToView] public bool ShowHeliconia() => this.IsShown("Heliconia");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Huckleberries"), VisibilityParam(nameof(ShowHuckleberry))]
        public int HuckleberryCeiling { get => this.CeilingOf("Huckleberry"); set => this.SetFromRow("Huckleberry", value); }
        [SyncToView] public bool ShowHuckleberry() => this.IsShown("Huckleberry");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Jointfir"), VisibilityParam(nameof(ShowJointfir))]
        public int JointfirCeiling { get => this.CeilingOf("Jointfir"); set => this.SetFromRow("Jointfir", value); }
        [SyncToView] public bool ShowJointfir() => this.IsShown("Jointfir");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Kelp"), VisibilityParam(nameof(ShowKelp))]
        public int KelpCeiling { get => this.CeilingOf("Kelp"); set => this.SetFromRow("Kelp", value); }
        [SyncToView] public bool ShowKelp() => this.IsShown("Kelp");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("King Fern"), VisibilityParam(nameof(ShowKingFern))]
        public int KingFernCeiling { get => this.CeilingOf("KingFern"); set => this.SetFromRow("KingFern", value); }
        [SyncToView] public bool ShowKingFern() => this.IsShown("KingFern");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Lattice Mushroom"), VisibilityParam(nameof(ShowLatticeMushroom))]
        public int LatticeMushroomCeiling { get => this.CeilingOf("LatticeMushroom"); set => this.SetFromRow("LatticeMushroom", value); }
        [SyncToView] public bool ShowLatticeMushroom() => this.IsShown("LatticeMushroom");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Lupine"), VisibilityParam(nameof(ShowLupine))]
        public int LupineCeiling { get => this.CeilingOf("Lupine"); set => this.SetFromRow("Lupine", value); }
        [SyncToView] public bool ShowLupine() => this.IsShown("Lupine");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Ocean Spray"), VisibilityParam(nameof(ShowOceanSpray))]
        public int OceanSprayCeiling { get => this.CeilingOf("OceanSpray"); set => this.SetFromRow("OceanSpray", value); }
        [SyncToView] public bool ShowOceanSpray() => this.IsShown("OceanSpray");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Orchid"), VisibilityParam(nameof(ShowOrchid))]
        public int OrchidCeiling { get => this.CeilingOf("Orchid"); set => this.SetFromRow("Orchid", value); }
        [SyncToView] public bool ShowOrchid() => this.IsShown("Orchid");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Papaya"), VisibilityParam(nameof(ShowPapaya))]
        public int PapayaCeiling { get => this.CeilingOf("Papaya"); set => this.SetFromRow("Papaya", value); }
        [SyncToView] public bool ShowPapaya() => this.IsShown("Papaya");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Pineapple"), VisibilityParam(nameof(ShowPineapple))]
        public int PineappleCeiling { get => this.CeilingOf("Pineapple"); set => this.SetFromRow("Pineapple", value); }
        [SyncToView] public bool ShowPineapple() => this.IsShown("Pineapple");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Prickly Pear Fruit"), VisibilityParam(nameof(ShowPricklyPear))]
        public int PricklyPearCeiling { get => this.CeilingOf("PricklyPear"); set => this.SetFromRow("PricklyPear", value); }
        [SyncToView] public bool ShowPricklyPear() => this.IsShown("PricklyPear");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Pumpkin"), VisibilityParam(nameof(ShowPumpkin))]
        public int PumpkinCeiling { get => this.CeilingOf("Pumpkin"); set => this.SetFromRow("Pumpkin", value); }
        [SyncToView] public bool ShowPumpkin() => this.IsShown("Pumpkin");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Rice"), VisibilityParam(nameof(ShowRice))]
        public int RiceCeiling { get => this.CeilingOf("Rice"); set => this.SetFromRow("Rice", value); }
        [SyncToView] public bool ShowRice() => this.IsShown("Rice");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Rose Bush"), VisibilityParam(nameof(ShowRoseBush))]
        public int RoseBushCeiling { get => this.CeilingOf("RoseBush"); set => this.SetFromRow("RoseBush", value); }
        [SyncToView] public bool ShowRoseBush() => this.IsShown("RoseBush");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Saxifrage"), VisibilityParam(nameof(ShowSaxifrage))]
        public int SaxifrageCeiling { get => this.CeilingOf("Saxifrage"); set => this.SetFromRow("Saxifrage", value); }
        [SyncToView] public bool ShowSaxifrage() => this.IsShown("Saxifrage");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Seagrass"), VisibilityParam(nameof(ShowSeagrass))]
        public int SeagrassCeiling { get => this.CeilingOf("Seagrass"); set => this.SetFromRow("Seagrass", value); }
        [SyncToView] public bool ShowSeagrass() => this.IsShown("Seagrass");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Sunflower"), VisibilityParam(nameof(ShowSunflower))]
        public int SunflowerCeiling { get => this.CeilingOf("Sunflower"); set => this.SetFromRow("Sunflower", value); }
        [SyncToView] public bool ShowSunflower() => this.IsShown("Sunflower");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Taro Root"), VisibilityParam(nameof(ShowTaro))]
        public int TaroCeiling { get => this.CeilingOf("Taro"); set => this.SetFromRow("Taro", value); }
        [SyncToView] public bool ShowTaro() => this.IsShown("Taro");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Tomato"), VisibilityParam(nameof(ShowTomatoes))]
        public int TomatoesCeiling { get => this.CeilingOf("Tomatoes"); set => this.SetFromRow("Tomatoes", value); }
        [SyncToView] public bool ShowTomatoes() => this.IsShown("Tomatoes");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Trillium Flower"), VisibilityParam(nameof(ShowTrillium))]
        public int TrilliumCeiling { get => this.CeilingOf("Trillium"); set => this.SetFromRow("Trillium", value); }
        [SyncToView] public bool ShowTrillium() => this.IsShown("Trillium");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Tulip"), VisibilityParam(nameof(ShowTulip))]
        public int TulipCeiling { get => this.CeilingOf("Tulip"); set => this.SetFromRow("Tulip", value); }
        [SyncToView] public bool ShowTulip() => this.IsShown("Tulip");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Waterweed"), VisibilityParam(nameof(ShowWaterweed))]
        public int WaterweedCeiling { get => this.CeilingOf("Waterweed"); set => this.SetFromRow("Waterweed", value); }
        [SyncToView] public bool ShowWaterweed() => this.IsShown("Waterweed");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("Wheat"), VisibilityParam(nameof(ShowWheat))]
        public int WheatCeiling { get => this.CeilingOf("Wheat"); set => this.SetFromRow("Wheat", value); }
        [SyncToView] public bool ShowWheat() => this.IsShown("Wheat");

        [Eco(RequiredAccess = AccessType.FullAccess, Serialized = false), UITypeName("Int32"), LocDisplayName("White Bursage"), VisibilityParam(nameof(ShowWhiteBursage))]
        public int WhiteBursageCeiling { get => this.CeilingOf("WhiteBursage"); set => this.SetFromRow("WhiteBursage", value); }
        [SyncToView] public bool ShowWhiteBursage() => this.IsShown("WhiteBursage");

        /// <summary>
        /// Names the command for crops that have no row -- ones another mod added. Hidden
        /// when every crop in this world has a row, which is the vanilla case.
        /// </summary>
        [SyncToView, Autogen, UITypeName("StringDisplay"), VisibilityParam(nameof(HasOverflow))]
        public string OverflowNote { get; private set; } = string.Empty;

        [SyncToView] public bool HasOverflow() => this.OverflowNote.Length > 0;

        /// <summary>Returns every crop's ceiling to zero, so every crop is harvested without limit.</summary>
        [RPC(AccessType.FullAccess), Autogen, UITypeName("BigButton"), Description("Clear All")]
        public void ClearAll(Player player)
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

        /// <summary>
        /// Driven by the dock's one-second tick. Re-pushes a row's visibility when the picker
        /// changed what is shown, and a row's value when its ceiling changed some other way
        /// (Clear All, the chat command, another citizen's edit).
        /// </summary>
        public void RefreshAll()
        {
            if (!this.ready) return;
            if (this.Parent is not DroneDockObject dock) return;

            // Swapped in before any Changed() fires, so a visibility getter read during the
            // push already sees the new set.
            var previous = this.lastShown;
            this.lastShown = this.ShownKeys();
            foreach (var key in RowKeys)
            {
                if (this.lastShown.Contains(key) != previous.Contains(key))
                    this.Changed("Show" + key);

                var value = dock.CropCeilingFor(key);
                if (!this.lastValues.TryGetValue(key, out var last) || last != value)
                {
                    this.lastValues[key] = value;
                    this.Changed(key + "Ceiling");
                }
            }

            // The catalog and the row set are both fixed for the life of the server, so the
            // note is worked out once.
            if (!this.overflowResolved)
            {
                this.overflowResolved = true;
                var overflow = CropCatalog.All.Where(c => !RowKeys.Contains(c.Key)).Select(c => c.DisplayName).ToList();
                if (overflow.Count > 0)
                {
                    this.OverflowNote = $"No row here for {string.Join(", ", overflow)}. Set those with /drone ceiling <crop>, <amount>.";
                    this.Changed(nameof(this.OverflowNote));
                    this.Changed(nameof(this.HasOverflow));
                }
            }
        }

        private int CeilingOf(string key) =>
            this.Parent is DroneDockObject dock ? dock.CropCeilingFor(key) : 0;

        /// <summary>A row's write. Full access was already enforced by Eco (see the class remarks).</summary>
        private void SetFromRow(string key, int value)
        {
            if (!this.ready) return;
            if (this.Parent is not DroneDockObject dock) return;

            dock.WriteCropCeilingFromTab(key, value);
            this.lastValues[key] = dock.CropCeilingFor(key);
            this.Changed(key + "Ceiling");
        }

        /// <summary>
        /// A row shows when its species exists in this world and the picker is empty or names
        /// that species' harvested item.
        /// </summary>
        private bool IsShown(string key) => this.lastShown.Contains(key);

        private HashSet<string> ShownKeys()
        {
            var rows = CropCatalog.All.Where(c => RowKeys.Contains(c.Key));

            var picked = this.Crop?.GetTypes()?.ToList();
            if (picked != null && picked.Count > 0)
            {
                var pickedTypes = new HashSet<Type>(picked);
                rows = rows.Where(c => c.Produce != null && pickedTypes.Contains(c.Produce.Type));
            }

            return new HashSet<string>(rows.Select(c => c.Key), StringComparer.Ordinal);
        }
    }
}
