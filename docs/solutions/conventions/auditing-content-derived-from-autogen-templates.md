---
title: "Audit mod content derived from AutoGen templates for the references you forgot to rename"
date: 2026-07-31
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: high
applies_when:
  - "Creating mod content by copying a vanilla AutoGen source file and renaming it"
  - "Reviewing a new recipe, item, block, or world object that was derived from a template"
  - "A recipe produces the wrong item, or an object reads another object's values, with no error"
  - "Deciding what to check before a derived file's first live test"
tags: [eco-modding, autogen, templates, copy-paste, recipes, code-review, silent-failure]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Audit mod content derived from AutoGen templates for the references you forgot to rename

## Context

Eco ships every vanilla object, item, block and recipe as generated C# under
`Mods/__core__/AutoGen/` inside the dedicated server install (not this repo). Copying the nearest vanilla file and renaming it is
the normal, correct way to author mod content — the templates encode conventions (attribute order,
`ModsPreInitialize` hooks, `CraftingComponent.AddRecipe` registration) that are tedious to rediscover.

The hazard is that renaming is a manual sweep across a file of a hundred-plus lines, and the compiler
only catches *some* of what you miss. One batch of six derived files in this mod carried six distinct
leftovers from their templates. Three stopped the build. Three compiled perfectly and were wrong.

## Guidance

**Sort the residue into loud and silent before you start looking.** They need different detection.

*Loud residue* — a reference to something that does not exist in this Eco version. The build fails and
names the file and line. It costs time but cannot ship:

- `InsulatedCopperWiringItem` — a v14 item, absent in 0.13.
- `AshlarStone` used as `typeof(AshlarStone)` when it is a **tag**, not a type. The ingredient wanted
  the string form: `new IngredientElement("AshlarStone", 20, typeof(IndustrySkill))`.
- `[AllowPluginModules(..., ItemTypes = new[] { typeof(AdvancedElectronicsUpgradeItem) })]` naming a
  plugin-module type that had never been written.

*Silent residue* — a reference to a type that **does** exist, is the wrong one, and type-checks. This
is the category that matters:

- `BatteryRecipe` emitted `new CraftingElement<BiodieselItem>(1)`. The file was derived from vanilla's
  Biodiesel recipe. Crafting a battery produced biodiesel.
- `EngineeringResearchPaperPostModernRecipe` emits `new CraftingElement<EngineeringResearchPaperModernItem>()`
  — vanilla's *Modern* paper, not the PostModern one the recipe is named for
  (`EcoServerMod/AdvancedElectronics/EngineeringResearchPaperPostModern.cs:55`, unfixed at time of
  writing, deferred as a balance decision).
- `AdvancedElectronicsAssemblyObject.Initialize()` sets
  `this.GetComponent<HousingComponent>().HomeValue = ElectronicsAssemblyItem.homeValue` — vanilla's
  item, not the mod's own `AdvancedElectronicsAssemblyItem.homeValue`
  (`EcoServerMod/AdvancedElectronics/AdvancedElectronicsAssembly.cs:107`, still present; may be
  deliberate reuse, but it was never a decision).

**Nothing type-checks the relationship between a recipe and its output.** `CraftingElement<T>` has no
constraint tying `T` to the recipe it sits in, so a battery recipe producing biodiesel is exactly as
valid to the compiler as one producing a battery. The same holds for `homeValue`, `PartInfo.TypeName`,
and any `typeof()` inside an attribute. These are the places to look, because they are the places the
language will not look for you.

**Grep the derived file for the template's own name before the first build.** The template's subject
noun is the highest-signal search term: derive `Battery.cs` from `Biodiesel.cs` and every remaining
`Biodiesel` is a leftover. This catches loud and silent residue in one pass, and it takes seconds.

**Write the grep so it cannot silently match nothing.** A search that finds no residue and a search
that is incapable of finding residue produce the same output. In this session, a sweep for leftover
`Electronics` references used `[A-Za-z]+Electronics[A-Za-z]*` — requiring at least one character
*before* the word — so bare `ElectronicsSkill` and `ElectronicsAssemblyItem` could never match, and
the audit came back clean on files that were not. Prefer `[A-Za-z]*Name[A-Za-z]*` with a star, and
sanity-check the pattern against a line you know should match. This is the same failure documented in
`docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md`.

**Check what the file registers itself against, not only what it declares.** Residue hides in the
registration tail as readily as in the type declarations: `CraftingComponent.AddRecipe(tableType: ...)`
pointing at the vanilla table the template used, `[Ecopedia(...)]` naming the template's page,
`[RequiresSkill]` naming the template's skill. These all compile and quietly attach your content to
vanilla's furniture.

## Why This Matters

Silent residue survives every check that normally protects this mod. It builds. It passes the
name-match gate, because both types genuinely exist and both have client assets. It produces no log
line and no exception. The only detection is a human reading the specific line, or a player crafting
the item and noticing they got something else.

It is also mis-attributed when it does surface. A battery that yields biodiesel reads as a recipe
*balance* problem, not a copy-paste problem, so it gets filed against the wrong part of the system. In
this case the battery bug was found only because an unrelated build failure forced a line-by-line read
of the file.

The cost scales with how well the template matched. The closer the vanilla analogue, the more of it
you keep, and the more places a stale reference can hide while still looking idiomatic — because it
*is* idiomatic. It is simply about the wrong object.

## When to Apply

- Immediately after copying any file out of the server install's `Mods/__core__/AutoGen/`, before the first build.
- When reviewing a PR that adds mod content — ask which vanilla file it came from, then grep the diff
  for that file's subject noun.
- When a recipe, item, or object behaves like a *different* object: yields the wrong product, shows
  another object's housing or tooltip values, or appears on the wrong crafting table.
- Before deferring a derived file as "works, balance later" — silent residue is not a balance issue
  and will not surface on its own.

## Examples

The residue sweep, written so it cannot match nothing. `*` not `+`, and the exclusion is anchored so
it only drops correctly-renamed hits:

```bash
# Derived Battery.cs from vanilla Biodiesel.cs -- every remaining "Biodiesel" is residue.
grep -noE '[A-Za-z]*Biodiesel[A-Za-z]*' EcoServerMod/AdvancedElectronics/Battery.cs.deferred

# Sweeping a whole batch for a vanilla noun, keeping only the un-renamed hits.
for f in EcoServerMod/AdvancedElectronics/*.cs; do
  OUT=$(grep -noE '[A-Za-z]*Electronics[A-Za-z]*' "$f" | grep -vE ':Advanced[A-Za-z]*$' || true)
  [ -n "$OUT" ] && { echo "=== $f"; echo "$OUT"; }
done
```

What silent residue looks like in place — both sides compile, and only the second is what the file
claims to be:

```csharp
// BatteryRecipe, derived from vanilla Biodiesel.cs. Compiles; wrong product.
items: new List<CraftingElement>
{
    new CraftingElement<BiodieselItem>(1)
});

// Corrected.
items: new List<CraftingElement>
{
    new CraftingElement<BatteryItem>(1)
});
```

A tag mistaken for a type — loud, but worth recognising on sight, since tag ingredients take the
string overload:

```csharp
new IngredientElement(typeof(AshlarStone), 20, typeof(IndustrySkill)),   // no such type
new IngredientElement("AshlarStone", 20, typeof(IndustrySkill)),         // tag: any ashlar stone
```

## Related

- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — why the
  residue grep must be written so it can fail loudly; an over-anchored pattern reports clean on a
  dirty file.
- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the other class of
  thing an AutoGen template will not give you, because a generator supplies it rather than the source.
- `docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md` — the same
  wrong-name-that-still-works failure on the Unity side of this mod.
