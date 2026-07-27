---
title: Designing a usable panel when the only primitive is a vertical stack
date: 2026-07-27
category: design-patterns
module: EcoServerMod
problem_type: design_pattern
component: tooling
severity: medium
applies_when:
  - "Building a player-facing panel on a surface that renders only stacked, full-width elements in declaration order"
  - "A feature would add one control per object, or per operation, to an already-crowded panel"
  - "Choosing the size of a fixed pool of compile-time controls that stands in for a dynamic list"
  - "Drawing a mockup for a target whose layout primitives are more restrictive than the mockup medium"
tags: [eco-modding, ui-design, worldobjectcomponent, tab, layout, mockup-fidelity, product-thinking]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Designing a usable panel when the only primitive is a vertical stack

> **⚠ Premise under revision (2026-07-27).** This doc assumes a mod tab can only render stacked,
> full-width elements. That is **false**: the client's autogen set includes `ButtonGrid`,
> `HorzBox` and `Table`, selected server-side by `UITypeName`. The layout *reasoning* below still
> holds wherever the stack is genuinely the constraint, and the row-budget arithmetic is still
> the right way to think — but do not treat "one column" as a hard limit. See
> `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` and
> `docs/ideation/2026-07-27-mod-ui-vocabulary.md`. Full rewrite pending live verification.

## Context

An Eco mod's `WorldObjectComponent` tab renders as a **single column of full-width elements, in
member declaration order**. There are no rows, no columns, no grids, no side-by-side pairs, and no
dynamically sized lists — RPCs are compile-time methods, so a button per item means a fixed pool of
button methods written out by hand. The set of things that render at all is a short whitelist
(`docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`).

The drone dock's survey panel grew feature by feature until it was a long ladder of buttons and a
wall of text, and the verdict from live play was blunt:

> "Vertical buttons are a terrible design, this is not a smartphone app."

That is the situation this doc is about: not *what renders* (covered by the conventions doc) but
**how to lay out a panel when the layout budget is one column and every feature wants a row.**

The trap underneath it: on a single-column surface, every added element is a subtraction from
everything else. A feature can work perfectly, ship, and still make the panel worse. Judging a
control on "does it function" misses the cost it imposes on its neighbours.

## Guidance

**1. Declaration order is reading order — put fixed anchors first.**
Anything the player reaches for repeatedly must sit *above* anything whose length varies. The dock's
map-manager button is declared first precisely because the text below it changes length as areas are
added, surveyed and renamed; an anchor that drifts is one you have to hunt for on every visit.
Conversely, controls whose labels are meaningless until something above is read (position-numbered
buttons that a list names) go *after* that thing.

**2. Move management onto a richer surface instead of growing the stack.**
The largest win was not rearranging buttons — it was deleting them. Create / Edit / View / Delete,
one row each, became a **single** "Manage Areas on Map" button that opens the game's map editor as a
multi-entry manager, where naming, redrawing and deleting are native interactions. One row now does
what four rows did, on a surface that already has real UI. When a stack is over budget, look for an
existing rich surface to delegate to before optimising the stack itself.

**3. Prefer one control per object over one control per operation.**
`N` objects × `M` operations is the row count that kills these panels. Collapsing to one control per
object turns `4N` into `N`. Make the single control a **toggle** where the inverse operation is
obvious — clicking the assigned area's button unassigns it, so "Unassign" needs no row of its own.

**4. Split into tabs before you accept scrolling.**
A second mod component on the same object registers its own tab. Splitting assignment (Areas) from
findings (Results) halved each panel instead of making one panel twice as long. Two short panels beat
one scrolling panel; the tab strip costs nothing in the column.

**5. Show one item at a time with prev/next, not all items at once.**
Rendering every area's findings makes panel length proportional to area count. A cursor — one line
naming the current item, plus Previous/Next — keeps length constant. Note the cursor should be
**independent of any assignment state**, so reading item B does not disturb what the machine is
doing to item A.

**6. Size a fixed control pool by real use, not by tidiness.**
When a compile-time pool stands in for a dynamic list, the cap is a **product** decision. Here the
motivating late-game setup is one survey area per resource — coal, iron ore, limestone, gold ore,
copper ore, each in a different biome — which is five. The pool is six: five plus a spare. A cap of
four would have fit an appealing "no scrolling ever" rule and broken the actual workflow. Fitting
real use beats satisfying a self-imposed layout rule; record the reasoning next to the constant so
the next reader does not "tidy" it back down. Give the overflow a fallback path (a chat command) and
say so in the panel.

**7. Gate controls that have nothing to act on.**
Bind each pooled control's visibility to a synced bool so unused positions do not render as dead
rows, and push the change explicitly or the client never re-evaluates. A pool of six costs six rows
only when six objects exist.

**8. A filter must cost fewer rows than the noise it removes.**
An earlier attempt added one toggle button per material to filter the readout. It shipped, worked,
and was rejected outright:

> "you added a lot more clutter and this defeats the purpose: selecting what we want to see takes
> several rolling up and down to click the buttons ... too much work and zero benefits"

The replacement was a native multi-select picker — **one** row, opening a popup for the actual
choosing. On a single-column surface, a control that expands to manage the panel's own length is
usually a net loss; push the interaction into a popup or an editor instead.

**9. A mockup must obey the target's layout primitives.**
A preview drawn in a medium more capable than the target is worse than no preview — it gets
approved, then cannot be built. An HTML mockup of this panel showed Previous/Next side by side. Eco
cannot do that; the buttons shipped stacked, and the reaction was:

> "you tricked me — those buttons are still vertical and not horizontal like the preview, lol"

Before drawing, write down the target's primitives (here: full-width only, stacked, declaration
order) and refuse yourself anything outside them, even when the mockup medium makes it trivial. The
point of a preview is to make the constraint visible, not to hide it.

## Why This Matters

On a single-column surface the panel length *is* the design. Every element competes with every other
for the same budget, so the usual per-feature test — "does this control work?" — is the wrong test.
Two features that each pass it can combine into a panel nobody wants to use, which is exactly how
this one got to a ladder of buttons: no individual step was wrong.

The rework did not add capability. Assignment, area management and findings all existed before. It
moved work off the stack (to the map editor), off the panel (to a second tab), and off the row count
(one toggle per area instead of four buttons per area) — and the result was judged better while doing
the same things. That is the shape of the win to look for.

The mockup lesson is the expensive one. An unbuildable preview does not fail loudly; it converts
into an approved design, and the gap only surfaces after implementation, when the cost of changing
direction is highest. Fidelity to the target's constraints matters more in a mockup than visual
polish does.

## When to Apply

- Before adding any element to a panel that renders as a single column — ask what its row cost buys
  relative to everything it pushes down.
- When a feature's natural shape is "a control per object" and the object count is player-controlled.
- When choosing a fixed pool size for compile-time controls: derive it from a real workflow, write
  the reasoning next to the constant, and provide an overflow path.
- Before drawing any mockup for a constrained target: enumerate the target's layout primitives first
  and stay inside them.
- When a panel is judged bad but every individual feature in it works — the problem is usually
  aggregate length, not any one control.

## Examples

The dock's Areas tab, ordered so the anchor never moves
(`EcoServerMod/AdvancedElectronics/SurveyAreasComponent.cs`):

```csharp
// 1. Fixed anchor FIRST -- the text below changes length as areas are added and surveyed.
[RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
public async Task ManageAreasOnMap(Player player) { ... }   // one row replaces Create/Edit/View/Delete

// 2. Assignment line, drone status, numbered area list -- variable length, so it sits below.
[SyncToView, Autogen, UITypeName("StringDisplay")]
public string AreasDisplay { get; private set; } = string.Empty;

// 3. One toggle per area, gated so unused positions do not render.
[SyncToView] public bool AreaExists1() => this.AreaCount() >= 1;

[RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists1)),
 UITypeName("BigButton"), Description("Assign Area 1")]
public void AssignArea1(Player player) => this.ToggleAssign(1);   // clicking the assigned one unassigns
```

The pool size as a recorded product decision, not a magic number
(`SurveyAreasComponent.cs:40-48`):

```csharp
/// Size of the compile-time assign-button pool. RPCs are methods, so SOME ceiling has to exist
/// -- buttons cannot be generated per area. Six is a product choice, not a technical one: the
/// motivating late-game setup is one area per resource (coal, iron ore, limestone, gold ore,
/// copper ore), each in a different biome, which is five with one spare. Four would fit without
/// scrolling but would not fit that setup, and fitting real use beats a self-imposed no-scroll
/// rule. Raise it if mod users ask for more. Areas past it are assigned with /drone assignarea.
public const int AssignButtonPool = 6;
```

Row-count arithmetic for five areas, before and after:

```
before:  5 areas x (Create/Edit/View/Delete pattern + select)  -> ~20 rows, all stacked
after:   1 "Manage Areas on Map" + 1 text block + 5 toggles    ->   7 rows
         findings moved to a second tab, one area at a time    ->   panel length now constant
```

## Related

- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — what actually
  renders on these surfaces; this doc is how to arrange it once you know. That doc's own history is
  the cautionary tail: two of the limits assumed here were later found to be untested, not real.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — why the readout is part of
  the feature at all; this doc is the layout budget that readout has to fit inside.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — every judgement above came from
  live play, which is the only place panel-length problems are visible.
