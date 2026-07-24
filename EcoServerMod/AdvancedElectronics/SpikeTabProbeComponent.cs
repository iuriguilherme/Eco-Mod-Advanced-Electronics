using System.ComponentModel;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    // =====================================================================
    // U1 SPIKE PROBE — TEMPORARY, DELETE AFTER LIVE BATCH L1.
    //
    // REVISION after L1 attempt #1: the first probe included a
    // [SyncToView] IEnumerable<string> member. That crashed the CLIENT
    // ("Cannot convert value: Q4 row A ... String to type ... View" ->
    // "Failed to receive views" -> disconnect): a synced collection's
    // elements must be View types, so a list of plain strings (or of any
    // mod type with no generated view) is impossible. That member is
    // removed. FINDING RECORDED: no list-shaped readout of plain/mod values.
    //
    // This revision isolates the remaining question with ONLY type-valid,
    // stock-proven member shapes copied verbatim from the vanilla
    // AreaBonusComponent tab (LocString via StringTitle, a plain int, a
    // BigButton RPC): does a MOD-defined component's tab render AT ALL?
    //
    // The attempt #1 log also showed "Can't convert named type to system
    // type: LocStringView" — the client failing to resolve the view type
    // for content nested inside this mod component's (ungenerated) view.
    // If this clean probe still fails to render, mod component tabs are not
    // viable and the readout/management interface must move to a surface
    // the client can render (world-space text via the prefab's own bundle
    // script, a stock component tab, or chat) — a design pivot for the user.
    //
    // Observe (open the placed Drone Dock, look for a "Survey Probe" tab):
    //   Q1  Does the "Survey Probe" tab appear?
    //   Q2  Does the StringTitle line render its text?
    //   Q3  Does "Run Probe" render, and does clicking it update the text +
    //       the click count + print the [SPIKE] chat line?
    // No collection member this time, so a failure here is the component
    // tab itself, not a poisoned field.
    // =====================================================================
    [Serialized, CreateComponentTabLoc("Survey Probe", true), HasIcon]
    public class SpikeTabProbeComponent : WorldObjectComponent, INotifyPropertyChanged
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // Exact shape of AreaBonusComponent.Title (stock, known to render).
        [SyncToView, Autogen, UITypeName("StringTitle")]
        public LocString ProbeText { get; set; } =
            Localizer.DoStr("Q2: if you can read this in the Survey Probe tab, a mod component tab renders text.");

        // Exact shape of AreaBonusComponent.InvestedStars (stock plain int).
        [SyncToView] public int ClickCount { get; private set; }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"),
         Description("Run Probe — tests whether a mod tab BUTTON renders and fires.")]
        public void RunProbe(Player player)
        {
            this.ClickCount++;
            this.ProbeText = Localizer.DoStr($"Q3: button works — fired {this.ClickCount} time(s).");
            this.Changed(nameof(this.ProbeText));
            this.Changed(nameof(this.ClickCount));
            player.MsgLocStr($"[SPIKE] RunProbe fired, ClickCount={this.ClickCount}");
        }
    }
}
