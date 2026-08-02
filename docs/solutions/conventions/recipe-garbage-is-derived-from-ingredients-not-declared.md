---
title: "Recipe garbage is derived from ingredient salvage cost, not declared"
date: 2026-08-01
category: conventions
module: EcoServerMod
problem_type: convention
component: recipes
severity: low
applies_when:
  - "Writing or reviewing a mod recipe"
  - "Copying a vanilla AutoGen recipe as a starting point"
  - "Adding or omitting SalvageCost on a craftable item"
  - "A recipe shows waste outputs nobody wrote"
tags: [eco-modding, recipes, garbage, salvagecost, autogen, derived-values]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Recipe garbage is derived from ingredient salvage cost, not declared

## Context

Every vanilla AutoGen recipe spells out a `garbages:` list:

```csharp
garbages: new List<GarbageOutput>
{
    new GarbageOutput(typeof(Trash), 0.2f),
},
```

Copying that pattern makes it look like a required field — something a mod recipe must supply or
its craft produces no waste. This mod's recipes never declared one, which raised the question of
whether they were missing something.

They were not. The Survey Drone recipe, which declares no `garbages:` at all, renders a full
**GARBAGES** row in the crafting panel with seven distinct scrap types. The waste is computed, not
written.

## Guidance

**Do not declare `garbages:` for ordinary recipes.** Waste is derived from what the recipe consumes:
each ingredient's `[SalvageCost]` scaled by a global ratio and by that ingredient's quantity. A
recipe made of items that carry salvage costs produces garbage automatically and proportionally.

The two lists are distinct and the engine says so at `Server/Eco.Gameplay/Items/Recipes/Recipe.cs:27`:

```csharp
public List<GarbageOutput> Garbages { get; protected set; } // Recipe-declared process waste (e.g. smelting slag). Does NOT include item-derived garbage — see <see cref="TotalGarbages"/> for the merged list.
```

`TotalGarbages` is the union the player actually sees (`Recipe.cs:38-43`), and the derived half is
computed per ingredient (`Recipe.cs:64`):

```csharp
contributions[material] = contributions.GetOrDefault(material) + (cost * SalvageCostUtil.CraftGarbageRatio * ingredient.Quantity.GetBaseValue);
```

**Declare `garbages:` only for process waste that the ingredients cannot account for** — slag from
smelting, tailings, byproducts of the transformation rather than of the materials. That is what the
field is for, and it is why vanilla smelting recipes carry one.

**Two escape hatches exist when the derivation is wrong for a recipe.** Both are on the recipe
(`Recipe.cs:29-34`):

- `ZeroSalvageCost` suppresses the item-derived contribution entirely, for recipes that legitimately
  consume nothing salvageable. Recipe-declared waste is unaffected.
- `GarbageIsApproximate` is not a switch but a computed flag: it reports true when a **tag**
  ingredient makes the preview an estimate, because which item satisfies the tag — and therefore its
  salvage cost — is unknown until craft time.

**Remember the direction of the dependency: `[SalvageCost]` on an item changes every recipe that
consumes it.** Adding one is not a local edit to that item's own disposal; it silently adds waste to
the whole downstream tree.

## Why This Matters

The failure this prevents is not a crash — it is writing redundant, wrong, or drifting data. A
hand-written `garbages:` list on an ordinary recipe **adds to** the derived list rather than
replacing it, so the visible result is more waste than intended, and the hand-written half does not
follow when ingredients or quantities change. The derived half stays correct forever; the declared
half rots.

It is also a case where copying vanilla misleads. AutoGen recipes are generated from a table, and
the generator emits every field it knows about whether or not it carries information for that
recipe. A field that appears in every generated example reads as mandatory, and the honest test is
not "does vanilla write it" but "does the engine need it from me."

## When to Apply

- When writing any new mod recipe — omit `garbages:` unless the recipe produces process waste
  distinct from its ingredients.
- When reviewing a recipe copied from an AutoGen template, alongside the other fields that template
  emits unconditionally.
- When adding `[SalvageCost]` to a craftable item — check what recipes consume it before assuming
  the change is local.
- When a recipe shows more waste than expected, before hunting for a bug: a declared list on top of
  the derived one is the likely cause.

## Examples

Observed live: the Survey Drone recipe declares no garbage, and its crafting panel still shows seven
scrap types at varying rates. The recipe's ingredients — advanced circuits, gearboxes, plastic,
fiberglass, steel gear, electric motor, and the rest — each carry a `[SalvageCost]`, and the panel is
showing their sum.

What this mod's recipes look like, and should keep looking like:

```csharp
recipe.Init(
    name: "SurveyDrone",  //noloc
    displayName: Localizer.DoStr("Survey Drone"),
    ingredients: new List<IngredientElement>
    {
        new IngredientElement(typeof(AdvancedCircuitItem), 6, typeof(AdvancedElectronicsSkill)),
        // ... the rest
    },
    // no garbages: -- derived from the ingredients above
    items: new List<CraftingElement>
    {
        new CraftingElement<SurveyDroneItem>(1),
    });
```

The other side of the same rule — a `[SalvageCost]` added to an item this session, which now feeds
every recipe consuming it:

```csharp
[SalvageCost(typeof(CopperScrap), 10.0f, typeof(IronScrap), 10.0f)]
public partial class AdvancedElectronicsAssemblyItem : WorldObjectItem<AdvancedElectronicsAssemblyObject>, IPersistentData
```

## Related

- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the general form:
  a field copied from a generated template because it was present, not because it was needed.
- `docs/solutions/runtime-errors/a-mod-recipe-that-closes-a-cycle-in-the-skill-graph.md` — the other
  recipe-authoring trap in this mod, where the harm is a boot-time crash rather than wrong data.
