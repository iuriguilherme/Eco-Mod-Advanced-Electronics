using System.ComponentModel;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
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

        // =====================================================================================
        // PROBE v6 -- the two questions U1 of the merged-survey-tab plan has to answer before
        // the dock's own tab is touched. Both live HERE because a WorldObjectComponent owns its
        // own tab: if the editable member below has no reachable setter, clicking it drops every
        // connected player, and it costs this tab rather than the dock's.
        //
        // Question 1 (dangerous): can a checkbox be a VIEW of state that lives somewhere else?
        //   The merged tab's roster wants exactly that -- each row reads and writes the dock's
        //   assigned area and holds nothing of its own, so "only one row checked" is a property
        //   of having one source of truth rather than bookkeeping. That needs [Eco] minus
        //   [Serialized]: sync and RPC generation, but no persistence, because there is nothing
        //   to persist. SLG's UI-System wiki page says [Eco] is a composite you may split, and
        //   Server/Eco.Shared/Networking/EcoAttributes.cs:17-27 confirms the parts. Nothing in
        //   this project has run the split.
        //
        // Question 2 (likely fine, cheap to confirm): can a row's label be set at RUNTIME?
        //   Vanilla does it on a WorldObjectComponent -- SettlementFoundationComponent.cs:213
        //   pairs DynamicTitle with a [SyncToView] string METHOD (:101), alongside the same
        //   VisibilityParam our own assign buttons already use successfully from a mod tab.
        //   If it holds, each roster checkbox carries its area's name and coverage; if not,
        //   rows read "P_ Derived Bool"-style member names and the text list comes back.
        //
        // Two variables, isolated across three members:
        //   P_DynamicButton  DynamicTitle only, vanilla-identical shape   -> isolates DynamicTitle
        //   P_StoredBool     DynamicTitle + known-good [Serialized, Eco]  -> control
        //   P_DerivedBool    DynamicTitle + [SyncToView, Autogen, AutoRPC], derived, no backing
        //                    field                                        -> the target shape
        //
        // If the button and the stored bool title correctly and only the derived one misbehaves,
        // the attribute split is the culprit, not DynamicTitle.
        //
        // NOT PROBED: a non-constant Range bound. That is a C# limitation, not an Eco one --
        // RangeAttribute (EcoAttributes.cs:77) is a plain attribute, its arguments must be
        // compile-time constants, and there is no RangeParam(nameof(...)) sibling anywhere in
        // the view system. The findings cursor is capped at compile time, like the button pool.
        // =====================================================================================

        /// <summary>The state the checkbox below is a view of. Stands in for the dock's assigned area.</summary>
        int p_derivedSource;

        /// <summary>Label source, shaped exactly like vanilla's: a [SyncToView] method returning string.</summary>
        [SyncToView]
        public string P_TitleSource() => $"Runtime label -- source is {this.p_derivedSource}";

        /// <summary>Vanilla-identical: DynamicTitle on an RPC BigButton. The safest of the three.</summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), DynamicTitle(nameof(P_TitleSource))]
        public void P_DynamicButton(Player player) => this.SetDerivedSource(this.p_derivedSource + 1);

        /// <summary>Control: known-good editable binding, so a failure here indicts DynamicTitle itself.</summary>
        [Serialized, Eco, UITypeName("Boolean"), DynamicTitle(nameof(P_TitleSource))]
        public bool P_StoredBool { get; set; }

        /// <summary>
        /// The target shape. No backing field of its own — the value IS <see cref="p_derivedSource"/>.
        /// Declared as [Eco] decomposed: sync + autogen + RPC generation, deliberately without
        /// [Serialized], because persisting a computed value would put a stale copy beside the real one.
        /// </summary>
        [SyncToView, Autogen, AutoRPC, UITypeName("Boolean"), DynamicTitle(nameof(P_TitleSource))]
        public bool P_DerivedBool
        {
            get => this.p_derivedSource > 0;
            set => this.SetDerivedSource(value ? 1 : 0);
        }

        /// <summary>Echoes the source so a write is visible even if a control fails to redraw.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string P_DerivedEcho { get; private set; } = "source is 0";

        // ---- Round 2, 2026-07-31 -------------------------------------------------------------
        // Round 1 answered the label question and, in doing so, invalidated its own answer to the
        // other one. DynamicTitle on a property row does not fall back to the member name -- it
        // blanks the label AND kills the row's input path. P_StoredBool proved that on its own:
        // known-good [Serialized, Eco] binding, dead the moment DynamicTitle was on it.
        //
        // So P_DerivedBool being dead says nothing about the attribute split; DynamicTitle alone
        // accounts for it. These two members carry ONE variable each, no titles:
        //
        //   P_StoredNoTitle   [Serialized, Eco]                     -> baseline: does Boolean
        //                                                              input still work at all?
        //   P_DerivedNoTitle  [SyncToView, Autogen, AutoRPC], no
        //                     [Serialized], no backing field        -> the real KTD1 question
        //
        // If the baseline ticks and the derived one does not, the split is the problem and KTD1's
        // fallback applies. If both tick, the merged roster gets its derived checkboxes.
        // -------------------------------------------------------------------------------------

        /// <summary>Baseline: the binding already proven to work, with nothing else on it.</summary>
        [Serialized, Eco, UITypeName("Boolean")]
        public bool P_StoredNoTitle { get; set; }

        // ---- Round 3, 2026-07-31: the WRITE path, per member ---------------------------------
        // Rounds 1 and 2 both failed to answer this, for the same structural reason: every derived
        // checkbox read the SAME source, and the button could move that source too. So no
        // observation identified which control caused what, and the read path masked the write
        // path -- a box can tick because the player clicked it, or because something else wrote
        // the field it derives from. Round 2's second screenshot was the button, not a click.
        //
        // Fix: one counter per member, nothing shared, and a COUNTER rather than a bool so even a
        // toggle the client reverts leaves a trace. A and B differ in exactly one thing.
        //
        //   P_WriteA  [Serialized, Eco]                      -> baseline write path
        //   P_WriteB  [SyncToView, Autogen, AutoRPC]         -> the decomposed attribute
        //
        // Read P_WriteProof, not the checkboxes: a tick mark only says the client drew one.
        // -------------------------------------------------------------------------------------

        int p_writeA;
        int p_writeB;

        /// <summary>The only trustworthy readout in this experiment: server-side call counts.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string P_WriteProof { get; private set; } = "setter calls -- A: 0   B: 0";

        [Serialized, Eco, UITypeName("Boolean")]
        public bool P_WriteA
        {
            get => this.p_writeA % 2 == 1;
            set { this.p_writeA++; this.PushWriteProof(); }
        }

        [SyncToView, Autogen, AutoRPC, UITypeName("Boolean")]
        public bool P_WriteB
        {
            get => this.p_writeB % 2 == 1;
            set { this.p_writeB++; this.PushWriteProof(); }
        }

        // Round 5: B never fired, and the likely reason is authorization rather than the split.
        // The two attributes default differently -- EcoAttribute:30 says the RPC requires Consumer
        // Access when unspecified, AutoRPCAttribute:62 says Full Access. A player without full
        // access on the dock has their write rejected silently, with no exception and no effect,
        // which is exactly what B did. Every working control in this mod names ConsumerAccess.
        //
        // C is B with that one token. If C fires and B does not, the decomposed attribute is fine
        // and the default access level was the whole problem.
        int p_writeC;

        [SyncToView, Autogen, AutoRPC(AccessType.ConsumerAccess), UITypeName("Boolean")]
        public bool P_WriteC
        {
            get => this.p_writeC % 2 == 1;
            set { this.p_writeC++; this.PushWriteProof(); }
        }

        // ---- Round 4, 2026-07-31: stop reading the client ------------------------------------
        // Rounds 1-3 all read the CLIENT's drawing, and the client draws a tick whether or not the
        // server ever hears about it. Round 3's counters were meant to fix that but confounded the
        // baseline: to get an observable side effect, P_WriteA was given a COMPUTED getter, so it
        // no longer matches T_Boolean's proven auto-property shape. "A did not fire" may therefore
        // mean "computed getters cannot receive input", not "Boolean input is dead".
        //
        // This mirror is driven by DroneDock's existing one-second readout tick and prints the
        // values the SERVER holds. Tick a box, wait a second, read this line -- if the value moved,
        // the write landed, whatever the checkbox is drawing.
        //
        // P_StoredNoTitle is the member that matters here: a plain [Serialized, Eco] auto-property,
        // byte-for-byte the T_Boolean shape, with nothing else on it.
        //
        // It doubles as the KTD2 race test: the tick writes nothing, it only pushes, so a click and
        // a tick landing in the same second must not fight.
        // -------------------------------------------------------------------------------------

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string P_ServerMirror { get; private set; } = "server state -- waiting for first tick";

        /// <summary>Called from the dock's tick. Reports server-side truth; never writes a member.</summary>
        public void RefreshMirror()
        {
            this.P_ServerMirror =
                $"server state -- StoredNoTitle: {this.P_StoredNoTitle} | " +
                $"calls A:{this.p_writeA} B:{this.p_writeB} C:{this.p_writeC} | " +
                $"source:{this.p_derivedSource}";

            this.Changed(nameof(this.P_ServerMirror));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.P_ServerMirror)));
        }

        void PushWriteProof()
        {
            this.P_WriteProof = $"setter calls -- A: {this.p_writeA}   B: {this.p_writeB}   C: {this.p_writeC}";

            this.Changed(nameof(this.P_WriteProof));
            this.Changed(nameof(this.P_WriteA));
            this.Changed(nameof(this.P_WriteB));
            this.Changed(nameof(this.P_WriteC));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.P_WriteProof)));
        }

        /// <summary>The real question: [Eco] decomposed, derived, no backing field, no label attribute.</summary>
        [SyncToView, Autogen, AutoRPC, UITypeName("Boolean")]
        public bool P_DerivedNoTitle
        {
            get => this.p_derivedSource > 0;
            set => this.SetDerivedSource(value ? 1 : 0);
        }

        void SetDerivedSource(int value)
        {
            this.p_derivedSource = value;
            this.P_DerivedEcho = $"source is {value}";

            this.Changed(nameof(this.P_DerivedBool));
            this.Changed(nameof(this.P_DerivedNoTitle));
            this.Changed(nameof(this.P_DerivedEcho));

            // Pushed, and observed NOT to take: the button's caption stayed at "source is 0" while
            // the echo reached 4. A DynamicTitle caption is resolved when the window opens and does
            // not re-resolve, so a runtime label cannot carry changing state.
            this.Changed(nameof(this.P_TitleSource));

            // Held constant on purpose. Round 1 refreshed live with both this and Changed() firing,
            // so either could be responsible. Dropping it here would put a second variable in a
            // round whose job is the attribute split -- which is the confound that cost round 1.
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.P_DerivedBool)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.P_DerivedNoTitle)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.P_DerivedEcho)));
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
            this.Changed(nameof(this.P_DerivedEcho));
        }
    }
}
