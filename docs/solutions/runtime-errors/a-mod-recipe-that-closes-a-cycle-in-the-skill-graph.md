---
title: "A mod recipe that makes its own skill an ancestor overflows the stack at 'Initializing skills'"
date: 2026-07-31
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: critical
symptoms:
  - "Server dies during 'Initializing skills' with Windows exception code 0xc00000fd and no log file entry"
  - "Captured stdout shows thousands of consecutive Eco.Gameplay.Skills.SkillTree.GetParentSet frames"
  - "The crash reproduces on every world, including a freshly created one"
  - "Removing the mod's own Skill subclass appears to fix it, and does not"
root_cause: logic_error
resolution_type: code_fix
tags: [eco-modding, skilltree, stack-overflow, recipes, tech-tree, research-papers, autogen, server-mod]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A mod recipe that makes its own skill an ancestor overflows the stack at 'Initializing skills'

## Problem

A mod recipe produced a vanilla research paper that the recipe's own required skill needs in
order to be unlocked. That closed a cycle in Eco's skill-dependency graph, and `SkillTree`
walks that graph with no cycle guard — the server died with an uncatchable stack overflow
during startup.

## Symptoms

The server exited during `Initializing skills` with Windows exception code `0xc00000fd` and
nothing in `Logs/*.log`: a `StackOverflowException` terminates the process without unwinding,
so no log writer flushes. Running `EcoServer.exe` from a shell with stdout redirected produced
the trace:

```
[11:42:17] Initializing skills                               ...
Stack overflow.
   at System.Attribute.Equals(System.Object)
   at System.Collections.Generic.List`1[...].Contains(System.__Canon)
   at Eco.Shared.Utils.CollectionExtensions.AddUnique[...](...)
   at Eco.Shared.Utils.ListExtensions.AddUniqueRange[...](...)
   at Eco.Gameplay.Skills.SkillTree.GetParentSet(Eco.Gameplay.Skills.RequiresSkillAttribute)
   at Eco.Gameplay.Skills.SkillTree.GetParentSet(Eco.Gameplay.Skills.RequiresSkillAttribute)
   ... x8072 ...
   at Eco.Gameplay.Skills.SkillTree.MakeNonTransitive(System.Collections.Generic.IDictionary`2<...>)
   at Eco.Gameplay.Skills.SkillTree.BuildSkillTrees()
```

**`GetParentSet` calling itself with no intervening frames is the whole diagnosis.** Pure
self-recursion thousands deep means a cycle in the input graph, not deep data. Reading it as
"the skill hierarchy is too deep" sends the investigation to the wrong file.

## What Didn't Work

- **Blaming the mod's own `Skill` subclass.** Removing `AdvancedElectronics.cs` from the build
  made the crash disappear, which "confirmed" that Eco 0.13 could not accept a modded skill.
  That conclusion was falsified by the maintainer: `.references/Mods/` holds working v13 skill
  mods (AnimalHusbandry, Beekeeping, IntelligenceSkillMod). The removal worked only because it
  also removed the recipes that referenced the paper.
- **Auditing the skill declaration.** The mod's skill is byte-for-byte structurally identical
  to vanilla's `ElectronicsSkill` — `[RequiresSkill(typeof(EngineerSkill), 0), Tag("Engineer
  Specialty"), Tier(5)]` against Tier(4) — and `EngineerSkill` declares no `RequiresSkill` at
  all, so that chain terminates at depth 2. There was never a cycle there to find.
- **Assuming a second producer of a vanilla item is inherently illegal.** It is not, and
  assuming so would have led to the wrong fix. See *Why This Works*.
- **Trusting a bisect whose runs were never validated.** Several "boots fine" results were runs
  of 23 and 43 lines that never reached `Initializing skills` at all. See
  `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md`.

## Solution

The recipe emitted the wrong item — vanilla's *Modern* research paper instead of the
*PostModern* one the recipe is named for:

```csharp
// before -- EngineeringResearchPaperPostModernRecipe, [RequiresSkill(typeof(IndustrySkill), 1)]
items: new List<CraftingElement>
{
    new CraftingElement<EngineeringResearchPaperModernItem>()
});

// after
items: new List<CraftingElement>
{
    new CraftingElement<EngineeringResearchPaperPostModernItem>()
});
```

With all `RequiresSkill` attributes restored, `Initializing skills` completed in 0.081 sec and
the server reached `Web Server now listening`.

Isolating it took three runs once each run was verified to reach the failing phase:

| Run | Configuration | Result |
|---|---|---|
| 1 | every `RequiresSkill` in the mod commented out | boots |
| 2 | only the paper recipe's `[RequiresSkill(typeof(IndustrySkill), 1)]` restored | 8071 `GetParentSet` frames |
| 3 | that same attribute moved onto the drone recipe instead | boots |

Run 3 is the one that matters: identical attribute, identical skill, different recipe. That
rules out the attribute and the skill and points at what the recipe *outputs*.

## Why This Works

Eco's tech tree is a graph. `RequiresSkill` supplies skill-to-skill edges; recipe
ingredient/output pairs supply item edges. `SkillTree.BuildSkillTrees` walks it through
`MakeNonTransitive` to strip redundant edges, and `GetParentSet` recurses over parents with no
visited-set guard, so any cycle is unbounded recursion.

The cycle this mod created:

```
IndustrySkillBookRecipe   (__core__/AutoGen/Tech/Industry.cs:98, :114)
  [RequiresSkill(typeof(MechanicsSkill), 1)]
  consumes 20x EngineeringResearchPaperModernItem   ->  unlocks IndustrySkill

EngineeringResearchPaperPostModernRecipe   (the mod)
  [RequiresSkill(typeof(IndustrySkill), 1)]
  produced EngineeringResearchPaperModernItem
```

IndustrySkill became reachable from its own prerequisite.

**A second producer of a vanilla item is not the problem.** Vanilla ships 22 items produced
under more than one skill, including three research papers —
`CulinaryResearchPaperBasicItem` (CampfireCooking *or* Hunting), `CulinaryResearchPaperAdvancedItem`
(Cooking *or* Baking), `GeologyResearchPaperModernItem` (Pottery *or* Glassworking). Alternate
recipes are idiomatic: `ConcentrateGoldLv2Recipe` produces `GoldConcentrateItem` at MiningSkill 4
alongside the base recipe's MiningSkill 1, with a better yield at a better table.

The actual invariant, checked across every `*SkillBookRecipe` in
`.references/Mods/__core__/AutoGen/Tech/` against every recipe output in that tree:

> **No vanilla skill book consumes a research paper that the same skill produces. Zero
> exceptions out of ~40 books.**

Every paper is produced by a skill strictly *upstream* of the book that consumes it —
`IndustrySkill`'s book needs `EngineeringResearchPaperModernItem`, produced only by
`MechanicsSkill` (`.references/Mods/__core__/AutoGen/Item/EngineeringResearchPaperModern.cs:40`), and
`EngineeringResearchPaperAdvancedItem`, produced only by `BasicEngineeringSkill`.

So the rule is not "one producer per item". It is: **a skill may not produce, directly or
transitively, anything its own unlock path consumes.**

## Prevention

- **Make it a static check.** This is greppable from source and would have caught the bug before
  the first boot. For every mod recipe, its outputs must not appear in the ingredient list of the
  skill book for the skill it requires:

  ```bash
  # what each skill book consumes
  grep -rn -A20 "class .*SkillBookRecipe" .references/Mods/__core__/AutoGen/Tech/*.cs \
    | grep "IngredientElement(typeof("

  # what our recipes require and produce
  grep -rnE "\[RequiresSkill|CraftingElement<" EcoServerMod/AdvancedElectronics/*.cs
  ```

- **Read pure self-recursion as a cycle, every time.** `X -> X` with no intervening frames means
  the input graph has a loop. Do not go looking for depth.
- **Capture stdout when the process dies outright.** A `StackOverflowException` is uncatchable in
  .NET — `try`/`catch`/`finally` never run and no log file receives the trace, but the runtime
  still prints frames to standard output. Adding `try`/`catch` to the mod would have produced
  nothing at all.
- **Treat a wrong `CraftingElement<T>` as a correctness bug, not balance.** It type-checks —
  `CraftingElement<T>` has no constraint tying `T` to the recipe it sits in — and it silently
  writes a new edge into the game's tech graph. This one was found and *deferred as a balance
  decision* for a day before it was understood to be the crash.
- **When a new skill or a new research paper enters the mod, re-run the check.** The cycle needs
  only one new edge, and the failure mode is a server that will not start.

## Related

- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the class this
  bug belongs to: content copied from a vanilla AutoGen file keeping a reference to the template's
  own item. That doc lists this exact line as an example of harmless-looking "silent residue"
  deferred as a balance question; it is the proximate cause of this crash and its severity there
  is understated.
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — why this took
  four wrong culprits before the real one: contaminated scratch restores and runs read as passes
  without checking they reached the phase under test.
- `docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md` — the
  other startup-time failure from the same session, at object rather than type level: a throw in
  `Initialize()` leaves an object half-built. Distinguishing tell: that one runs per instance and
  only once one exists in the save; this one runs at type-graph build and fires on every world.
