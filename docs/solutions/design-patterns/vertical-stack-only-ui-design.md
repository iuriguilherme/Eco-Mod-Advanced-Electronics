---
title: Designing a usable panel when the only primitive is a vertical stack
date: 2026-07-27
last_updated: 2026-08-21
category: design-patterns
module: EcoServerMod
problem_type: design_pattern
component: tooling
severity: medium
applies_when:
  - "Building a player-facing panel on a surface that renders one element per row, in declaration order, with no dynamically sized lists"
  - "A feature would add one control per object, or per operation, to an already-crowded panel"
  - "Choosing the size of a fixed pool of compile-time controls that stands in for a dynamic list"
  - "Deciding between one editable control per object and a single cursor over them"
  - "Drawing a mockup for a target whose layout primitives are more restrictive than the mockup medium"
tags: [eco-modding, ui-design, worldobjectcomponent, tab, layout, mockup-fidelity, product-thinking]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Designing a usable panel when the only primitive is a vertical stack

> **Premise corrected 2026-07-27, settled by live test.** The title overstates the constraint. A mod
> tab is **not** limited to full-width stacked elements: most autogen templates render as
> **two-column rows** — label on the left, control on the right — and a few (headers,
> `StringDisplay`, the plaques) go full width. So the horizontal axis is not empty.
>
> What survives intact is the thing that actually drives the design: **panel length is the budget,
> and it is spent one row at a time.** Two-column rows do not change that, because each still costs
> one row. Every rule below is about row count, and every one of them still holds.
>
> What also survives is the *reason* the row budget is fixed: **a dynamically sized list still does
> not render from a mod tab.** `Table` and `ButtonGrid` exist in the client's set, but binding a
> collection to them from a mod component produces an empty container — verified across several
> deployed builds. So a control per object remains a hand-written pool of compile-time RPC methods,
> and the pool-sizing rule below is still load-bearing rather than a workaround.
>
> Read "vertical stack" throughout as "one row per element, in declaration order", not as "one
> column of full-width controls".

## Context

An Eco mod's `WorldObjectComponent` tab renders **one element per row, in member declaration
order**. Each row is typically two columns — label left, control right — and some templates take the
full width; either way an element costs a row and the rows accumulate downward.

What the surface does not give you is **dynamic length**: no grids of generated controls, and no
dynamically sized lists. `Table` and `ButtonGrid` are in the client's template set, but a collection
bound to them from a mod component renders an empty container, so a control per item still means a
fixed pool of RPC methods written out by hand. The set of templates that render at all is a
whitelist (`docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`), and the
binding rules for each are in
`docs/solutions/runtime-errors/autogen-template-binding-contract.md`.

The drone dock's survey panel grew feature by feature until it was a long ladder of buttons and a
wall of text, and the verdict from live play was blunt:

> "Vertical buttons are a terrible design, this is not a smartphone app."

That is the situation this doc is about: not *what renders* (covered by the conventions doc) but
**how to lay out a panel when the budget is vertical space and every feature wants a row.**

The trap underneath it: when length is the budget, every added element is a subtraction from
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

**3. Prefer ONE control over one control per object — corrected 2026-07-31.**
This rule originally said "one control per object, not one per operation", turning `N × M` into `N`.
That is still the right direction and it does not go far enough, because **`N` editable controls over
one field do not work at all.** An interaction writes every editable member back as a batch, in
declaration order, so one click on a per-object control produces N writes and the field ends at
whatever the last member says — for a one-of-N control, always "not me". Six checkboxes over one
assigned-area field measured six setter calls per click and left the field cleared. Full mechanism:
`docs/solutions/runtime-errors/n-editable-members-cannot-share-one-field.md`.

So collapse to **one** control holding the value — a cursor (`Int32` stepper), a picker, or a single
commit action. It costs one row whatever the object count, which removes the row-budget pressure that
motivated a pool in the first place, and reserving one end of its range for "none" makes the inverse
operation free. The toggle advice above survives only in that narrower form: a value control whose
zero position is the inverse, not N toggles.

**4. Split into tabs when you have a second COMMIT action, not when rows accumulate.**
Originally this read "split before you accept scrolling", which is the wrong trigger. Once rule 3 is
applied, rows stop scaling with object count and length rarely forces a split. What forces one is
`BigButton`: it is the panel's commit control, so a panel wants at most one, and a second genuine
action needs its own pane to live in. This panel split into Areas and Results, then collapsed back to
a single Survey tab the moment assignment stopped needing a button of its own — the split's only
remaining job had been hosting one. The dock carries a second tab again today (**Mining**), but that
one is a different job on a different drone, not a second commit action on the same work.

> **This rule is currently violated in shipped code, and the constraint is still in force**
> (owner-restated 2026-08-21: two and three buttons per pane is "not good UI at all"). The Survey tab
> declares three `BigButton`s — Manage Areas on Map, Assign Selected Area, Unassign Area
> (`EcoServerMod/AdvancedElectronics/SurveyComponent.cs:204`, `:217`, `:240`) — and Mining declares
> two (`MiningComponent.cs:94`, `:118`). Each costs about 3.2 standard rows and leaves roughly
> two-thirds of the panel width dead; the measurements are in
> `docs/ideation/2026-07-31-dock-ui-palette.html`.
>
> The intended repair is rule 3 applied once more: Assign and Unassign are not two commits, they are
> one piece of state, so a single `Boolean` costs one row instead of 6.4 and reads as state rather
> than as two commands. That leaves Survey with exactly one commit button and Mining with none.

Note also that **RPC methods render after all properties**, whatever the declaration order, so a
button always lands at the bottom of its pane. That is why rule 1's "put fixed anchors first" cannot
be satisfied with a button.

**5. Show one item at a time behind a cursor, not all items at once.**
Rendering every area's findings makes panel length proportional to area count. A cursor — one line
naming the current item — keeps length constant. The cursor must be **independent of any assignment
state**, so reading item B does not disturb what the machine is doing to item A.

Prev/Next buttons were the first shape tried and are not the one that shipped: two buttons cost two
rows and are two more commit-shaped controls. What shipped is a single `Int32` stepper with a
`Range` — a two-button cursor in one row (`SurveyComponent.cs:147`, `MiningComponent.cs:79`). Read
this rule as "one cursor control", not "Previous and Next".

**6. If a fixed control pool is unavoidable, size it by real use, not by tidiness.**
Rule 3 usually removes the need for a pool entirely, and it did here — the six-button assign pool
described below no longer exists, replaced by the one stepper plus one commit button in rule 5. Keep
this rule for the cases where a pool genuinely cannot collapse to a single control.

When a compile-time pool stands in for a dynamic list, the cap is a **product** decision. The
motivating late-game setup is one survey area per resource — coal, iron ore, limestone, gold ore,
copper ore, each in a different biome — which is five. The pool is six: five plus a spare. A cap of
four would have fit an appealing "no scrolling ever" rule and broken the actual workflow. Fitting
real use beats satisfying a self-imposed layout rule; record the reasoning next to the constant so
the next reader does not "tidy" it back down. Give the overflow a fallback path (a chat command) and
say so in the panel.

**7. Gate controls that have nothing to act on.**
Bind each pooled control's visibility to a synced bool so unused positions do not render as dead
rows, and push the change explicitly or the client never re-evaluates. A pool of six costs six rows
only when six objects exist. This pairs with rule 6 and, like it, has no live instance here any
more — `VisibilityParam` survives only in the detached `UIShowcaseComponent`. It applies again the
moment a pool does.

**8. A filter must cost fewer rows than the noise it removes.**
An earlier attempt added one toggle button per material to filter the readout. It shipped, worked,
and was rejected outright:

> "you added a lot more clutter and this defeats the purpose: selecting what we want to see takes
> several rolling up and down to click the buttons ... too much work and zero benefits"

The replacement was a native multi-select picker — **one** row, opening a popup for the actual
choosing. A control that expands to manage the panel's own length is
usually a net loss; push the interaction into a popup or an editor instead.

**9. A mockup must obey the target's layout primitives.**
A preview drawn in a medium more capable than the target is worse than no preview — it gets
approved, then cannot be built. An HTML mockup of this panel showed Previous/Next side by side. Eco
cannot do that; the buttons shipped stacked, and the reaction was:

> "you tricked me — those buttons are still vertical and not horizontal like the preview, lol"

Before drawing, write down the target's primitives (here: one element per row, declaration order,
no dynamically generated controls) and refuse yourself anything outside them, even when the mockup
medium makes it trivial. The point of a preview is to make the constraint visible, not to hide it.

Note the primitives themselves have to be *verified*, not assumed — this doc originally recorded
"full-width only", which was never tested and turned out to be wrong. An unverified constraint in a
mockup checklist is the same failure as an unbuildable mockup, one level up.

## Why This Matters

On this surface the panel length *is* the design. Every element competes with every other
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

The dock's Survey tab as it ships today (`EcoServerMod/AdvancedElectronics/SurveyComponent.cs`).
Note the ordering consequence of rule 4's closing note — RPC methods render after all properties
whatever their declaration order, so the file declares the buttons last because that is where they
land anyway:

```csharp
// Readouts first: header, the numbered area list, drone status.
[SyncToView, Autogen, UITypeName("StringTitle")]
public string AssignHeader { get; private set; } = "Areas";

[SyncToView, Autogen, UITypeName("StringDisplay")]
public string AreasDisplay { get; private set; } = string.Empty;

// ONE cursor, not a pool -- rules 3 and 5. It picks what you are LOOKING at and nothing else;
// an earlier build assigned on change, so scrolling to read a neighbour reassigned the drone.
[Serialized, Eco, Range(1, MaxSurveyAreas), UITypeName("Int32")]
public int ViewPosition { get; set; }

[SyncToView, Autogen, UITypeName("StringDisplay")]
public string ResultsDisplay { get; private set; } = string.Empty;

// Buttons last. Three of them, which is two more than rule 4 allows -- see the note there.
[RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
public async Task ManageAreasOnMap(Player player) { ... }
```

The cap that used to size a control pool is now just a range on the cursor, so ten areas cost the
same one row as one area (`SurveyComponent.cs:71`, `:147`):

```csharp
public const int MaxSurveyAreas = 10;

[Serialized, Eco, Range(1, MaxSurveyAreas), UITypeName("Int32")]
```

Row-count arithmetic across the three generations of this panel:

```
first:   5 areas x (Create/Edit/View/Delete + select)          -> ~20 rows, all stacked
second:  1 "Manage Areas on Map" + text + 5 assign toggles     ->   7 rows, scales with areas
today:   1 map button + text + 1 cursor + findings + 2 buttons ->   constant, any area count
```

The second generation is what rules 6 and 7 were written for. The third removed the pool entirely
by applying rule 3, which is why those two rules now have no live instance here.

## Related

- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — what actually
  renders on these surfaces; this doc is how to arrange it once you know. That doc's own history is
  the cautionary tail: two of the limits assumed here were later found to be untested, not real.
- `docs/solutions/runtime-errors/n-editable-members-cannot-share-one-field.md` — why rules 3 and 4
  above were corrected: N editable controls over one field destroy each other's writes.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — why the readout is part of
  the feature at all; this doc is the layout budget that readout has to fit inside.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — every judgement above came from
  live play, which is the only place panel-length problems are visible.
- `docs/ideation/2026-07-31-dock-ui-palette.html` — the measured control palette behind rule 4:
  every template drawn at true size, with `BigButton` costed at 3.2 rows against a `Boolean`,
  `Int32` stepper or `Selector` at one.
