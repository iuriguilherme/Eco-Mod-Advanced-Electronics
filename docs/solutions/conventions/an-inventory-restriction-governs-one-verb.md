---
title: "An inventory restriction governs one verb — a tag filter blocks putting in, not taking out or burning"
date: 2026-08-02
last_updated: 2026-08-10
category: conventions
module: EcoServerMod
problem_type: convention
component: inventory_restrictions
severity: medium
applies_when:
  - "Changing which items an existing inventory accepts"
  - "Writing release notes for a change that narrows an inventory's accepted set"
  - "Reasoning about what happens to items already in an inventory when its rules change"
  - "Adding a restriction and expecting it to govern removal or consumption"
tags: [eco-modding, inventory, restrictions, tagrestriction, fuel, migration, release-notes]
related_components: [EcoServerMod/AdvancedElectronics]
---

# An inventory restriction governs one verb — a tag filter blocks putting in, not taking out or burning

Paths beginning `Server/` below are Eco's engine source tree, not files in this repository.

## Context

This mod switched its drone dock from vanilla liquid fuel to a mod-owned fuel tag. Existing servers
had docks with biodiesel sitting in the tank, and the obvious reading of "the tank now only accepts
Electric Fuel" was that the old fuel becomes stuck: unusable because it will not burn, unremovable
because it no longer passes the filter, and blocking the drone's removal because the mod refuses to
release a drone whose tank is non-empty.

That reading produced a one-way-door migration story, a `CONCEPTS.md` entry describing stranded
fuel, two acceptance examples asserting it, and a release note telling players to drain every dock
before updating.

None of it was true. The engine drains the old fuel by itself.

## Guidance

**A restriction has three independent hooks and answers "no opinion" on all of them by default**
(`Server/Eco.Gameplay/Items/InventoryRelated/InventoryRestrictions.cs:246-266`):

```csharp
public virtual int MaxAccepted(Item item) => -1;                                          // putting in
public virtual int MaxPickup(RestrictionCheckData checkData, Item item, int totalMoved) => -1;  // taking out
```

`-1` means default behavior — the restriction declines to interfere. A restriction governs a verb
only by overriding that verb's hook, and each override is opt-in.

**`TagRestriction` overrides intake and nothing else** (`:476-488`):

```csharp
public override int MaxAccepted(Item item) => item.Tags().Any(x => this.allowedTags.Contains(x.Name)) ? -1 : 0;
```

There is no `MaxPickup` override. Items already in the inventory stay removable by hand, whatever
the tag list now says.

**Internal consumption does not consult restrictions at all.** A component that eats from its own
inventory reads the stacks directly. `FuelSupplyComponent.LoadFuel` takes whatever is first:

```csharp
var firstItem = this.FuelSupply.NonEmptyStacks.FirstOrDefault()?.Item;
```

No tag check, no restriction call. So a tank narrowed to a new fuel class keeps burning the old
fuel until it is spent, then empties itself and asks for the new kind.

**Check which hooks a restriction overrides before predicting behavior.** In the whole restriction
file only five classes override `MaxPickup`, and each exists specifically to block removal —
`PutOnlyRestriction`, `PermanentModuleRestriction`, `RightsRestriction`,
`ClothingSlotBonusRestriction`, and a general test-function one. If blocking removal is not a
restriction's stated purpose, it does not block removal.

## Why This Matters

The consequence is a release note that tells players to do work they do not need to do, and warns
them about a failure that cannot happen.

Worse, it is the kind of wrong that reads as cautious. "Drain your fuel tanks before updating" costs
a player ten minutes and sounds like responsible advice, so nobody pushes back on it and nobody
discovers it was unnecessary. The actual behavior — the dock quietly finishing its old fuel and then
asking for the new kind — is strictly better and would have gone unmentioned.

The framing that causes the error is treating an inventory's accepted-item set as a description of
what may *be* there, rather than what may *enter*. Under the first reading, narrowing the set
strands whatever no longer fits. Under the second, narrowing it only changes what happens next, and
everything already inside follows its normal lifecycle.

## When to Apply

- Whenever a change narrows an existing inventory's accepted items and servers already have
  instances in the wild. Ask what happens to the contents before writing the migration note.
- Before writing any "do this before updating" instruction. The engine may already handle it.
- When adding a restriction and expecting it to prevent removal or consumption — it will not unless
  it overrides those hooks.
- When reasoning about a component that consumes from its own inventory. Restrictions gate the
  player's hands, not the component's.

## Examples

The three verbs, and who governs each, for a fuel tank whose tag list was narrowed:

| Verb | Governed by | Result after the tag change |
|---|---|---|
| Player adds fuel | `TagRestriction.MaxAccepted` | old fuel refused |
| Player removes fuel | nothing overrides `MaxPickup` | old fuel removable by hand |
| Component burns fuel | no restriction in the path | old fuel burns normally until spent |

What the release note became, once the behavior was checked rather than assumed:

```text
  Before:  Drain the fuel tank of every Drone Dock before updating, or the
           leftover fuel will block removing the drone.

  After:   Fuel already in a dock keeps burning until it is spent, then the
           dock asks for the new kind. No action needed before updating.
```

The check that produced the second version is two greps — does this restriction override
`MaxPickup`, and does the consuming code path call any restriction at all.

## Related

- `docs/solutions/runtime-errors/naming-a-component-hides-it-from-its-vanilla-consumer.md` — the
  other fuel-component surprise in this mod, and the same underlying habit: assuming a mechanism
  covers more than the one narrow thing it was written for.
- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md` — also about
  what happens to already-existing objects when a declaration changes. There the answer is that the
  engine re-converges each object's component list on every load, adding and removing; here it is
  "they drain normally."
