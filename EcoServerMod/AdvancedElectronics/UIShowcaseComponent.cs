using System.ComponentModel;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// TEMPORARY SHOWCASE -- delete once the layout brainstorm has its screenshots.
    ///
    /// PURPOSE, restated because it drifted: render as much of the client's autogen template
    /// vocabulary as possible IN ONE DEPLOY, screenshot it, then brainstorm which templates the real
    /// dock UI should use. See docs/ideation/2026-07-27-mod-ui-vocabulary.md -- this is the "batched
    /// probe" that document asked for. The showcase is a PREREQUISITE for the brainstorm, not a
    /// research project of its own.
    ///
    /// The vocabulary is the prefab set the client searches by name (per SLG's UI-System wiki page):
    ///
    ///     Boolean ButtonGrid CatalystDisplay CivicEntry CivicList CivicListNoScroll Color
    ///     Deprecated ErrorCivics ExpandableList FilterOutputsDisplay GeneralHeader HorzBox
    ///     IEnumerable IEnumerableHeader Int32 ItemInput LargeItemWithHeader Law LinedHeader
    ///     LongString PipeInput PipeOutput ProposableSlot Range SectionHeader Selector
    ///     SetOfConditions SimpleEntry Single String StringDescription StringDisplay StringInput
    ///     StringLargeInput StringPlaque StringPlaqueEditable StringTitle Table TextInput Void
    ///
    /// Everything below is a SCALAR, so nothing here can crash the client: an unknown template name
    /// is logged ("Can't convert named type to system type") and the member is skipped
    /// (View.cs:356). The list/table templates are deliberately ABSENT -- seven builds were spent on
    /// them without a single row rendering, the ideation doc files them as Tier B ("a hypothesis,
    /// not a plan"), and chasing them blocked the showcase the brainstorm actually needs.
    ///
    /// Declared [Serialized, Eco] with public setters wherever a template accepts input, because
    /// that is the shape proven to survive interaction -- an editable template with an unreachable
    /// setter kills the server with "Missing RPC call Set&lt;Prop&gt;". Numerics additionally carry
    /// Range(min,max), or every edit clamps straight back to the original value.
    /// </summary>
    [Serialized, CreateComponentTabLoc("UI Showcase", true), HasIcon]
    public class UIShowcaseComponent : WorldObjectComponent, INotifyPropertyChanged
    {
        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        public event PropertyChangedEventHandler PropertyChanged;

        // ---- Headers: do they group, and what does each cost in vertical space? ----

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string T_StringTitle { get; private set; } = "StringTitle";

        [SyncToView, Autogen, UITypeName("SectionHeader")]
        public string T_SectionHeader { get; private set; } = "SectionHeader";

        [SyncToView, Autogen, UITypeName("LinedHeader")]
        public string T_LinedHeader { get; private set; } = "LinedHeader";

        [SyncToView, Autogen, UITypeName("GeneralHeader")]
        public string T_GeneralHeader { get; private set; } = "GeneralHeader";

        // ---- Read-only text. Looking for the findings readout: multi-line, own scrollbar,
        //      not typable. StringDisplay is what the dock uses today. ----

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string T_StringDisplay { get; private set; } = "StringDisplay -- current dock default";

        [SyncToView, Autogen, UITypeName("String")]
        public string T_String { get; private set; } = "String";

        // MOVED to editable after a live crash. Declared display-only with a private setter, editing
        // it dropped the server with "Missing RPC call SetT_StringDescription". That is the second
        // member of the looks-like-display-but-is-editable family, after LongString -- the black
        // full-width bar reads as a readout, and it accepts input.
        [Serialized, Eco, UITypeName("StringDescription")]
        public string T_StringDescription { get; set; } =
            "StringDescription -- multi-line candidate for findings. Limestone: ~210 blocks, " +
            "shallowest at (412, 63, -88), depth 2-14. IronOre: ~48 blocks, depth 9-22.";

        [SyncToView, Autogen, UITypeName("StringPlaque")]
        public string T_StringPlaque { get; private set; } = "StringPlaque";

        [SyncToView, Autogen, UITypeName("Deprecated")]
        public string T_Deprecated { get; private set; } = "Deprecated";

        // ---- Editable text. All [Eco] + public setter so typing cannot drop the server. ----

        [Serialized, Eco, UITypeName("LongString")]
        public string T_LongString { get; set; } = "LongString -- editable, scrolls internally";

        [Serialized, Eco, UITypeName("StringInput")]
        public string T_StringInput { get; set; } = "StringInput";

        [Serialized, Eco, UITypeName("StringLargeInput")]
        public string T_StringLargeInput { get; set; } = "StringLargeInput";

        [Serialized, Eco, UITypeName("StringPlaqueEditable")]
        public string T_StringPlaqueEditable { get; set; } = "StringPlaqueEditable";

        [Serialized, Eco, UITypeName("TextInput")]
        public string T_TextInput { get; set; } = "TextInput";

        // ---- Scalars. Boolean/Int32/Single were confirmed working in an earlier build; kept so a
        //      regression is obvious and so the brainstorm sees them beside everything else. ----

        [Serialized, Eco, UITypeName("Boolean")]
        public bool T_Boolean { get; set; } = true;

        [Serialized, Eco, Range(0, 100), UITypeName("Int32")]
        public int T_Int32 { get; set; } = 42;

        [Serialized, Eco, Range(0, 10), UITypeName("Single")]
        public float T_Single { get; set; } = 3.5f;

        // Range is REMOVED, and it is the counterexample to "scalars are safe". On a float it drew
        // an empty "0 to 0" interval in an earlier build, so its value shape was already known to be
        // wrong -- but it was left declared editable anyway, and merely HOVERING it dropped the
        // server:
        //
        //     Could not finding matching RPC signature for method: SetT_Range
        //     ---> System.NotSupportedException: Specified method is not supported.
        //
        // The crash-safety argument only covers an UNKNOWN template name, which the client logs and
        // skips (View.cs:356). A KNOWN template bound to a mismatched value shape still generates a
        // setter RPC, and the server throws when it cannot match the signature. So the real rule is:
        // an editable template is only safe when the member type matches the shape the template
        // expects. Range needs a range-shaped value; there is no float form of it.

        // Renders a real colour picker, but the chosen colour does NOT survive a restart, and
        // [Serialized] is dropped here because it cannot help: Eco.Shared.Utils.Color is a plain
        // struct with no [Serialized] attribute of its own, so the serializer has nothing to write.
        // Vanilla has zero UITypeName("Color") usages, so there is no reference shape to copy.
        // Keep it in the showcase as a rendering data point; treat per-area colour as unavailable
        // until something serializable backs it (e.g. store an int/string and map it to a Color).
        [Eco, UITypeName("Color")]
        public Color T_Color { get; set; } = Color.Green;

        // ---- Live-refresh experiment (ONE member, so the result is unambiguous). ----
        //
        // Every editable member so far writes and persists but does not update on screen until the
        // window is reopened or the server restarts -- the steppers move and the number does not.
        // The recorded rule is that [Eco] change tracking is not enough on a mod component:
        // INotifyPropertyChanged must be RAISED, and declaring the interface (as this class does)
        // without ever firing it is exactly the persist-but-do-not-refresh symptom.
        //
        // This is the highest-value open question for the real dock UI, because it is what makes
        // the current assign buttons feel dead. T_Int32Live pushes the change explicitly; T_Int32
        // above is the unchanged control. If Live updates on screen and the control does not, the
        // fix for the whole interface is one Changed() call per setter.
        int t_Int32Live = 7;

        [Serialized, Eco, Range(0, 100), UITypeName("Int32")]
        public int T_Int32Live
        {
            get => this.t_Int32Live;
            set
            {
                this.t_Int32Live = value;
                this.Changed(nameof(this.T_Int32Live));
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.T_Int32Live)));
            }
        }

        public override void Initialize()
        {
            base.Initialize();

            // Only [SyncToView, Autogen] members need an explicit push; [Eco] members are handled by
            // the framework's own change tracking.
            this.Changed(nameof(this.T_StringTitle));
            this.Changed(nameof(this.T_SectionHeader));
            this.Changed(nameof(this.T_LinedHeader));
            this.Changed(nameof(this.T_GeneralHeader));
            this.Changed(nameof(this.T_StringDisplay));
            this.Changed(nameof(this.T_String));
            this.Changed(nameof(this.T_StringDescription));
            this.Changed(nameof(this.T_StringPlaque));
            this.Changed(nameof(this.T_Deprecated));
        }
    }
}
