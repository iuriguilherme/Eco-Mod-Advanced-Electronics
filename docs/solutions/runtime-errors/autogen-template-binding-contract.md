---
title: "Autogen UI templates fail three different ways: blank, missing, or a server-killing Missing RPC call Set<Prop>"
date: 2026-07-27
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "Clicking a stepper or checkbox in a mod component tab disconnects every player with InvalidOperationException: Missing RPC call Set<PropertyName> for <Component>"
  - "A member declared with UITypeName renders nothing at all, and takes the members below it down with it"
  - "A template renders but shows the wrong control shape (two fields where one was expected)"
root_cause: wrong_api
resolution_type: code_fix
applies_when:
  - "Choosing a UITypeName for a property on a mod WorldObjectComponent tab"
  - "Trying to render a list, table or button grid from a mod component"
  - "A template name is known to exist in the client but produces nothing on screen"
tags: [eco-modding, autogen, uitypename, uilisttypename, worldobjectcomponent, client-rendering]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Autogen UI templates fail three different ways: blank, missing, or a server-killing `Missing RPC call Set<Prop>`

> **Path convention.** Paths beginning `Server/` or `Client/` refer to the **Eco game source in the
> local Eco checkout, external to this repo** — they will not resolve here, by design. Paths
> beginning `EcoServerMod/` or `docs/` are in-repo.

## Problem

The Eco client renders a mod's component tab by picking a prefab **per property, by name**, from a
68-entry set. Naming a template that exists is only one third of the binding — the **attribute**,
the **member type**, and (for interactive templates) a **reachable setter** must all match. Get any
one wrong and it fails in a different, non-obvious way, including one that takes the server down.

## Symptoms

Observed live on 2026-07-27 from a probe component exposing thirteen templates at once.

**1. Editable template + unreachable setter → server exception, all players disconnected.**

```
Exception: InvalidOperationException
Message: Missing RPC call SetRangeProbe for UIShowcaseComponent
Source: Eco.Core
  at Eco.Core.Controller.ControllerManager.HandleViewRPC(...)
```

Triggered by clicking the `+` on a stepper. The property was declared
`[SyncToView, Autogen, UITypeName("Range")]` with a `private set`. The client renders the control,
the player interacts, and the client calls back a `Set<PropertyName>` RPC that does not exist.
Nothing fails until someone clicks — the tab looks perfectly healthy.

**2. Wrong attribute for a container → blank, and it swallows following members.**

`UITypeName("ButtonGrid")` and `UITypeName("HorzBox")` on `string` properties rendered nothing, and
the two `BigButton` RPCs declared *after* them also failed to appear. The tab showed only the text
member above them. No error, client-side or server-side.

**3. Wrong member type → renders, but as the wrong shape, or not at all.**

`UITypeName("Range")` on a `float` drew **two** steppers ("0 to 0") — it wants a range-shaped
value, not a scalar. `UITypeName("NestedMeter")` on a `float` rendered nothing at all.

## What Didn't Work

Assuming a template name is a complete specification. It is not. The template set was discovered by
reading the client, and the natural next step — put the name on a property and see — silently
conflates three independent questions. A blank member reads as "this template doesn't work for
mods", which is the wrong conclusion and the same misattribution this project has now made three
times.

Guessing type bindings also failed twice in one pass (`Range`, `NestedMeter`). Grepping the game
source for a real usage of the template settles it immediately and should be the first move, not the
fallback.

## Solution

Match all three parts. The rules, each grounded in a working usage:

**Display scalar** — `[SyncToView, Autogen, UITypeName("X")]`, settable property, assigned a value,
and `Changed(nameof(...))` pushed. A never-assigned computed getter draws blank.

**Editable scalar** — declare it `[Eco]` with a **public setter**:

```csharp
[Eco, UITypeName("Boolean")] public bool BooleanProbe { get; set; } = true;
[Eco, UITypeName("Int32")]   public int  Int32Probe   { get; set; } = 42;
```

`[Eco]` is the attribute the already-working `MaterialTargets` picker uses, and it is what makes the
setter reachable. *(Deployed as the fix for symptom 1; not yet re-verified live at time of writing.)*

**Container / list** — `UIListTypeName` on a **collection**, not `UITypeName` on a scalar, and the
element must be a type the client already has a view for. Vanilla's reference shape, driving a whole
grid of buttons from a collection (`Server/Eco.Gameplay/Components/PerformCivicActionComponent.cs:41-42`):

```csharp
[Autogen, SyncToView, EnabledParam(nameof(CivicActionEnabled)), UIListTypeName("ButtonGrid")]
public IEnumerable<Type> AvailableCivicActions => CivicsManager.Obj.GetCivicActionsForWorldObject(...);
```

Two details in that line are easy to miss and both matter: it is a **computed getter**, so the
"must be settable and assigned" rule for scalars does **not** apply to collections; and the element
type is `Type`, a game type with a generated client view — a mod-defined element type has none,
which is what made an earlier `IEnumerable<string>` attempt crash with
`Cannot convert String to View`.

## Why This Works

Autogen is a *binding*, not a renderer directive. The name selects a prefab; the prefab expects a
particular data shape and, if interactive, a particular write path. The three failure modes map
exactly onto the three halves of that contract being unsatisfied — which is why they present so
differently:

- **Missing write path** → renders fine, dies on interaction (the client happily builds a control it
  cannot deliver input for).
- **Wrong attribute** → the property is not recognised as a list member at all, so the list template
  gets no data and the member is dropped; neighbouring members can be dropped with it.
- **Wrong type** → the prefab binds against fields the value does not have, yielding a partial or
  empty control.

## Prevention

- **Before using a template name, grep the game source for a real usage** and copy the whole
  declaration — attribute, member type, getter-vs-setter. One `rg 'UIListTypeName\("ButtonGrid"\)'`
  answered in seconds what two rounds of guessing did not.
  Scope the search to `Server/Eco.Gameplay` — a whole-tree grep times out.
- **Probe display and interaction as separate questions.** A tab that renders is not a tab that
  works; nothing in symptom 1 is visible until a player clicks.
- **Quarantine risky templates in their own component.** Each `WorldObjectComponent` gets its own
  tab, so a container experiment that blanks a tab costs that tab only. This is what kept twelve
  answered questions when the container half failed.
- **Read a blank member as "binding mismatch", never as "not available to mods."** All three
  symptoms here produced no error at all in the client log.

## Related

- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the surface
  whitelist and the 68-template vocabulary this contract applies to.
- `docs/ideation/2026-07-27-mod-ui-vocabulary.md` — the ranked list of what the vocabulary unlocks,
  and the probe these findings came from.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — batching thirteen templates
  into one restart is what made three distinct failure modes visible in a single pass.
