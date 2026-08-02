---
title: "Which IngredientElement overload you call decides whether any bonus can reduce that ingredient"
date: 2026-08-02
category: conventions
module: EcoServerMod
problem_type: convention
component: recipes
severity: medium
applies_when:
  - "Writing or editing a mod recipe's ingredient list"
  - "Deciding which crafting table a recipe belongs on"
  - "An upgrade module or skill appears to have no effect on a recipe"
  - "Copying an ingredient line from another recipe"
tags: [eco-modding, recipes, ingredients, bonuses, upgrade-modules, dynamic-values, silent-failure]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Which IngredientElement overload you call decides whether any bonus can reduce that ingredient

Paths beginning `Server/` below are Eco's engine source tree, not files in this repository.

## Context

Deciding which table a mod recipe belongs on looked like a flavour question — which bench fits the
tier, which one the player already has. It is not only that. A crafting table's upgrade module
reduces ingredient costs, and whether it can touch a given recipe is fixed by how that recipe's
ingredient lines were written, not by the table, the skill gate, or the module's own bonus list.

This mod has one recipe of each kind, which is what made the difference visible:

```csharp
// Battery — the module reduces these
new IngredientElement(typeof(NitricAcidItem), 4, typeof(AdvancedElectronicsSkill)),

// Advanced Electronics Upgrade — the module cannot touch these
new IngredientElement(typeof(AdvancedCircuitItem), 3, true),
```

They look like the same call with a different third argument. They are different constructors
building different quantity types.

## Guidance

**Two constructor families, and only one produces a bonus-affected quantity**
(`Server/Eco.Gameplay/Items/Recipes/IngredientElement.cs:53-82`):

```csharp
// (Type, float, bool) -> ConstantValue. No bonus can touch it.
public IngredientElement(ItemRepresentation stackable, float count = 1f, bool staticIngredient = false)
    { this.Quantity = new ConstantValue(!staticIngredient ? count * RecipeManager.CraftResourceModifier : count); }

// (Type, float, Type skill) -> ModuleModifiedValue. Bonuses apply.
private IDynamicValue CreateQuantityValue(float start, Type skill, Type talent = null)
    { IDynamicValue smv = new ModuleModifiedValue(start, skill, DynamicValueType.Efficiency); ... }
```

**`staticIngredient: false` does not mean dynamic.** Both branches of that ternary produce a
`ConstantValue` — the flag only decides whether the global `CraftResourceModifier` is applied to
the number. So the two-argument form with no flag at all, `new IngredientElement(typeof(X), 3)`, is
equally bonus-immune. The parameter name reads as the opposite of what it controls.

**Pass the skill type to make an ingredient reducible.** That is the whole mechanism. Not the
recipe's `[RequiresSkill]`, not the table, not the module.

**The gate is a type check, repeated at every consumer.** `Quantity is ConstantValue` appears at
consumption (`Server/Eco.Gameplay/Items/WorkOrder.cs:214`), at preview
(`Server/Eco.Gameplay/Components/CraftingComponent.cs:657`), and in the tooltip layer
(`Server/Eco.Gameplay/Systems/NewTooltip/TooltipLibraryFiles/SkillTooltipLibrary.cs:520,625,634`).
There is no separate opt-out to look for — the quantity's runtime type is the switch.

## Why This Matters

The failure is a module that does nothing, with no error anywhere.

A player crafts the upgrade, slots it into the table, opens the recipe, and the numbers do not move.
Nothing failed: the module is installed, its bonus list is correct, the skill gate is right, the
recipe is on the right table. The ingredient simply is not the kind of thing a bonus applies to, and
that was decided in a constructor call whose difference from the working one is a third argument.

It also silently decides a design question that looks unrelated. "Which table should this recipe
live on" only matters for module effects if the recipe's ingredients are reducible in the first
place — put a bonus-immune recipe on a module-bearing table and the module is decorative there.
That is why an upgrade module's *own* recipe uses the constant form: it would otherwise be a module
that discounts itself.

## When to Apply

- When writing any mod recipe. Decide per ingredient whether a skill or module should be able to
  reduce it, and pick the overload accordingly.
- When choosing which table hosts a recipe, if the reason involves an upgrade module. Check the
  ingredient overloads before concluding the module buys anything there.
- When a module or skill appears to have no effect. Check the ingredient lines before the module,
  the bonus, the table, or the skill.
- When copying an ingredient line from another recipe — the overload comes with it, and a line
  copied from a static recipe silently makes the new ingredient static too.

## Examples

The in-game tell, which is faster than reading source: the crafting panel styles constant
ingredients as a warning and reducible ones as positive
(`SkillTooltipLibrary.cs:520`), and `CraftingComponent.cs:657` describes them as "yellow-bordered,
non-fractional". So:

- **Yellow-bordered, whole numbers** → `ConstantValue`. No module or skill will ever move them.
- **Normal styling, fractional values** → `ModuleModifiedValue`. Bonuses apply.

Open a recipe with the module slotted and look at the border. If the number is yellow, the
ingredient list is the thing to fix, not the module.

The two forms side by side, with what each is for:

```csharp
// Reducible: the skill type is what makes it so. Use for ordinary recipe ingredients.
new IngredientElement(typeof(NitricAcidItem), 4, typeof(AdvancedElectronicsSkill)),

// Constant: use when the cost must not move -- an upgrade module's own recipe, or anything
// where a discount would be circular or would break a balance floor.
new IngredientElement(typeof(AdvancedCircuitItem), 3, true),

// Also constant, and easy to write by accident -- there is no flag to notice.
new IngredientElement(typeof(AdvancedCircuitItem), 3),
```

## Related

- `docs/solutions/conventions/recipe-garbage-is-derived-from-ingredients-not-declared.md` — the
  other place a recipe's ingredient list silently drives something else. There the derived thing is
  waste output; here it is whether a bonus can apply.
- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — why a line
  copied from a generated recipe carries assumptions the template never states. An overload choice
  is exactly that kind of invisible inheritance.
