---
title: "An empty marker component is a client-UI feature flag — declare it, or the vanilla tab renders without its controls"
date: 2026-08-21
category: conventions
module: EcoServerMod
problem_type: convention
component: tooling
severity: medium
applies_when:
  - "A vanilla Eco object shows a UI affordance your mod object's otherwise-identical tab does not"
  - "Adding a stock component tab (Storage, Links, Power) to a mod WorldObject"
  - "An engine component has an empty body and is declared [Serialized, ForceCreateView]"
  - "Tempted to subclass or swap a stock component to change what its tab renders"
  - "A chat command is standing in for a control the stock client should already offer"
symptoms:
  - "Drone Dock Storage tab lists linkable inventories but renders no per-target Take From / Put Into checkboxes"
  - "A vanilla Desalinator shows the same controls with no apparent extra component in its visible source"
  - "Workaround chat command `/drone link` and a knowingly-wrong wide auto-link default were shipped in place of the missing control"
root_cause: incomplete_setup
resolution_type: code_fix
tags: [eco-modding, worldobject, requirecomponent, marker-component, client-ui, storage, linkcomponent, engine-source, falsification]
related_components: [EcoServerMod/AdvancedElectronics]
---

# An empty marker component is a client-UI feature flag

Paths beginning `Server/` or `Mods/__core__/` are Eco's own trees — the engine source checkout and
the dedicated server's shipped core mod — not files in this repository. Everything else is
repo-relative. Engine line numbers come from a local read-only checkout on branch `release`
(HEAD dated 2026-08-21); the deployed dedicated server is 0.14.0.3 and may sit behind it.

## Context

The mod's Drone Dock is a hauling machine: a mining drone flies out, fills its hold, comes home, and
unloads into whatever storage the dock is linked to. Choosing which nearby containers it unloads
into is the single most important control the object has.

The dock rendered a Storage tab. The tab listed the nearby linkable inventories correctly. It
rendered no per-target **Take From / Put Into** checkboxes and no header-level Take All / Put All.
A vanilla Desalinator standing a few blocks away rendered the same tab *with* those controls.

Because the affordance was missing, the mod shipped a chat command as a stopgap — `/drone link <n>`,
which lists targets and toggles one by number
(`EcoServerMod/AdvancedElectronics/DroneCommands.cs:44-97`). It sets exactly the two flags the
missing checkboxes set:

```csharp
link.SetObjectInput(user, chosen.Storage, turnOn, userModified: true);
link.SetObjectOutput(user, chosen.Storage, turnOn, userModified: true);
```

That is the whole shape of the bug, visible in the workaround before anyone understood it: **the
state existed, the server honoured it, and only the control was absent.** Nothing about linking was
broken. The player just had no way to touch it without typing.

This matters beyond one component name because of the constraint the mod runs under. The Eco client
is IL2CPP and loads no mod assemblies, so a mod cannot write client UI, cannot debug client UI, and
cannot read the code that decides what to draw (see
`docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`). Every client
behaviour is a consequence of what the server declares. When such a client draws less than you expected, you cannot step into it — you
can only find the declaration it keys off.

### Symptoms

- The tab header read `LINKABLE INVENTORIES (PERSONAL)`; a vanilla Store beside it read `(SHARED)`.
  The header is a red herring — it comes from `LinkComponent.SharesSettingsFor`
  (`Server/Eco.Gameplay/Components/LinkComponent.cs:84`, synced at `:86`), which a subclass
  inherits, so it cannot tell you which component type an object actually carries.
- The target list itself was correct and complete. Only the toggles were missing.
- **No error anywhere.** No server exception, no client error, no missing-asset warning. The tab was
  drawn, just with fewer widgets. Absence of the control was the only signal.
- The corresponding server state was present the whole time. `LinkComponent.LinkSettings` carries
  `Input` and `Output`, both `[Serialized, SyncToView]` and both defaulting to `true`
  (`LinkComponent.cs:579-589`), and the per-player dictionary of storages-with-settings is itself
  `[SyncToView]` (`:75`). The client was already being told the values of the checkboxes it was not
  drawing.

## Guidance

**The discriminator is an empty marker component**, and the engine says what it is for in its own
comment (`Server/Eco.Gameplay/Components/Storage/StorageComponent.cs:32-36`):

```csharp
//Client will check if a storage has this component then it will enable Input/Output mode for the UI. It currently work like a "flag".
[Serialized, ForceCreateView]
[NoIcon]
public class InOutLinkedInventoriesComponent : WorldObjectComponent
{}
```

No members, no logic, no server behaviour. `[ForceCreateView]` makes it reach the client even though
it has nothing to sync; `[NoIcon]` keeps it out of the tooltip. Its entire function is to be present.

The fix on the mod's world object is one attribute
(`EcoServerMod/AdvancedElectronics/DroneDock.cs:129-137`, shipped in `e21ae76`, merged in `e9e38bb`,
released as `v0.3.0`):

```csharp
[RequireComponent(typeof(InOutLinkedInventoriesComponent))]
```

`Initialize()` was not touched. No runtime code participates in enabling the controls: the cure is
purely declarative.

### The rule

A closed client deciding whether to draw a widget needs exactly one thing: **a declared, synced,
unambiguous flag.** It cannot see whether anything on the server consumes links — it has no
`CraftingComponent` behaviour and no `FilterComponent` logic, only a component list. So when a stock
tab renders without an affordance a vanilla object has, look for a component whose presence *is* the
answer, not for a component whose type or behaviour implies it.

### Marker components are recognisable by shape

- empty body (`{}`), deriving from `WorldObjectComponent`
- `[ForceCreateView]` — it must reach the client despite syncing nothing
- often `[NoIcon]` — it is not a player-facing capability
- frequently a comment explaining it "works like a flag"

```bash
# Marker components usually explain themselves.
grep -rn "Client will check\|work like a .flag.\|ForceCreateView" Server/Eco.Gameplay --include=*.cs

# An empty WorldObjectComponent is a flag by construction.
grep -rnA2 "class .*Component : WorldObjectComponent$" Server/Eco.Gameplay --include=*.cs | grep -B2 "^\s*{}"
```

### Compare transitive closures, not attribute blocks

**The discriminator was never visible in any object's own attribute list.** `[RequireComponent]` is
recursive — `WorldObjectUtil.GetRequiredComponents` walks each required component's own requirements
(`Server/Eco.Gameplay/Objects/WorldObjectUtil.cs:236-249`) — so a world object's effective component
set is a transitive closure. Comparing the attribute blocks at the top of two classes compares the
wrong thing.

Vanilla also routinely splits a declaration between a generated file and a hand-written partial. The
Desalinator's AutoGen half (`Server/Mods/__core__/AutoGen/WorldObject/Desalinator.cs:52-60`) declares
none of the relevant components; the hand-written partial adds one
(`Server/Mods/__core__/Objects/Desalinator.cs:13`):

```csharp
[RequireComponent(typeof(FilterComponent))]
public partial class DesalinatorObject
```

and `FilterComponent`'s own requirements are where the marker finally appears
(`Server/Eco.Gameplay/Components/FilterComponent.cs:65-71`):

```csharp
[RequireComponent(typeof(LiquidConverterComponent))]
[RequireComponent(typeof(SharedLinkComponent))]
[RequireComponent(typeof(InOutLinkedInventoriesComponent))]
```

Two objects whose visible attribute lists differ by one line may differ by a dozen components.
Compute the closure — or at minimum open every partial and every required component's own
declaration — before believing a one-line difference explains a behaviour.

### Reframe from "what do they have in common?" to "what consumes this?"

The first question has as many answers as there are components and no way to rank them. The second
has a bounded, greppable answer set. Link consumption goes through two methods on `LinkComponent`:
`GetSortedLinkedComponents` (`LinkComponent.cs:222-228`, whose `source`/`target` parameters filter on
exactly `Settings.Input` and `Settings.Output`) and `GetLinkedStoragesWithSettings` (`:248-254`).
Grepping every caller outside `LinkComponent.cs` yields a small closed set: `FilterComponent`,
`SortingComponent`, `RecyclingComponent`, `MixingComponent`, `ForSaleComponent`, the trade-offer
helper `Store/IHasTradeOffers.cs:55`, and `Settlements/Annexation/AnnexationManager.cs:137`. Reading
each one's requirement block finds the marker immediately.

Caveat on exhaustiveness: `CraftingComponent` reaches links through a cached `Link` property instead
(`CraftingComponent.cs:153`, `:358`), so name-grepping those two methods finds most consumers but not
all.

This reframing is what ended a four-theory investigation in one grep.

## What didn't work

Four theories. Each was plausible, each was tested, each was wrong. The *sequence* is the lesson:
the investigation kept inferring from properties observable from outside the client instead of
finding what the client actually tests.

### Theory 1 — "the controls need a link-*consuming* component (crafting or a store)"

Every remembered vanilla object with the checkboxes was a crafting table or a store — something that
pulls from and pushes to linked inventories. A dock does neither in the vanilla sense.

**Falsified by one counterexample:** the Desalinator's generated half declares a link component and
no crafting or store component at all (`AutoGen/WorldObject/Desalinator.cs:52-60`), and it shows the
controls.

**Residual truth worth keeping.** The theory was not random — crafting tables *do* reliably have the
controls. It had the causality backwards, the most common way a correlation-shaped theory fails.
`CraftingComponent` does not grant the controls; it *requires* the thing that does
(`CraftingComponent.cs:83`). A theory that is right about the population and wrong about the
mechanism survives a lot of confirming evidence.

### Theory 2 — "a mod *subclass* of the link component is invisible to the client's renderer"

The dock carried `DroneDockLinkComponent : SharedLinkComponent`
(`EcoServerMod/AdvancedElectronics/DroneDockLink.cs`), a mod-defined type in an assembly the client
never loads. A client that binds a view to a component's *own* type would have no view for a type it
has never heard of. This is a good theory for an unmoddable client and it explains the symptom
exactly.

**Falsified by live play:** a dock placed fresh while the class required the *stock*
`SharedLinkComponent` still showed no controls.

**What it cost.** Testing it required a deploy, a restart, and a human standing in front of the
object — and it triggered the startup crash described below. It also left residue: the dock still
declares the stock `SharedLinkComponent` (`EcoServerMod/AdvancedElectronics/DroneDock.cs:128`) and
the comment above that line still asserts the falsified theory as fact —

> "A mod SUBCLASS of SharedLinkComponent does not, which is why this names the stock type: the
> controls bind to the component's own view type and a mod-defined type has none."
> (`DroneDock.cs:120-123`)

— nine lines above the real fix, whose own comment says the opposite ("Nothing else grants them: not
the link component's type", `DroneDock.cs:133`). The file contradicts itself, and the wide auto-link
default that `DroneDockLinkComponent` existed for was traded away to satisfy a theory that turned out
to be wrong. **A falsified theory does not automatically leave the codebase when it leaves your
head.** Grep for its traces when you close one out.

### Theory 3 — "`SharedLinkComponent` versus plain `LinkComponent` is the discriminator"

The clean-looking vanilla A/B was Ashlar Basalt Fireplace (`AshlarBasaltFireplace.cs:52-60`) against
Desalinator (`AutoGen/.../Desalinator.cs:52-60`): near-identical component lists differing in
`LinkComponent` versus `SharedLinkComponent`, and only the Desalinator shows the controls.

**Falsified in both directions.** The Robotic Assembly Line declares a *plain* `LinkComponent`
(`Server/Mods/__core__/AutoGen/WorldObject/RoboticAssemblyLine.cs:56`) and has the controls; the dock
had `SharedLinkComponent` and did not.

**The trap.** The A/B pair was not minimal, only *nearly* minimal — and, per the closure rule above,
its real difference was hidden in a partial neither side of the comparison opened. Two objects are
enough to generate a hypothesis and never enough to confirm one; the confirming step is a third
object chosen specifically to break the candidate, not a second that agrees.

### Theory 4 — "`MinimapComponent` correlates"

Comparing the declared component lists of six objects, `MinimapComponent` matched the behaviour
perfectly:

| Object | Link component | Minimap | Controls |
|---|---|---|---|
| Ashlar Basalt Fireplace (`AshlarBasaltFireplace.cs:52-60`) | `LinkComponent` | no | no |
| Storage Chest (`StorageChest.cs:51-56`) | `LinkComponent` | no | no |
| Research Table (`ResearchTable.cs:51-58`) | `LinkComponent` | yes | yes |
| Robotic Assembly Line (`RoboticAssemblyLine.cs:53-65`) | `LinkComponent` | yes | yes |
| Desalinator (`AutoGen/.../Desalinator.cs:52-60`) | `SharedLinkComponent` | yes | yes |
| Drone Dock (`DroneDock.cs`, before the fix) | `SharedLinkComponent` | no | no |

Six for six, and it *survived* the counterexample that killed theory 3. It had no causal story
whatsoever: nothing about being drawn on a map should decide whether a checkbox is drawn in a panel.

**Falsified from live play:** storage chests and stockpiles have no minimap component and work fine
as link targets.

**This is the most instructive failure of the four.** A perfect correlation across six samples was
still wrong, because all six came from the same confounded population: `MinimapComponent` marks
"machines worth finding again", and machines worth finding again are processing and crafting machines
— which transitively require the component that actually matters. The correlation was a shadow of the
real cause, cast by vanilla's content conventions. **Sample size does not repair a missing
mechanism.** A theory that cannot name the code that reads the property is a placeholder, not a weak
theory to be strengthened with more samples.

For the record, the dock carries `MinimapComponent` for a player-facing reason — docks are placed
away from settlements and a drone that stops reporting is a dock you have to walk to
(`DroneDock.cs:61-63`). It is not what turned the controls on.

### The detour: swapping a required component took the whole server down

While testing theory 2, `[RequireComponent(typeof(DroneDockLinkComponent))]` on `DroneDockObject` was
changed to `[RequireComponent(typeof(SharedLinkComponent))]`. Docks already saved in the world carried
the old component. On the next boot the server died:

```
System.NullReferenceException
   at Eco.Mods.TechTree.DroneDockObject.Initialize()
   at Eco.Gameplay.Objects.WorldObject.DoInitializationSteps(Boolean fistTimeAdded)
```

`Initialize()` contained a bare dereference:
`this.GetComponent<LinkComponent>().Initialize(LinkRadius);`

Two facts make this fatal rather than merely broken, and both are worth keeping regardless of what
caused the null:

1. **`Initialize()` runs on every saved object at every server start**, in bulk
   (`Server/Eco.Gameplay/Objects/WorldObjectManager.cs:161` calls `DoInitializationSteps` for every
   object in the world; step order at `WorldObject.cs:537-546`). An exception raised there is not one
   broken dock — it is a server that does not finish starting.
2. **`GetComponent<T>` is assignability-based, so it normally *would* have found the old subclass**
   (`Server/Eco.Gameplay/Objects/WorldObjectComponent.cs:189-198` matches with
   `componentType.IsInstanceOfType(component)`). The lookup was not the problem; the object genuinely
   had no link component of any kind by the time `Initialize()` ran.

**The mechanism is unresolved.** An earlier explanation recorded in this repo's own history — that a
newly-required component is not attached in time for the object's `Initialize()` — is contradicted by
the current engine source, where `ValidateComponents()` runs *before* `Initialize()` in
`DoInitializationSteps` (`WorldObject.cs:537-546`) and adds missing components at `:490-496`. Do not
repeat that explanation. What is established is that reconciliation happens in
`WorldObject.ValidateComponents` (`:457-533`), which computes the required set from the *current*
type, adds what is missing, then removes anything not in it (`:522`); the checkout read for this
document carries two guards written for exactly this hazard — a required entry counts as satisfied
only by a component that will survive the removal pass, "so a doomed component can't mask a missing
one" (`:485-486`), and additions run before removals "so a component replaced by a newly required
derived type finds its successor" (`:489`). Whether the deployed 0.14.0.3 build contains those guards
was not established.

What shipped is the guard (`EcoServerMod/AdvancedElectronics/DroneDock.cs:550-555`):

```csharp
// Guarded: an NRE here aborts server startup entirely rather than degrading one dock.
if (this.TryGetComponent<LinkComponent>(out var link))
    link.Initialize(LinkRadius);
```

## Why this matters

Once the marker is known, everything that looked like a rule falls out of it as a consequence:

- **`CraftingComponent` requires it** (`CraftingComponent.cs:83`) — which is why every crafting table
  has the controls, and why theory 1 kept finding confirming examples.
- **`FilterComponent` requires it** (`FilterComponent.cs:67`) — the Desalinator's entire route to the
  controls, via a hand-written partial that theory 3's A/B comparison never opened.
- **`SortingComponent`** (`:71`), **`RecyclingComponent`** (`:60`), **`StoreComponent`**
  (`Store/StoreComponent.cs:50`) and **`MintComponent`** (`:38`, with a candid comment that a Mint
  only has output and should really have a narrower flag) require it too.
- **`ForSaleComponent`** shows the pattern from the other side: it merely *may* have it
  (`ForSaleComponent.cs:103-104`) and creates it on demand when an object is actually put up for sale
  (`:432-433`, `GetOrCreateComponent<InOutLinkedInventoriesComponent>()`). The flag is not tied to a
  type — it is tied to whether input/output routing is meaningful for that object *right now*.

So the population theory 1 spotted is real: consumers of links do have the controls. They have them
because each one independently declares the marker, not because consuming links is what the client
checks.

The rest of the machinery was already correct and already working, which is why nothing errored:
`LinkSettings.Input` / `Output` were being synced all along (`LinkComponent.cs:585-586`), the
server-side consumers were filtering on them (`:225`), and the chat command was writing them. Only
the affordance was gated. **A missing affordance is not a missing feature — check whether the state,
the sync and the server-side honouring are already in place, because if they are, you are looking for
a flag, not for behaviour.**

## When to apply

Reach for this when a stock tab on a mod object renders but renders *less* than a vanilla object's,
with no error of any kind. In order:

1. **Diff the vanilla object's transitive component closure against yours, in engine source.** Zero
   restarts. This is the step that should come first every time, and in this investigation it came
   last.
2. **Grep for what consumes the state the missing control edits.** Each consumer is a short file
   whose own requirement block you can read.
3. **Only then deploy.** A theory that cannot name the code that reads the property does not get a
   deploy — it gets a grep. Live tests here are paid for by a person quitting the client and waiting
   (`docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`), which puts a hard price on guessing.
4. **One counterexample is enough; a perfect correlation is not.** When you have a correlation with no
   mechanism, go looking for the confound — vanilla content conventions correlate everything with
   everything.

Three habits this cost enough to be worth stating separately:

- **Guard component lookups inside `Initialize()`.** Use `TryGetComponent` rather than dereferencing
  `GetComponent<T>()`. The blast radius of an exception there is the whole server's startup, not one
  object.
- **Treat a required-component type swap as a save migration, not an edit.** Changing a required
  component to its base, its subclass, or a sibling means every already-placed object goes through
  load-time reconciliation with a mismatch between the type it was saved with and the type it is now
  required to have. See
  `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`.
- **Delete the theory from the code when you delete it from your head.** Falsified explanations
  survive in comments and in the changes made to satisfy them. Here the shipped dock still explains
  its component choice with theory 2 (`DroneDock.cs:120-123`) and still pays theory 2's price — the
  stock `SharedLinkComponent` in place of the subclass whose wide auto-link default was traded away —
  leaving `EcoServerMod/AdvancedElectronics/DroneDockLink.cs` attached to nothing while carrying a
  long rationale for a decision the code no longer makes. `/drone link`
  (`DroneCommands.cs:44-97`) is likewise a stopgap whose stated exit condition has now been met.
  Closing an investigation includes grepping for its casualties.

## The discovery instrument

`InOutLinkedInventoriesComponent` is an empty type whose only documentation is a source comment. It
has no server-side API surface to discover and nothing to find in a stripped reference assembly. The
Eco **source checkout** is what made this findable at all — not a convenience, the instrument. See
`docs/solutions/conventions/excluding-third-party-from-a-unity-mod-repo.md` for how this repo resolves it without committing
machine-local paths.

## Related

- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the standing constraint that makes this class
  of bug possible: no client code, so every visual is a consequence of a server declaration. Its
  storage entry is about whether a *slot* renders at all; this doc is about a slot's optional
  controls, gated by a different component's presence.
- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md` — why the attribute list is the authority
  every saved object is rewritten to match. The load-time pass described there is the same pass the
  startup crash happened inside.
- `docs/solutions/runtime-errors/naming-a-component-hides-it-from-its-vanilla-consumer.md` — the other case where
  a component's *identity* rather than its behaviour decided whether the engine found it.
- `docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md` — the neighbouring idea that some declarations carry no
  behaviour at all and exist purely to be read by something else.
