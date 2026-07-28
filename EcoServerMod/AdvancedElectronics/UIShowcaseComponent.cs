using System;
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
    /// <summary>
    /// TEMPORARY PROBE v2 -- delete once the layout brainstorm is done.
    ///
    /// v1 answered the display half: 12 of 13 templates rendered, and they render as
    /// TWO-COLUMN rows (label left, control right), not a single stacked column. It also
    /// failed usefully -- clicking a stepper killed the server with
    /// "Missing RPC call SetRangeProbe for UIShowcaseComponent", which is the contract:
    /// an EDITABLE template round-trips through a setter the framework must be able to reach.
    ///
    /// v2 tests two corrections, each grounded in a real usage rather than a guess:
    ///
    /// 1. Editable scalars declared [Eco] with a PUBLIC setter instead of
    ///    [SyncToView, Autogen] + private setter. [Eco] is the attribute the working
    ///    MaterialTargets picker already uses, and it is what generates the write path.
    /// 2. Containers driven by UIListTypeName on a COLLECTION, not UITypeName on a scalar --
    ///    see the sibling component below.
    /// </summary>
    [Serialized, CreateComponentTabLoc("UI Showcase", true), HasIcon]
    public class UIShowcaseComponent : WorldObjectComponent, INotifyPropertyChanged
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>
        /// v3: [Eco] stopped the click from crashing, but the edited value never stuck -- the
        /// stepper moved and the number did not. Vanilla components carrying editable [Eco]
        /// members implement INotifyPropertyChanged (PerformCivicActionComponent does), which is
        /// the missing half: the write lands, but without a change notification nothing tells the
        /// view to re-read it. Pairing this with [Serialized] on the members below is the
        /// hypothesis under test.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        // --- Controls: proven in v1, kept so a regression is obvious at a glance ---

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string StringTitleProbe { get; private set; } = "PROBE v2 -- editable scalars below";

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string StringDisplayProbe { get; private set; } =
            "v1 crashed on click: missing Set<Prop>. These use [Eco] + public setter instead.";

        // --- The v2 experiment: editable scalars via [Eco]. If clicking a stepper no longer
        //     drops the server, the contract is "editable template needs a reachable setter",
        //     and every interactive template in the 68 opens up. ---

        [Serialized, Eco, UITypeName("Boolean")]
        public bool BooleanProbe { get; set; } = true;

        [Serialized, Eco, Range(0, 100), UITypeName("Int32")]
        public int Int32Probe { get; set; } = 42;

        [Serialized, Eco, Range(0, 10), UITypeName("Single")]
        public float SingleProbe { get; set; } = 3.5f;

        // --- Display templates worth a second look for the real UI. LongString earned its
        //     place in v1: a labelled multi-line box with its OWN scrollbar, which is the
        //     findings readout solved (fixed height, scrolls internally, panel stays put). ---

        // LongString turned out to be an EDITABLE template, not a display one -- it renders a text
        // box the player can type into, and typing into it with a private setter crashed the
        // client exactly as the Range stepper did in v1. Same contract, second confirmation.
        // Declared editable here so it survives being typed in. If the survey findings want a
        // read-only scrolling box, that is a different template (StringDescription or a
        // Deprecated/HeaderList variant) -- this one always accepts input.
        [Serialized, Eco, UITypeName("LongString")]
        public string LongStringProbe { get; set; } =
            "LongString is EDITABLE -- type in it. v2 crashed here because the setter was private.";

        [SyncToView, Autogen, UITypeName("SectionHeader")]
        public string SectionHeaderProbe { get; private set; } = "SectionHeader -- grouping";

        public override void Initialize()
        {
            base.Initialize();

            // Only the [SyncToView, Autogen] members need an explicit push; [Eco] members are
            // handled by the framework's own change tracking.
            this.Changed(nameof(this.StringTitleProbe));
            this.Changed(nameof(this.StringDisplayProbe));
            this.Changed(nameof(this.LongStringProbe));
            this.Changed(nameof(this.SectionHeaderProbe));
        }
    }

    /// <summary>
    /// TEMPORARY PROBE v2 -- the container half, still quarantined in its own tab so a failure
    /// costs only this tab.
    ///
    /// v1 put UITypeName("ButtonGrid") on a string and got a blank tab that also swallowed the
    /// two buttons below it. The mistake was the attribute AND the type: containers are declared
    /// with UIListTypeName on a collection. Vanilla drives a whole grid of civic-action buttons
    /// from IEnumerable&lt;Type&gt; that way -- see PerformCivicActionComponent.cs:41-42, which is
    /// also a COMPUTED GETTER, so collections do not follow the "must be settable and assigned"
    /// rule that scalars do.
    ///
    /// Elements are Type because the client already has a view for it; a mod-defined element type
    /// does not, which is what made the old IEnumerable&lt;string&gt; attempt crash.
    ///
    /// If ButtonGrid renders here, the six-button assign cap dies: the areas list becomes a
    /// generated grid instead of a hand-written pool of RPC methods.
    /// </summary>
    [Serialized, CreateComponentTabLoc("UI Layout", true), HasIcon]
    public class UILayoutProbeComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string LayoutProbeNote { get; private set; } =
            "v2: UIListTypeName on IEnumerable<Type>, per PerformCivicActionComponent.";

        /// <summary>A grid of buttons generated from a collection.</summary>
        [Autogen, SyncToView, UIListTypeName("ButtonGrid")]
        public IEnumerable<Type> ButtonGridProbe => new[]
        {
            typeof(SurveyDroneItem),
            typeof(DroneDockItem),
        };

        /// <summary>Same collection as a table, to compare the two list templates side by side.</summary>
        [Autogen, SyncToView, UIListTypeName("Table")]
        public IEnumerable<Type> TableProbe => new[]
        {
            typeof(SurveyDroneItem),
            typeof(DroneDockItem),
        };

        public override void Initialize()
        {
            base.Initialize();
            this.Changed(nameof(this.LayoutProbeNote));
        }
    }
}
