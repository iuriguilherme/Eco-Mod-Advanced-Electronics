using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Gameplay.Utils;
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
    public class UIContainerProbeComponent : WorldObjectComponent, IHasClientControlledContainers
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // ALWAYS name the build here. v9 rendered identically to v8 and this note still read
        // "v7", so the screenshot could not confirm which DLL was loaded -- that had to be dug
        // out of the server log. A probe should identify itself on screen.
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ContainerProbeNote { get; private set; } =
            "v11: rows now name their own UI via UITypeName (Selector for deeds, HorzBox for mod " +
            "rows) alongside UIListTypeName for the container. 2 deeds + 2 mod rows are seeded.";

        /// <summary>
        /// v8 CONTROL, kept deliberately. Result: renders a header and NO rows, with no error
        /// logged anywhere. This is the [SyncToView] IEnumerable shape, and blank-with-no-error
        /// is what it does -- for a vanilla element type as much as a mod one.
        /// </summary>
        [Autogen, SyncToView, UIListTypeName("Table")]
        public IEnumerable<Deed> DeedTableProbe => PropertyManager.GetAllDeeds().Take(3);

        /// <summary>
        /// v8 CONTROL. One element (this.Parent), rendered nothing at all. Kept so v9 can tell a
        /// changed result from a changed world.
        /// </summary>
        [Autogen, SyncToView, UIListTypeName("ButtonGrid")]
        public IEnumerable<WorldObject> ObjectGridProbe => new[] { this.Parent };

        // --- v9: the shape vanilla actually uses for lists. ---
        //
        // There are only five UIListTypeName usages in the whole game and FOUR are this one:
        //
        //     [Eco, AllowEmpty, UIListTypeName("IEnumerableHeader"), AllowCopyPaste]
        //     public ControllerList<GameValue<bool>> List { get; set; }
        //
        // ControllerList<T> : ThreadSafeList<T>, IClientControlledList, declared [Eco] rather than
        // [SyncToView], and constructed with a back-reference in the owner's constructor
        // (LawSection.cs:70-73). The [SyncToView] IEnumerable form I copied in v7/v8 is the
        // OUTLIER, 1 of 5 -- and it is the one that renders blank.
        //
        // Two elements, to separate two questions in one restart:
        //   DeedControllerList -- vanilla element with a known client view. Isolates the
        //                         CONTAINER shape: if this fills, ControllerList is the answer.
        //   ModRowList         -- mod-defined element. Isolates the ELEMENT: if the vanilla one
        //                         fills and this does not, mod row types are the wall, and that
        //                         is the real ceiling on a rich per-row list of our own data.

        // v10 adds the piece v9 was missing. Every vanilla owner of a ControllerList implements
        // IHasClientControlledContainers, and the interface's own comment says why:
        //
        //   "If any class has lists that need to be managed by RPCs from the client, add this
        //    interface and it will automatically get all those RPCs it needs."
        //    -- ControllerListExtensions.cs:35-37
        //
        // It is declared [ForceCreateView] and every member is a DEFAULT interface method, so
        // implementing it costs exactly one identifier in the class declaration and no bodies.
        // v9's lists were declared correctly but had no client-side RPCs to manage them, which
        // is consistent with what was observed: they synced without error and rendered nothing.
        // v11: name the ROW UI as well as the container.
        //
        // From SLG's own wiki (Eco.wiki/UI-System.md:9):
        //
        //   "The [UIListTypeName] names the game object to make the list from. For non-lists
        //    (OR THE UI USED ON EACH LIST ELEMENT) you can tag [UITypeName]."
        //
        // Two tags, two jobs: UIListTypeName is the container, UITypeName is each row. The client
        // agrees at AutoGenUIPicker.cs:31-32, where the row lookup (inList == true) reads
        // prop.UITypeName. I read that line hours ago and parsed "(!prop.IsList || inList)" as
        // "non-lists only", so I never set the row half on any of v7-v10.
        //
        // Vanilla list members do not set UITypeName, which is why copying them did not reveal
        // this: vanilla ROW TYPES have their own prefabs in UI/Prefabs/Components/AutoGenUI, found
        // by name at AutoGenUIPicker.cs:56 (GetPrefabByName on the element type name). IfThenBlock
        // and LegalAction resolve that way. A mod row type has no prefab and never can, so naming
        // an existing row UI explicitly is the documented path for exactly this case.
        //
        // Two different row UIs, to get two data points from one restart.
        [Eco, AllowEmpty, UIListTypeName("IEnumerableHeader"), UITypeName("Selector")]
        public ControllerList<Deed> DeedControllerList { get; set; }

        [Eco, AllowEmpty, UIListTypeName("IEnumerableHeader"), UITypeName("HorzBox")]
        public ControllerList<ProbeRow> ModRowList { get; set; }

        public UIContainerProbeComponent()
        {
            // Vanilla builds these in the owner's ctor with a back-reference naming the property
            // (LawSection.cs:70-73); the list needs the owner to route change notifications.
            this.DeedControllerList = new ControllerList<Deed>(this, nameof(this.DeedControllerList));
            this.ModRowList         = new ControllerList<ProbeRow>(this, nameof(this.ModRowList));
        }

        public override void Initialize()
        {
            base.Initialize();
            this.Changed(nameof(this.ContainerProbeNote));

            // Seed both lists so an empty render means "did not sync", not "nothing to show".
            if (this.ModRowList.Count == 0)
            {
                this.ModRowList.Add(new ProbeRow { Label = "mod row one" });
                this.ModRowList.Add(new ProbeRow { Label = "mod row two" });
            }

            if (this.DeedControllerList.Count == 0)
                foreach (var deed in PropertyManager.GetAllDeeds().Take(2))
                    this.DeedControllerList.Add(deed);
        }
    }

    /// <summary>
    /// TEMPORARY PROBE v9 -- a mod-defined ControllerList row.
    ///
    /// Modelled on IfThenBlock (Civics/Laws/IfThenBlock.cs:24-25), the vanilla row type:
    /// [Serialized] on the class, IController + INotifyPropertyChanged, and [Eco] members for
    /// whatever the row shows.
    ///
    /// The open question is whether a mod can supply a row type at all. Vanilla row types are
    /// woven by Eco.Fody at build time; this mod project references only Eco.ReferenceAssemblies
    /// and runs no weaver, so IController's ControllerID has to be implemented by hand. If that
    /// is not enough, this is where it shows.
    /// </summary>
    [Serialized]
    public class ProbeRow : IController, INotifyPropertyChanged
    {
        [Eco] public string Label { get; set; } = "row";

        int controllerID = -1;
        public ref int ControllerID => ref this.controllerID;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
