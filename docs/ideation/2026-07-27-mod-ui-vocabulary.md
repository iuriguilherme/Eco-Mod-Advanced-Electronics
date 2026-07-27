# Ideation — UI improvements unlocked by the stock autogen vocabulary

**Date:** 2026-07-27
**Focus:** what new UI a mod can accomplish, given access to the Eco Client project and SLG's internal wiki
**Status:** grounded; no live verification yet

---

## Grounding context

Two evidence scouts ran against sources outside this repo: SLG's internal developer wiki
(`../../Eco.wiki`) and the Eco Client Unity project (`../../Eco/Client`). Dossiers:
`evidence-wiki-ui.md`, `evidence-client-panels.md` (session scratch, not committed).

The investigation settled two questions that had been conflated.

### The ceiling is real and hard

A mod **cannot** add UI prefabs or client code. Confirmed on three independent mechanisms:

- Panel prefabs come from a **serialized `GameObject[]` on a client prefab** — a fixed array, not
  a registry anything can join.
- View types are discovered from `AppDomain.CurrentDomain.GetAssemblies()`
  (`Client/.../LookupAssemblies.cs:17`).
- `InjectionPreventer.cs:78` **quits the game** on a non-whitelisted assembly.
- `ModBundleManager.cs:241-315` classifies bundle assets into exactly ModObject / ModImage /
  ModItem / ModBenefit / BlockSet / Font / ChatEmote / ImageContainer — **there is no UI branch**.
  Bundle content reaches client UI only as icons, sprites and fonts; arbitrary Canvas+TMP is
  possible only world-space on a ModObject.

Also worth knowing: `[WorldObjectUIPanel]` — the mechanism this project's render-surfaces doc names
as the thing mods can't have — is marked **deprecated** in the client source
(`Scripts/View/WorldObjectView.cs:13`). It is a dead end for SLG too.

### The floor is far higher than we assumed

A server component flagged `CreateComponentTab`/`Autogen` gets a tab with **zero client code**,
rendered by `WorldObjectPanelDefault.prefab` → `AutoViewComponentUI` → `AutoSetupUI`, which picks a
prefab **per property, by name** (`WorldObjectUI.cs:259-273`). The steering wheel is entirely
server-side: `UITypeName`, `UIListTypeName`, `ComponentTabName` — and those literals ship in the
ModKit's `Eco.Shared.dll`.

The vocabulary is **68 prefab names** in `PrefabsCollection.viewUIs`
(`Client/Assets/UI/Prefabs.prefab`) — matching the count the Eco maintainer quoted — plus 4
special-cased in code. **We use about four of them.**

From the wiki, on why this works for mods at all (`UI-System.md:53`):

> "it works with mods. Since the type data for the views is sent over on server connect, you don't
> have to recompile the client to get it... This gives lots of flexibility for adding new
> configuration options in mods."

So: **reuse-only, but the reusable set is 68 wide.**

---

## What this overturns

Three constraints this project designed around are false as stated:

| Recorded constraint | Reality |
|---|---|
| "Stacked full-width only — no rows, columns or grids" | `ButtonGrid`, `HorzBox`, `Table` exist |
| "A rich list/table needs client UI the ModKit can't provide" | `Table`, `IEnumerable`, `ButtonList`, `ExpandableList` exist |
| "Limited to text + buttons + editable scalars" | `Range`, `Boolean`, `Color`, `NestedMeter`, `ItemInput`, header variants |

It also re-explains an old failure. A `[SyncToView] IEnumerable<string>` crashed the client with
`Cannot convert String to View`, and we concluded *lists are impossible*. An `IEnumerable` template
exists — so the fault was the **element type**, not the list. Same misattribution shape as the
mod-tag bug: right symptom, wrong layer.

---

## Survivors, ranked, in two risk tiers

### Tier A — low risk (layout and scalars; no element-type question)

These only require naming a different template on an existing property.

1. **Horizontal controls via `ButtonGrid` / `HorzBox`.** Directly answers the standing complaint
   that vertical button stacks are "not a smartphone app", and would make the Prev/Next pair sit
   side by side — the exact layout an earlier mockup promised and could not deliver.
2. **Coverage as `NestedMeter` instead of `"42%"` text.** Small change, disproportionate perceived
   quality, and coverage is the number players read most.
3. **Structure with `SectionHeader` / `LinedHeader` / `GeneralHeader`.** The Areas tab currently
   relies on declaration order alone to imply grouping; headers make it explicit and reduce the
   pressure that forced the fixed-anchor ordering rule.
4. **`StringDescription` / `LongString` / `StringPlaque` for findings text.** Richer than
   `StringDisplay` for multi-line content, which is exactly what findings are.
5. **`Range` for bounded numbers** (survey depth, plot cap) and **`Boolean` for toggles** — replaces
   button-as-toggle patterns with real controls.
6. **`Color` for per-area colour**, tying the dock list to the colours already cycled onto map
   entries. Cosmetic, but it closes a real disconnect between two surfaces showing the same areas.

### Tier B — high value, gated on one unknown

These are the big structural wins, and they all depend on the same question.

7. **Findings as a `Table`** — material / quantity / depth / location as real columns instead of one
   composed text blob. This is the single largest improvement available if it works.
8. **Areas as `IEnumerable` / `ButtonList` / `ExpandableList`** — a real list would kill *both* the
   vertical stack *and* the six-button cap, since the cap exists only because RPCs are compile-time
   methods. A list-driven surface removes the ceiling entirely rather than raising it.

**The gate:** list and table templates bind to element *views*. Our crash proved elements must be
View types, and a mod-defined type has no generated client view. So Tier B hinges on whether a list
can be bound to **game types that already have views** (Items, and similar), or whether some
`UIListTypeName` path accepts simpler elements. Until that is answered, Tier B is a hypothesis —
a well-founded one, but not a plan.

---

## Rejected

- **Ship a custom panel prefab in the mod bundle.** Closed on three independent mechanisms above;
  `ModBundleManager` has no UI branch at all.
- **`Fast-UI-extensions.md` as an extension point.** Despite the name, it is purely layout
  performance (`FastLayout`, `FastImage`). No extension surface. Recorded so nobody chases it.
- **Reviving `[WorldObjectUIPanel]`.** Deprecated in client source.
- **Richer world-space readout via animated states.** The channels are fixed and complete —
  `States` (bool), `StringStates` (string), `FloatStates` (float), `Events` (void); any other
  payload type throws (`WorldObjectInternal.cs:211`), transport is edge-triggered per key with
  identical values dropped. Adequate for a status line, not a foundation for UI.

---

## Recommended next step

One **batched probe**, following this project's own live-testing discipline: a single throwaway
component exposing several Tier A templates at once plus one Tier B list attempt, deployed in one
restart and screenshotted. That answers the element-type gate and validates half a dozen Tier A
candidates in the same cycle, instead of a restart per guess.

Then `ce-brainstorm` on whichever of Tier B survives — that is where the interaction design
actually changes, and it deserves requirements rather than an incremental patch.

---

## Docs this invalidates

- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — its "rich list is
  not available to a mod tab" section is wrong in the general case and needs rewriting around the
  68-name vocabulary and the true ceiling.
- `docs/solutions/design-patterns/vertical-stack-only-ui-design.md` — its layout *reasoning* stands,
  but its premise (a single-column primitive) is false. Revise once the probe confirms, rather than
  leaving a confidently-wrong doc in the store.

Hold both edits until the probe returns live evidence. Recording a *new* wrong conclusion while
correcting an old one is the failure this project keeps repeating.
