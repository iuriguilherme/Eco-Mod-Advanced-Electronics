using System.Collections.Generic;
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
    // This component exists ONLY to answer one question a live restart can
    // settle and offline source-reading cannot: does a MOD-defined
    // WorldObjectComponent's tab render its content on the stock client?
    // The whole dock-survey interface (Survey Areas tab, Survey Results tab)
    // lives or dies on this.
    //
    // It attaches to DroneDockObject via a TEMPORARY [RequireComponent] line
    // in DroneDock.cs (also marked "U1 SPIKE"). There is no other openable
    // object to test on — a fresh spike object would need a Unity client
    // prefab this repo cannot build offline, so the shipping dock is the only
    // surface. Both this file and that one line are removed once L1 answers
    // the question. It ships no gameplay behaviour.
    //
    // What to observe in-game (open the placed Drone Dock, look for a
    // "Survey Probe" tab):
    //   Q1  Does a "Survey Probe" tab appear at all?                 (tab declaration)
    //   Q2  Does the StringTitle text render inside it?              (mod tab TEXT)
    //   Q3  Does the "Run Probe" button render, and does clicking it
    //       change the text + print the [SPIKE] chat line?           (mod tab BUTTON + action)
    //   Q4  Does the string list render as rows?                     (stock-typed collection)
    // A "yes" to Q2 means the interface can be built as text; a "yes" to Q3
    // means create/assign/delete can be in-tab buttons rather than chat.
    // =====================================================================
    [Serialized, CreateComponentTabLoc("Survey Probe", true), HasIcon]
    public class SpikeTabProbeComponent : WorldObjectComponent, INotifyPropertyChanged
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public LocString ProbeText { get; set; } =
            Localizer.DoStr("Q2: if you can read this line in a tab, mod-defined tab TEXT renders.");

        [SyncToView] public int ClickCount { get; private set; }

        // Q4: a collection of a STOCK element type (string). If these render as
        // rows, a text-row readout is viable without any generated per-type view.
        [SyncToView, Autogen, UITypeName("GeneralHeader")]
        public IEnumerable<string> ProbeRows { get; private set; } = new List<string>
        {
            "Q4 row A",
            "Q4 row B",
            "Q4 row C",
        };

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"),
         Description("Run Probe — tests whether a mod tab BUTTON renders and fires an action.")]
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
