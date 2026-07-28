---
title: "Autogen UI templates fail three different ways: blank, missing, or a server-killing Missing RPC call Set<Prop>"
date: 2026-07-27
last_updated: 2026-07-28
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "Clicking a stepper or checkbox in a mod component tab disconnects every player with InvalidOperationException: Missing RPC call Set<PropertyName> for <Component>"
  - "Interacting with the object disconnects the client: Failed to receive views from the server, InvalidCastException: Unable to cast object of type 'ViewClassInfo' to type 'View'"
  - "A member declared with UITypeName renders nothing at all, and takes the members below it down with it"
  - "A template renders but shows the wrong control shape (two fields where one was expected)"
  - "An edited value persists across a restart but the on-screen control never updates until the window is reopened"
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

**Editable scalar** — `[Serialized, Eco]` with a **public setter**. Numerics additionally need
**`Range(min, max)`**; without bounds every edit clamps straight back to the original value, and the
`Range` *template* renders as an empty "0 to 0" interval:

```csharp
[Serialized, Eco, UITypeName("Boolean")]              public bool  BooleanProbe { get; set; } = true;
[Serialized, Eco, Range(0, 100), UITypeName("Int32")] public int   Int32Probe   { get; set; } = 42;
[Serialized, Eco, Range(0, 10),  UITypeName("Single")] public float SingleProbe { get; set; } = 3.5f;
```

`[Eco]` is the attribute the already-working `MaterialTargets` picker uses, and it is what makes the
setter reachable. Verified live: text and numeric edits both save and survive a restart. Vanilla
carries the same bounds — `Server/Eco.Gameplay/Components/Collection/PickupBountyComponent.cs:64`
is `[Eco, Range(-10000, 10000)] public float`. *(That component is newer; the long-stable references
are store prices and law values, which agree.)*

**Live refresh is a fourth requirement, separate from persistence — PROVEN 2026-07-28.** An
auto-property `[Serialized, Eco]` member persists a write and survives a restart, but the on-screen
value never moves until the window is reopened: the stepper clicks, the number does not change, and
the new value only appears after the next restart. `[Eco]` change tracking alone does not refresh a
mod component's view. Declaring `INotifyPropertyChanged` on the component is likewise not enough —
the notification has to be **raised**.

This was settled by an A/B in one deploy: two adjacent `Int32` members, identical but for the setter.

```csharp
// Control -- persists, never refreshes on screen.
[Serialized, Eco, Range(0, 100), UITypeName("Int32")]
public int T_Int32 { get; set; } = 42;

// Refreshes live. An explicit backing field is required, because an auto-property
// gives you no setter body to push from.
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
```

Live updated on screen; the control did not. Both persisted. **So any editable member a player is
meant to watch needs an explicit backing field and a push in the setter** — this is what makes a mod
tab's controls feel alive rather than dead, and it is a per-member cost, not a one-line component fix.

**Two templates LOOK like display and are actually EDITABLE: `LongString` and `StringDescription`.**
Both render as bordered, high-contrast text areas that read as readouts, and both crash on the first
keystroke when given a private setter — `Missing RPC call SetLongStringProbe`, then
`Missing RPC call SetT_StringDescription` for the same reason weeks apart. Declare them
`[Serialized, Eco]` with a public setter. A genuinely read-only multi-line readout needs a different
template; appearance is not evidence of read-only-ness in this vocabulary.

**Some templates render but cannot persist, and no attribute fixes it.** `UITypeName("Color")` draws
a working colour picker from a mod tab, but the chosen colour is gone after a restart:
`Eco.Shared.Utils.Color` (`Server/Eco.Shared/Utils/Color.cs:13`) is a plain `struct` with no
`[Serialized]` attribute of its own, so `[Serialized]` on the *property* has nothing to write.
Vanilla contains **zero** `UITypeName("Color")` usages, so there is no reference shape to copy either.
To offer a colour, store a serializable value (an index or name) and map it to a `Color` for display.

**Unexplained, recorded rather than guessed at:** `StringPlaqueEditable` accepts typed text but does
not persist across a restart, on the same `[Serialized, Eco]` + public-setter shape that works for
`LongString`, `StringInput` and `StringDescription`. Do not assume the shape is sufficient for every
text template.

**Container / list** — `UIListTypeName` on a **collection**, not `UITypeName` on a scalar, and the
element must be a type the client already has a view for. Vanilla's reference shape, driving a whole
grid of buttons from a collection (`Server/Eco.Gameplay/Components/PerformCivicActionComponent.cs:41-42`):

```csharp
[Autogen, SyncToView, EnabledParam(nameof(CivicActionEnabled)), UIListTypeName("ButtonGrid")]
public IEnumerable<Type> AvailableCivicActions => CivicsManager.Obj.GetCivicActionsForWorldObject(...);
```

Two details in that line are easy to miss and both matter: it is a **computed getter**, so the
"must be settable and assigned" rule for scalars does **not** apply to collections; and the element
type is `Type`, a game type with a generated client view.

**However — copying that declaration exactly still crashes the dock window.** Tested across six
builds:

| Elements | Result |
|---|---|
| `SurveyDroneItem`, `DroneDockItem` (mod types) | crash |
| `IronOreItem`, `CoalItem` (vanilla types) | crash |
| container member absent | works |

The reason is a **client type-reconstruction gap**, and it is specific. A vanilla component has a
code-generated view class compiled into the client, so its list property really is
`List<ViewClassInfo>` (`View.cs:337-343`). A mod component has no such class, so the client rebuilds
the type from a name string — and `View.cs:114` maps *every* list to `typeof(List<View>)`, throwing
away the element type the server sent alongside it. Since a `Type` member serializes as
`ViewClassInfo` (`TypeGenerationHelper.cs:62`), the cast fails:

```
System.InvalidCastException: Unable to cast object of type 'ViewClassInfo' to type 'View'.
```

**The operative rule: a mod list's elements must deserialize to `View`.** `Type` does not, and
neither does `string` — the older `IEnumerable<string>` crash names the same target type. Both
element sets tested above were `Type` values, so those six builds never varied the thing that
matters; "vanilla elements crash too, therefore the element type is irrelevant" was a bad inference
from a correct observation. A collection of `IViewController` instances is the untested case.

Practical consequence today is unchanged — no `Table` over item types, no generated `ButtonGrid`, so
a fixed pool of `[RPC]` methods is still how you offer N buttons and multi-column data stays
composed text. But record it as *unsolved*, not *impossible*.

The exception is in the **client** log —
`%USERPROFILE%\AppData\LocalLow\Strange Loop Games\Eco\Player.log`, not
the server's `Logs/`. The client's own crash dialog renders off-screen with only its OK button
reachable, which is what made this look undiagnosable; the full stack trace was on disk throughout.

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
