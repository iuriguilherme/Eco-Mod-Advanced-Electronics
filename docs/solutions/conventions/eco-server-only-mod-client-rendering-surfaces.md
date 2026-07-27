---
title: "What a server-only Eco mod can and cannot render on the stock client"
date: 2026-07-24
last_updated: 2026-07-27
category: conventions
module: EcoServerMod
problem_type: convention
component: tooling
severity: high
applies_when:
  - "Designing any player-facing UI for an Eco mod that ships server C# plus a ModKit asset bundle but no custom client code"
  - "Deciding whether a readout belongs in a component tab, a map overlay, a tooltip, or world-space text"
  - "A mod-defined tab, overlay, or synced member renders blank, or crashes the client on view reception"
tags: [eco-modding, client-rendering, worldobjectcomponent, tab, overlay, synctoview, editmap, server-only, modkit, gamepickerlist, tags, visibilityparam]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Spike]
---

# What a server-only Eco mod can and cannot render on the stock client

## Context

The survey-drone mod needed a player-facing interface: a way to draw a survey area on the map
and a place to read the drone's findings. The natural-sounding design — "the mod gets its own
map overlay layer, and its dock window shows a rich table of findings" — collided with a hard
boundary that neither the stripped `Eco.ReferenceAssemblies` nor the server-side API surface
reveals: **the stock Eco client renders UI from build-time-generated view types and a fixed set
of `UITypeName` templates, so mod-defined view content that has no generated client view does
not render.** A server-only mod (server C# in `Mods/UserCode/` plus a ModKit asset bundle of
prefabs/textures) ships no client C# and runs no client-side view codegen, so it can only use
what the stock client already knows how to draw.

This was settled by a live feasibility batch (`.references/screenshots/9`, batch L1) plus
reading the game client source at the local Eco checkout — after the project's earlier tooltip
readout "did not render" with no server-side error, the same failure class.

## Guidance

> **Citation convention.** Paths beginning `Client/`, `Server/`, `Eco.Core/`, `Eco.ModKit/`,
> `Eco.Gameplay/`, `Eco.Shared/` or `Components/` refer to the **game source in the local Eco
> checkout, external to this repo** — they will not resolve inside this repository, by design.
> Paths beginning `EcoServerMod/`, `Assets/` or `docs/` are in-repo. Same convention as the
> sibling placement-requirements doc.

Treat client rendering as a **whitelist of proven surfaces**, not an open contract. For a
server-only Eco mod on 0.13.0.4, the surfaces are:

**1. A mod-defined `WorldObjectComponent` tab renders — use it, with constraints.**
A component with `[Serialized, CreateComponentTabLoc("Tab"), HasIcon]` and
`Availability => WorldObjectComponentClientAvailability.UI` gets its own tab on the object's
window (verified live on the Drone Dock). Inside it:
- **Actions work.** A method with `[RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton")]`
  renders a button and fires, with the `AccessType` enforced by the engine. This is the way to
  offer create/assign/delete without chat commands.
- **A synced collection of non-`View` values CRASHES the client.** `[SyncToView] IEnumerable<string>`
  (or of any mod value type) throws `Cannot convert value ... String to type
  Eco.Shared.View.View` → `Failed to receive views` → the client disconnects. Collection
  elements must be `View` types. `DeedManagementComponent`'s `[SyncToView] IEnumerable<Deed>`
  works only because `Deed` has a *generated client view*; a mod's own type does not. **Compose
  any list/readout as one formatted `LocString` text block, not a synced collection.**
- **Read-only text renders — but only from a SETTABLE property that is explicitly assigned.**
  A settable string (or `LocString`) property with `[SyncToView, Autogen, UITypeName("StringDisplay")]`
  (or `"StringTitle"`) renders its text when you **assign it and call `this.Changed(nameof(prop))`**
  — set it in `Initialize` and after every mutation. Both `StringDisplay` (normal text) and
  `StringTitle` (larger/caps) were confirmed live. The earlier failure was a **never-assigned
  computed getter** (`public LocString X => Build();`): with no backing value ever set and no
  `Changed`, nothing syncs to the client, so the member draws blank even though the button beside
  it renders. Mirror the stock `ForSaleComponent.Note` / `ConstitutionComponent.DisplayText`
  shape (settable, assigned), not the `PartsComponent.Description` computed-getter shape — the
  computed getter works for a stock component with a generated view but not for a mod component.
  A plain `[SyncToView] string` with no `UITypeName` did not render; give it a `UITypeName`.
- **A rich per-row list of YOUR OWN data is not available to a mod tab.** The stock
  "My Deeds" list, the Authorization tab, and the jurisdiction/demographic Selector dropdowns
  render through a hand-written **client-side** `WorldObjectPanel` MonoBehaviour (e.g. `DeedsUI`,
  `AuthComponentView`) bound to a codegen'd `WorldObjectComponentView` via
  `[WorldObjectUIPanel("...")]`, instantiating row prefabs. **The ModKit does not expose
  `WorldObjectPanel`, `WorldObjectComponentView`, or `[WorldObjectUIPanel]`** (absent from
  `Assets/EcoModKit` and `Assets/EcoLibs`), so a mod cannot write one. A `[SyncToView]`
  collection of a mod-defined `IController` renders **blank** in the generic auto-view (it does
  not crash the way an `IEnumerable<string>` does, but it shows nothing). This half stands: to
  display mod-owned rows, compose text.
- **But a native item PICKER does render from a mod tab — the constraint is the DATA, not the
  tab.** `[Eco, AllowEmpty, RequiredTag(...)] GamePickerList<BlockItem>` renders the same
  multi-select popup a civic law uses, from an ordinary mod `WorldObjectComponent` tab, and its
  selection reads back server-side (`SurveyResultsComponent.MaterialTargets`, confirmed live).
  This overturns an earlier reading of this doc that treated pickers as tab-unreachable. The
  real rule is narrower: a picker's options come from a **client-shared registrar of a viewable
  type** (Item, Deed, Settlement), so it works for globally-registered game types and *not* for
  dock-local mod data — survey areas have no registrar, which is why they still cannot have a
  Selector. Note this shape has **no vanilla precedent on a `WorldObjectComponent`**: all 18
  vanilla `GamePickerList<T>` declarations sit on civics GameValues, work-party payments, or
  plain `IController` row types, and the only two combining it with `RequiredTag` are in
  `Civics/Constitutional/CivicArticleCondition.cs:45-46`. Absence of precedent turned out to
  mean untested, not unsupported.
- **Scoping a picker requires a tag the client already knows — and the deadline is earlier than
  it looks.** `RequiredTag` filters client-side against `ViewClassInfo.Tags`
  (`Client/Assets/UI/Scripts/Utilities/SelectorPopupUI.cs:337-348`), which the server builds
  **once** while constructing the `ControllerManager` plugin
  (`Eco.Core/Controller/ControllerMarshalerService.cs:367`, reached from `ControllerManager`'s
  cache build). Consequences:
  - A tag declared with a `[Tag("X")]` **attribute** on a type — including a mod type, or a
    vanilla item replaced via a `.override` file — **does** reach the client. Mod DLLs load and
    `InitMods()` runs *before* `TagManager.Initialize()`
    (`Eco.ModKit/ModDataSync.cs:63-66`), and the attribute pass enumerates all loaded assemblies.
  - A type→tag association created **at runtime** (`TagManager.AddTypeToTag`/`GetOrMake` from an
    `IModKitPlugin.Initialize`, which runs after `ControllerManager` is added) is never in that
    snapshot. The server registry looks correct and the picker still renders **empty**.
  - Corrected attribution: an earlier conclusion in this project blamed
    `GeneratedRegistrarWrapper<Tag>.SetupDone()`. That is only a `WhenReady` gate with no
    observed consumer; it is not what freezes the client's tag data. The `ViewClassInfo` build
    is. The symptom was reported accurately, the cause was not — and the wrong cause made the
    attribute/`.override` route look futile when it is in fact the route that works.
- **Multiple mod components each get their own tab.** Two mod `WorldObjectComponent`s on one
  object, each with `CreateComponentTabLoc`, both register and both render (Areas + Results on
  the Drone Dock, confirmed live). Splitting a crowded tab is a real option.
- **`VisibilityParam` works on a mod tab, so members can be conditionally hidden.** A
  `[SyncToView]` bool member plus `VisibilityParam(nameof(ThatBool))` on an `[RPC, Autogen]`
  button hides or shows it client-side; the visibility source must be re-pushed with an explicit
  `this.Changed(nameof(ThatBool))` or the client never re-evaluates. Vanilla precedent is
  `AreaBonusComponent` (`Components/AreaBonusComponent.cs:140,143`) — itself a
  `WorldObjectComponent`. Because RPCs are compile-time methods, this gives a **fixed pool** of
  buttons gated per position, not a dynamic count.
- **The escape hatch for custom client UI is a bundle prefab MonoBehaviour driven by
  animated-states.** A mod ships its own MonoBehaviour on the WorldObject prefab (e.g. this
  project's `DockReadoutDisplay`) that reads server-synced `StringStates`/`FloatStates` (pushed
  by `WorldObject.SetAnimatedState`) and renders whatever Unity UI it wants. This is Unity client
  work, the data channel is one-way (server -> client), and interaction still routes through the
  server-side `[RPC]` tab — but it is the only way to render UI the generic auto-view cannot.

**2. The map *editor* is reachable, and it is a full multi-entry MANAGER, not just a picker.**
`player.EditMap(MapEditRequest)` opens the same plot editor district/deed editing uses (it runs
on `EditableOverlay`, a stock client type — a separate code path from the passive overlay list).
An earlier version of this doc prescribed the minimal deed shape (`AllowNewEntries = false`, one
fixed editable entry). That was the first thing tried, not the ceiling. Confirmed live, a mod can
hand the editor **every** region it owns at once and let the player create, redraw, rename and
delete them in one round-trip — replacing a whole stack of Create/Edit/View/Delete buttons:

- `AllowNewEntries = true` plus one `MapEntry` per owned region in `Overlay.MapEntries`, each
  with its own colour and `EntryDescription` (which is the player-visible name).
- Rename and delete are enabled **per entry** via `EntryStatus[id] = new EditableEntryStatus
  { AllowNameChange = true, AllowDelete = true, MaxArea = cap }`. `DefaultEntryStatus` is
  consulted **only for ids absent from `EntryStatus`**, so relying on it alone leaves the rename
  field and delete button inert on every existing entry.
- Seed a placeholder entry when the mod owns none yet, or the player opens an editor with
  nothing to draw into.

Caveats that cost real cycles:

- **The returned overlay must be treated as a diff, and a partial return is dangerous.** Absence
  of an id from the returned `MapEntries` is the *only* delete signal, so a null/partial
  round-trip that is not guarded reads as "the player deleted everything". Check both `Map` and
  `MapEntries` before reconciling.
- **Replace geometry only when it actually changed.** If redrawing an area resets derived state
  (findings, progress), reconciling unconditionally wipes that state for every area on every
  confirm — including areas the player never touched. Compare plot sets first.
- **New entries come back with client-assigned temporary ids** (negative in practice), so the
  server must mint its own id rather than trusting the returned one. Existing ids do round-trip
  intact, which is what makes per-entry reconciliation possible at all.
- **A new entry's default NAME is not server-controllable without a registrar.** The client names
  it `New {RelatedRegistrar's contained type}`, falling back to the hardcoded `"District"`
  (`Client/Assets/UI/Scripts/Minimap/MapEditor/MinimapEditor.cs:130`, applied at `:150`).
  `RelatedRegistrar` resolves against client-shared registrars only — vanilla's sole setter is
  `DistrictMap.cs:177` — so mod-local regions with no registrar always show "New District" in the
  editor. The only lever is renaming server-side after the confirm, which is why the name appears
  to correct itself on return. Cosmetic, editor-only.
- The client's `MaxArea` is a hint; re-check the cap server-side.

**3. A passive map-overlay LAYER is impossible for a mod — do not design around one.**
The client `OverlayManager.Start()` hardcodes exactly two overlay sources; a mod registrar is
never consulted, and rendering a custom overlay additionally needs a client-side `IClientOverlay`
partial on a codegen-generated view type. A server-only mod supplies neither, and its asset
bundle carries prefabs/textures, not replacements for client engine singletons.

**4. World-space text via the prefab's own bundle script works** (the `SetAnimatedState` →
prefab `DockReadoutDisplay` path). This is client rendering the mod genuinely controls because
the MonoBehaviour ships in the bundle — but it is limited to what that prefab script does, not
arbitrary UI.

**The meta-rule (two halves):**

*Verify each surface live before building on it.* A working server-side contract (a public
interface, a settable property, a compiling `[SyncToView]` member) does **not** imply the stock
client will render it. Same discipline as "trace the failing call, don't theorise" — reach for
the observed behavior, not the plausible API.

*And verify the NEGATIVES too, because they are the expensive ones.* Three entries in the list
above were once "impossible" here, and all three shipped: rich pickers from a mod tab,
multi-entry map management, and a second mod component tab. A false positive costs one failed
deploy and announces itself. A false negative is written into a conventions doc, silently prunes
the design space for every later session, and nothing ever contradicts it — the option is simply
never tried again. Before recording "X is impossible", ask what would have to be true, and
whether the failure was actually observed at X or inferred from a neighbour.

The specific trap, in both overturned cases, was **attributing a failure to the wrong layer**:
- The picker failed on *dock-local data with no registrar*, and that got recorded as *"pickers
  don't work in mod tabs"* — surface blamed for a data problem. Retesting the same surface with
  registry-backed data (Items) worked immediately.
- The empty tag-scoped picker was blamed on *tag registration timing at `SetupDone()`*, when the
  real deadline is the one-time `ViewClassInfo` build. Same observable, and the wrong cause
  eliminated the `[Tag]`-attribute route that would have worked.

When something fails, isolate whether the **surface**, the **data behind it**, or the **timing of
when that data was registered** was the blocker. "Absence of vanilla precedent" is a fourth
distinct thing, and the weakest evidence of the four: the working `GamePickerList` on a
`WorldObjectComponent` has no vanilla precedent at all.

## Why This Matters

Three of this mod's interaction surfaces were ruled out one at a time — the tooltip (never
rendered), the map overlay layer (engine-blocked), and a rich synced-list tab (crashes the
client) — and each discovery could have cost a wasted implementation cycle or a client-crashing
deploy. The boundary is invisible from the server side: the code compiles, the API exists, and
the failure is either a silent blank or a disconnect with a deserialization stack trace that
names a view type, not your component. Knowing the whitelist up front means designing the
interface as *dock-tab text + buttons, drawing via `EditMap`* from the start, instead of
discovering the constraint after building the wrong thing.

It also reframes "the mod should own its own map layer": the drawable, civics-free thing a mod
can actually reach is the map *editor* (an action), not a persistent *overlay* (a rendered
layer). Those are different engine subsystems with different reachability.

The 2026-07-27 revision matters for the opposite reason. The whitelist was **too conservative**,
and a too-conservative whitelist is self-confirming: the UI was designed around text-and-buttons
because pickers were believed unreachable, so nothing re-tested pickers. It took a user
overruling a withheld experiment ("A crash would crash the game anyway, it's ok to implement it
and test") to break the loop. Two of the three overturned limits had been recorded from a single
failed attempt against the *hardest* case — dock-local data, a runtime-registered tag — and
generalized into a rule about the surface. The cost was not one wasted cycle; it was a whole
interaction design shaped around a constraint that was not real.

## When to Apply

- Before designing any player-facing UI for a server-only Eco mod — pick surfaces from the
  whitelist above rather than from what the server API appears to allow.
- When a mod tab, overlay, or synced member renders blank or crashes view reception: suspect a
  missing generated client view for a mod-defined type before suspecting your own logic.
- When tempted to sync a `List`/`IEnumerable` of a mod type or a primitive to a tab: stop and
  compose text instead, unless every element type has a generated client view.
- When a design assumes a passive map overlay layer: it is not achievable server-only; move the
  display to a dock tab or world-space text, or cut it.
- When a tag-scoped picker renders empty while the server registry looks correct: the tag was
  associated too late for the one-time `ViewClassInfo` build. Move it to a `[Tag]` **attribute**
  (on your own type, or on a vanilla type replaced via a `.override` file) instead of registering
  it at runtime. Do not conclude "mods cannot have tags".
- Before writing "X is impossible" into this doc or any other convention: name what was actually
  observed, at which layer, and with which data. If the negative rests on one attempt against the
  hardest case, or on absence of vanilla precedent, mark it *untested* rather than *impossible* —
  and say what a cheap retest would look like.

## Examples

The client-side hardcoding that blocks a mod overlay layer, from the game client source at
`Eco/Client/Assets/Scripts/Overlays/OverlayManager.cs` (the local Eco checkout, external to this
repo — same citation convention as the sibling placement-requirements doc):

```csharp
public void Start()
{
    GlobalData.SubscribeAndCallEveryInit(() => RegisterRegistrar(GlobalData.Registrar<DistrictMapView>()));
    GlobalData.SubscribeAndCallEveryInit(() => EnsureRegistered(GlobalData.Obj.InfluenceManager.Maps.Values.Cast<IClientOverlay>()));
}
```

Only the district and influence layers are ever registered; there is no path for a mod's
overlay to enter this list, and `Overlays.md` in the same client folder documents that a server
overlay renders only via a client-side `IClientOverlay` partial on a codegen'd view type.

The tab shape that DOES render (verified live), modelled on the vanilla `AreaBonusComponent`:

```csharp
[Serialized, CreateComponentTabLoc("Survey Areas"), HasIcon]
public class SurveyAreasComponent : WorldObjectComponent
{
    public override WorldObjectComponentClientAvailability Availability =>
        WorldObjectComponentClientAvailability.UI;

    // Read-only text: a SETTABLE property, assigned + Changed-notified. Renders.
    // (A never-assigned computed getter `=> Build();` draws blank — the earlier trap.)
    [SyncToView, Autogen, UITypeName("StringDisplay")]
    public string AreasDisplay { get; private set; } = string.Empty;

    public override void Initialize() { base.Initialize(); this.Refresh(); }
    private void Refresh() { this.AreasDisplay = ComposeText(); this.Changed(nameof(this.AreasDisplay)); }

    // Actions render and fire; AccessType is engine-enforced.
    [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Create area")]
    public void CreateArea(Player player) { /* opens player.EditMap(...), then Refresh() */ }

    // A synced *collection* of a mod/plain type here would still CRASH the client.
    // Compose the list as the single AreasDisplay text block above.
}
```

The crash signature when the whitelist is violated (a `[SyncToView] IEnumerable<string>` member),
from the live client log:

```
Failed to receive views from the server
InvalidOperationException: Cannot convert value: <element> with valuetype String to type: Eco.Shared.View.View
```

The map-editor call that works for a mod caller lives in this repo as the proven reference:
`EcoServerMod/AdvancedElectronics.Spike/SpikeEditMapCommand.cs` (the deed-pattern `MapEditRequest`
+ `await player.EditMap`). The **multi-entry manager** built on the same call —
`AllowNewEntries`, per-entry `EntryStatus`, and the reconcile that treats absence as deletion —
is `EcoServerMod/AdvancedElectronics/SurveyAreaPicker.cs`.

The picker + gated-button shapes that render from a mod tab (both confirmed live):

```csharp
// Native multi-select item picker, from an ordinary mod WorldObjectComponent tab.
// Options come from the client-shared Item registrar; RequiredTag filters them against the
// one-time ViewClassInfo tag snapshot, so the tag must be attribute-declared, not runtime-added.
[Eco, AllowEmpty, RequiredTag(BlockTags.Excavatable)]
[LocDescription("Materials to show in the survey results.")]
public GamePickerList<BlockItem> MaterialTargets { get; set; } = new();

// Conditionally hidden button. RPCs are compile-time methods, so this is a FIXED pool of
// buttons gated per position -- not a dynamically sized list.
[SyncToView] public bool AreaExists1() => this.AreaCount() >= 1;

[RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists1)),
 UITypeName("BigButton"), Description("Assign Area 1")]
public void AssignArea1(Player player) => this.ToggleAssign(1);

// Without this push the client never re-evaluates visibility, so a newly created area gains
// no button and a deleted one keeps its own until the window is reopened.
this.Changed(nameof(this.AreaExists1));
```

Live in this repo as `EcoServerMod/AdvancedElectronics/SurveyResultsComponent.cs` (picker) and
`SurveyAreasComponent.cs` (gated buttons), on the two-tab `DroneDockObject`.

The startup ordering that decides whether a tag reaches the client, from the game source:

```csharp
// Eco.ModKit/ModDataSync.cs:63-66 -- mods are initialized BEFORE tags are built,
// so a [Tag] attribute on a mod (or .override'd vanilla) type IS collected.
this.InitMods();
Parallel.Invoke(Block.Initialize, Item.Initialize);
Skill.InitializeSkills();
TagManager.Initialize();

// Eco.Core/Controller/ControllerMarshalerService.cs:367 -- read ONCE per controller type
// while ControllerManager builds its cache. Anything associated after this is invisible
// to the client picker, no matter how correct the server registry looks.
var tagsNames = ControllerManager.TypeToTags?.Invoke(controllerType);
```

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the sibling
  "the stripped reference assemblies hide the truth" convention, for placement rather than
  rendering; both are cases where a compiling server contract hides a client requirement.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — why the readout surface
  is part of the feature; this doc is the constraint list that surface must fit inside.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the same
  "observe, don't assume" discipline applied to a pathfinding invariant; here applied to client
  rendering.
