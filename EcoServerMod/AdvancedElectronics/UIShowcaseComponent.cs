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
    /// <summary>
    /// TEMPORARY PROBE -- delete once the layout brainstorm is done.
    ///
    /// The client picks an autogen prefab PER PROPERTY, BY NAME, from PrefabsCollection.viewUIs
    /// (68 entries; see docs/ideation/2026-07-27-mod-ui-vocabulary.md). This project shipped using
    /// four of them. This component renders one property per candidate name so the whole
    /// vocabulary can be SEEN in one screenshot instead of guessed at one restart per name --
    /// the batched-probe discipline from docs/solutions/workflow-issues/eco-mod-batched-live-testing.md.
    ///
    /// Each property is named after the template it requests, so the rendered label doubles as the
    /// legend: whatever appears next to "String Plaque Probe" is what UITypeName("StringPlaque")
    /// draws. Autogen inserts spaces between capitals, so no separate caption is needed.
    ///
    /// Everything here is a GUESS about type binding. Names are verified to exist in the client's
    /// set; which C# type each template expects is not documented anywhere we can read. A template
    /// given the wrong type may render blank (harmless) -- or throw on view reception and drop the
    /// client's connection, which is why the risky container templates are quarantined in the
    /// sibling UILayoutProbeComponent rather than sitting in here.
    /// </summary>
    [Serialized, CreateComponentTabLoc("UI Showcase", true), HasIcon]
    public class UIShowcaseComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // --- Text and heading variants (lowest risk: all string-typed, and two of them are
        //     already proven in production, which makes them the control group) ---

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string StringTitleProbe { get; private set; } = "StringTitle -- proven, control";

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string StringDisplayProbe { get; private set; } = "StringDisplay -- proven, control";

        [SyncToView, Autogen, UITypeName("StringDescription")]
        public string StringDescriptionProbe { get; private set; } = "StringDescription -- untested";

        [SyncToView, Autogen, UITypeName("LongString")]
        public string LongStringProbe { get; private set; } =
            "LongString -- untested. This sentence is deliberately long so wrapping, truncation and " +
            "vertical growth are visible in the screenshot; a findings readout would use this shape.";

        [SyncToView, Autogen, UITypeName("StringPlaque")]
        public string StringPlaqueProbe { get; private set; } = "StringPlaque -- untested";

        [SyncToView, Autogen, UITypeName("SectionHeader")]
        public string SectionHeaderProbe { get; private set; } = "SectionHeader -- untested";

        [SyncToView, Autogen, UITypeName("LinedHeader")]
        public string LinedHeaderProbe { get; private set; } = "LinedHeader -- untested";

        [SyncToView, Autogen, UITypeName("GeneralHeader")]
        public string GeneralHeaderProbe { get; private set; } = "GeneralHeader -- untested";

        // --- Numeric and boolean templates. Type binding is the guess here: Int32/Single are
        //     named after CLR types so the mapping is probably literal; NestedMeter and Range are
        //     assumed to want a float, and the value is set mid-scale so a filled bar is
        //     distinguishable from an empty or full one at a glance. ---

        [SyncToView, Autogen, UITypeName("Boolean")]
        public bool BooleanProbe { get; private set; } = true;

        [SyncToView, Autogen, UITypeName("Int32")]
        public int Int32Probe { get; private set; } = 42;

        [SyncToView, Autogen, UITypeName("Single")]
        public float SingleProbe { get; private set; } = 3.5f;

        [SyncToView, Autogen, UITypeName("Range")]
        public float RangeProbe { get; private set; } = 0.6f;

        [SyncToView, Autogen, UITypeName("NestedMeter")]
        public float NestedMeterProbe { get; private set; } = 0.6f;

        public override void Initialize()
        {
            base.Initialize();

            // Autogen members render from a value that was actually assigned and pushed; a
            // never-assigned member draws blank regardless of template (the trap that cost this
            // project a cycle when the first tab shipped). Field initializers above set the
            // values; this pushes them so the client has something to draw on first open.
            this.Changed(nameof(this.StringTitleProbe));
            this.Changed(nameof(this.StringDisplayProbe));
            this.Changed(nameof(this.StringDescriptionProbe));
            this.Changed(nameof(this.LongStringProbe));
            this.Changed(nameof(this.StringPlaqueProbe));
            this.Changed(nameof(this.SectionHeaderProbe));
            this.Changed(nameof(this.LinedHeaderProbe));
            this.Changed(nameof(this.GeneralHeaderProbe));
            this.Changed(nameof(this.BooleanProbe));
            this.Changed(nameof(this.Int32Probe));
            this.Changed(nameof(this.SingleProbe));
            this.Changed(nameof(this.RangeProbe));
            this.Changed(nameof(this.NestedMeterProbe));
        }
    }

    /// <summary>
    /// TEMPORARY PROBE -- the quarantined half. Delete with its sibling.
    ///
    /// These are the templates that answer the standing complaint that a stacked column of
    /// full-width buttons "is not a smartphone app" -- and they are also the ones most likely to
    /// misbehave, because they are CONTAINERS. A container template handed a plain scalar may
    /// render blank, or may throw on view reception and drop the client.
    ///
    /// They live in their own component, and therefore their own tab, so that failure is one
    /// bisection step: if the dock window dies, delete this class and the Showcase tab still
    /// answers thirteen questions. Splitting the risk is the whole reason for the second file's
    /// worth of ceremony.
    /// </summary>
    [Serialized, CreateComponentTabLoc("UI Layout", true), HasIcon]
    public class UILayoutProbeComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string LayoutProbeNote { get; private set; } =
            "Container templates below. Blank = wrong type binding, not a missing template.";

        [SyncToView, Autogen, UITypeName("HorzBox")]
        public string HorzBoxProbe { get; private set; } = "HorzBox";

        [SyncToView, Autogen, UITypeName("ButtonGrid")]
        public string ButtonGridProbe { get; private set; } = "ButtonGrid";

        // Two buttons under the container probes: if HorzBox or ButtonGrid turns out to affect
        // sibling layout rather than needing a bound collection, these are what would sit side by
        // side, and the screenshot shows it immediately.
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Probe Button A")]
        public void ProbeButtonA(Player player) { }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Probe Button B")]
        public void ProbeButtonB(Player player) { }

        public override void Initialize()
        {
            base.Initialize();
            this.Changed(nameof(this.LayoutProbeNote));
            this.Changed(nameof(this.HorzBoxProbe));
            this.Changed(nameof(this.ButtonGridProbe));
        }
    }
}
