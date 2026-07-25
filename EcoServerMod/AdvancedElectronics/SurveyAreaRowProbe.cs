using System.Collections.Generic;
using System.ComponentModel;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    // =====================================================================
    // ISOLATED UI PROBE — TEMPORARY, DELETE AFTER THIS TEST.
    //
    // Question: can a mod-defined IController type render in a [SyncToView]
    // collection (the RealEstateDesk "My Deeds" list pattern), or does it
    // crash the client the way a [SyncToView] IEnumerable<string> did?
    //
    // This is attached to the SurveyDroneObject, NOT the dock, so that if it
    // crashes on view reception it only affects the DRONE's window — open the
    // drone to run the probe; the dock stays safe either way. If the drone's
    // window shows an "Area Probe" tab listing the three rows below, a
    // mod-controller list renders and the RealEstateDesk-style UI is
    // achievable (areas would move to a registry of such controllers). If the
    // tab is blank or opening the drone disconnects, it is not.
    // =====================================================================

    /// <summary>One probe row: a minimal mod-defined controller with synced display fields.</summary>
    [Serialized]
    public sealed class SurveyAreaRow : IController, INotifyPropertyChanged
    {
        [SyncToView] public string Name { get; set; }
        [SyncToView] public string Info { get; set; }

        int controllerID;
        public ref int ControllerID => ref this.controllerID;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Probe component on the drone: a synced collection of mod controllers plus a button so
    /// the tab registers. Populated with dummy rows in <see cref="Initialize"/>.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Area Probe", true), HasIcon]
    public class SurveyAreaProbeComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>The RealEstateDesk-style test: a synced list of a mod controller type.</summary>
        [SyncToView] public IEnumerable<SurveyAreaRow> Rows { get; private set; } = new List<SurveyAreaRow>();

        public override void Initialize()
        {
            base.Initialize();
            this.Rows = new List<SurveyAreaRow>
            {
                new SurveyAreaRow { Name = "Probe Row 1", Info = "3 plots" },
                new SurveyAreaRow { Name = "Probe Row 2", Info = "8 plots" },
                new SurveyAreaRow { Name = "Probe Row 3", Info = "5 plots" },
            };
            this.Changed(nameof(this.Rows));
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Refresh probe rows")]
        public void RefreshProbe(Player player) => this.Changed(nameof(this.Rows));
    }
}
