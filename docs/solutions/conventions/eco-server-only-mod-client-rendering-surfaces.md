---
title: "What a server-only Eco mod can and cannot render on the stock client"
date: 2026-07-24
category: conventions
module: EcoServerMod
problem_type: convention
component: tooling
severity: high
applies_when:
  - "Designing any player-facing UI for an Eco mod that ships server C# plus a ModKit asset bundle but no custom client code"
  - "Deciding whether a readout belongs in a component tab, a map overlay, a tooltip, or world-space text"
  - "A mod-defined tab, overlay, or synced member renders blank, or crashes the client on view reception"
tags: [eco-modding, client-rendering, worldobjectcomponent, tab, overlay, synctoview, editmap, server-only, modkit]
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
- **Read-only text is not guaranteed by `StringTitle`.** A
  `[SyncToView, UITypeName("StringTitle")] LocString` rendered no visible text in the live probe
  (the tab drew its button but not the title text). If you need a read-only text block, probe the
  available `UITypeName` templates live to find one that renders, and keep world-space prefab
  text as the fallback (see #4). Do not assume any given text member displays.

**2. The map *editor* is reachable and returns drawn plots — use it for area selection.**
`player.EditMap(MapEditRequest)` opens the same plot editor district/deed editing uses (it runs
on `EditableOverlay`, a stock client type — a separate code path from the passive overlay list).
Build the request on the deed pattern: `AllowNewEntries = false`, one fixed editable entry,
`EntryStatus.MaxArea` for a plot cap, `RelatedRegistrar` left unset. Verified live: the editor
opened with mod-authored title/hint, enforced the cap, and returned the drawn plot coordinates.
The returned overlay is a world-sized `Array2D<int>` the server must diff and re-validate;
client entry IDs are renumbered and must not be trusted.

**3. A passive map-overlay LAYER is impossible for a mod — do not design around one.**
The client `OverlayManager.Start()` hardcodes exactly two overlay sources; a mod registrar is
never consulted, and rendering a custom overlay additionally needs a client-side `IClientOverlay`
partial on a codegen-generated view type. A server-only mod supplies neither, and its asset
bundle carries prefabs/textures, not replacements for client engine singletons.

**4. World-space text via the prefab's own bundle script works** (the `SetAnimatedState` →
prefab `DockReadoutDisplay` path). This is client rendering the mod genuinely controls because
the MonoBehaviour ships in the bundle — but it is limited to what that prefab script does, not
arbitrary UI.

**The meta-rule:** verify each client-render surface **live** before building on it. A working
server-side contract (a public interface, a settable property, a compiling `[SyncToView]`
member) does **not** imply the stock client will render it. This is the same discipline as
"trace the failing call, don't theorise" — reach for the observed behavior, not the plausible
API.

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

## When to Apply

- Before designing any player-facing UI for a server-only Eco mod — pick surfaces from the
  whitelist above rather than from what the server API appears to allow.
- When a mod tab, overlay, or synced member renders blank or crashes view reception: suspect a
  missing generated client view for a mod-defined type before suspecting your own logic.
- When tempted to sync a `List`/`IEnumerable` of a mod type or a primitive to a tab: stop and
  compose text instead, unless every element type has a generated client view.
- When a design assumes a passive map overlay layer: it is not achievable server-only; move the
  display to a dock tab or world-space text, or cut it.

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

    // Actions render and fire; AccessType is engine-enforced.
    [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Create area")]
    public void CreateArea(Player player) { /* opens player.EditMap(...) */ }

    // A synced collection of a mod/plain type here would CRASH the client.
    // Render the list as a composed LocString text block instead.
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
+ `await player.EditMap`).

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the sibling
  "the stripped reference assemblies hide the truth" convention, for placement rather than
  rendering; both are cases where a compiling server contract hides a client requirement.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — why the readout surface
  is part of the feature; this doc is the constraint list that surface must fit inside.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the same
  "observe, don't assume" discipline applied to a pathfinding invariant; here applied to client
  rendering.
