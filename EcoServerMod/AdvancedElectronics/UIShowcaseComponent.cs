using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
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

        // --- v7: the ViewMapper escape hatch (SAFE -- cannot crash). ---
        //
        // A mod component has no compiled client view class, so the client rebuilds each member's
        // type from a NAME through View.cs:100-129. That switch is the real ceiling on what data
        // can cross to a mod tab: bool/int/float/string/Enum/Range/Color/Vector3i/void, plus
        // List<View> and Dictionary. Past it there are two fallbacks, and the second is untested:
        // View.cs:124-126 resolves ANY name registered in ViewMapper.
        //
        // A scalar Type member serializes as "ViewClassInfo" (TypeGenerationHelper.cs:62). If
        // ViewMapper knows that name, an item TYPE can be displayed directly -- icon and localised
        // name for free, instead of hand-formatting the item name into a text block.
        //
        // ANSWERED 2026-07-27 v7, and the answer is NO -- both members removed.
        //
        // A scalar Type member does not reach a mod tab. The client names it "TypeView", and
        // ViewMapper has no entry for that, so View.cs:128 logs
        //
        //     View errors: Can't convert named type to system type: TypeView
        //
        // and View.cs:356-357 returns without ever setting the value. Note this corrects an
        // earlier reading of mine: the member's NAME is "TypeView", not "ViewClassInfo".
        // TypeGenerationHelper.cs:62 (which does say ViewClassInfo) is a different function from
        // the one that names members -- ControllerMarshalerService.GetViewTypeName, which is
        // simply <TypeName> + "View". The list-element crash proves VALUES serialize as
        // ViewClassInfo objects; the member NAME is a separate thing, and I had conflated them.
        //
        // Worse than blank, it renders a trap: autogen still builds an AutoGenSelector row for
        // the member, showing "None.". Clicking its chevron passes the null type into
        // SelectorPopupUI.InitFlexible, which throws NullReferenceException in
        // ViewClassInfo.DerivesType and strands the popup's search box at screen origin. The
        // client survives, but the UI is left visibly broken.
        //
        // So the ViewMapper fallback at View.cs:124-126 is NOT a general escape hatch for
        // arbitrary registered types -- at least not for Type. Displaying an item needs its
        // localised name composed into a text block, as the shipping code already does.

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
    /// TEMPORARY PROBE v7 -- the container question, reopened after reading the client source.
    ///
    /// v5/v6 concluded "containers are closed to mods" because IEnumerable&lt;Type&gt; crashed with
    /// BOTH mod and vanilla element types. That inference was wrong: IronOreItem and
    /// SurveyDroneItem are both Type VALUES, so neither run varied the thing that decides it.
    ///
    /// The client log gives the mechanism (Player.log, not the server's Logs/):
    ///
    ///     InvalidCastException: Unable to cast object of type 'ViewClassInfo' to type 'View'.
    ///       at Eco.Shared.Utils.ListExtensions.FromBson[T](IList`1[T], BSONArray)
    ///
    /// Chain, all read at the tree:
    ///   ControllerMarshalerService.cs:451-455  every IEnumerable member is typed
    ///                                          "IEnumerableView"; element type is carried in a
    ///                                          SEPARATE listTypeName field.
    ///   TypeGenerationHelper.cs:62             a Type element generates as ViewClassInfo.
    ///   View.cs:337-343                        VANILLA path: a compiled, code-generated view
    ///                                          class supplies the true List&lt;ViewClassInfo&gt;.
    ///   View.cs:345-359                        MOD path: no compiled class, so the type is
    ///                                          rebuilt from the name string.
    ///   View.cs:114                            "IEnumerableView" =&gt; typeof(List&lt;View&gt;),
    ///                                          DISCARDING listTypeName. ViewClassInfo is not a
    ///                                          View, so the cast throws.
    ///
    /// So the rule is not "no containers", it is: A MOD LIST'S ELEMENTS MUST DESERIALIZE TO View.
    /// Type does not. Neither does string -- which is why the old IEnumerable&lt;string&gt; crash
    /// named Eco.Shared.View.View as its target type too. Same line of client code, twice.
    ///
    /// This probe finally varies the right axis: elements that ARE controllers with client views.
    /// Deed is the vanilla precedent (DeedManagementComponent.cs:19 syncs IEnumerable&lt;Deed&gt;)
    /// and WorldObject is the one every mod already has a handle on via this.Parent.
    ///
    /// If either renders, Table and ButtonGrid come back and the six-button assign pool dies.
    ///
    /// v7 quarantined this on the drone. That was wasted: a drone window renders no tabs at all,
    /// so the probe never ran -- no render, no log line, no crash. Absence of a crash there was
    /// absence of the component, not evidence about containers. v8 puts it back on the dock,
    /// which is the only object in this mod that reliably shows a mod tab.
    ///
    /// Read ContainerProbeNote FIRST. It is a plain StringDisplay, a template already proven to
    /// work, declared before either list. If the tab shows the note but no lists, the lists
    /// failed. If the tab is empty or missing entirely, the component never attached and the
    /// lists are still untested -- which is exactly the ambiguity v7 fell into.
    /// </summary>
    [Serialized, CreateComponentTabLoc("UI Containers", true), HasIcon]
    public class UIContainerProbeComponent : WorldObjectComponent
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ContainerProbeNote { get; private set; } =
            "v7: elements are controllers (Deed, WorldObject), not Type. If these render, " +
            "the client's List<View> demand is satisfiable from a mod and containers are open.";

        /// <summary>
        /// The vanilla precedent, copied to a mod component. Capped at three so a populated world
        /// does not turn the probe into a wall of deeds.
        /// </summary>
        [Autogen, SyncToView, UIListTypeName("Table")]
        public IEnumerable<Deed> DeedTableProbe => PropertyManager.GetAllDeeds().Take(3);

        /// <summary>
        /// Same question via a handle every mod component already has. If Deed renders and this
        /// does not, the difference is the element type's own view, not the container.
        /// </summary>
        [Autogen, SyncToView, UIListTypeName("ButtonGrid")]
        public IEnumerable<WorldObject> ObjectGridProbe => new[] { this.Parent };

        public override void Initialize()
        {
            base.Initialize();
            this.Changed(nameof(this.ContainerProbeNote));
        }
    }
}
