---
title: "In a framework that auto-discovers types, declaring the type is the registration"
date: 2026-08-08
category: conventions
module: EcoServerMod
problem_type: convention
component: recipes
severity: medium
applies_when:
  - "Withholding or hiding mod content before a release because a feature is unfinished"
  - "Commenting out only the CraftingComponent.AddRecipe call inside a RecipeFamily constructor"
  - "Verifying that removed content is actually gone from a build rather than trusting a source diff"
  - "Deciding how much to remove when a save file may already hold the content"
symptoms:
  - "A recipe whose table registration was commented out still appears in the recipe browser and its skill's tech tree"
  - "A source grep shows no active registration call, yet the content is still reachable in game"
root_cause: wrong_api
resolution_type: code_fix
tags: [eco-modding, recipefamily, forcecreateviewallderived, content-withholding, release-prep, crafting-table]
related_components:
  - "EcoServerMod/AdvancedElectronics"
---

# In a framework that auto-discovers types, declaring the type is the registration

## Context

Eco does not ask a mod which recipes it provides. It scans the loaded assemblies for types derived
from `RecipeFamily` and instantiates one of each at startup. The mod never calls a "register this
recipe" API from the outside; the constructor runs because the type exists, and the constructor is
where registration happens.

That shape decides what "removing content from a release" has to mean, and it was learned the
expensive way while cutting `v0.2.0`. The mining and harvest drones had to be withheld — their arm
does not yet animate correctly in flight — so the first attempt commented out the one line in each
recipe constructor that attaches the recipe to a bench:

```csharp
// CraftingComponent.AddRecipe(tableType: typeof(RoboticAssemblyLineObject), recipeFamily: this);
```

The theory was that a recipe belonging to no crafting table is a recipe no player can reach. It is
not. Eco's `RecipeFamily` carries `[ForceCreateViewAllDerived]` — confirmed against Strange Loop
Games' own engine source rather than inferred, at `Server/Eco.Gameplay/Items/Recipes/RecipeFamily.cs`
line 24 in the Eco 0.14 source checkout this mod's reference assemblies are built from. That path is
in Eco's tree, not this repository. The engine therefore still constructed `MiningDroneRecipe` and
`HarvestDroneRecipe` at startup, and their constructors still reached

```csharp
this.Initialize(displayText: Localizer.DoStr("Mining Drone"), recipeType: typeof(MiningDroneRecipe));
```

which is the call that registers the recipe with the game. Commenting out `AddRecipe` removed the
bench and nothing else. The result was worse than shipping the drone: a recipe listed in the recipe
browser and drawn into the Advanced Electronics tech tree, with no table anywhere that could craft
it. Visible, promised, and impossible.

The fix was to comment out the entire `RecipeFamily`-derived class. Both files now carry the whole
recipe inside a `/* … */` block: `EcoServerMod/AdvancedElectronics/MiningDrone.cs:263-320` and
`EcoServerMod/AdvancedElectronics/HarvesterDrone.cs:263-320`, each introduced by an explanatory
comment at line 252 of its file. A live recipe for comparison is
`EcoServerMod/AdvancedElectronics/SurveyDrone.cs:276-323` (`SurveyDroneRecipe`, with its
`AddRecipe` call at line 315) and `EcoServerMod/AdvancedElectronics/DroneDock.cs:898-936`
(`DroneDockRecipe`, with its `AddRecipe` at line 931). The two shapes are otherwise identical, which is the
point: the only difference between shipped and withheld is whether the type is compiled.

## Guidance

**To withhold content from a release under an auto-discovering framework, remove the type. Disabling
what the type does leaves what the type is.** Turning off a behaviour inside a constructor is not
suppression when the framework's contract is "your type existing is your registration". Ask what the
framework keys on — derived types, an attribute, an annotation, a naming convention, a file in a
scanned directory — and delete or exclude that key. Anything short of it is a partially disabled
object that the framework still surfaces.

**Withhold the recipe, keep the item and the world object.** `MiningDroneItem`, `MiningDroneObject`,
`HarvestDroneItem`, and `HarvestDroneObject` stay defined and compiled. Any save that already holds
one of these drones goes on loading. Removing those types would break those worlds, and this mod
ships no save migrations, so the smallest removal that achieves the goal is the right one: cutting
the recipe stops new ones being made and touches nothing already in a world.

The `.csproj` shows the same trade-off taken the other way, and shows what it costs.
`AdvancedElectronics.csproj:54-56` removes `AdvancedElectronicsAssembly.cs` from compilation
entirely, which deletes its item, its world object and its recipe together — anything already placed
will not load. That is the heavier removal, and it needs the release notes to say so. They do not:
the exclusion's own comment points readers at "the release notes' known-issues block", that block
never mentions it, and the shipped `README.txt` still tells players to build the Advanced Electronics
Assembly and craft at it. Two files each defer to the other for an explanation neither contains.
A worked example of why the removal and the notes have to be checked against the same artifact.

**Verify against the compiled artifact, never against the source.** `grep` has no idea what a
`/* … */` block means. Source-grepping a commented-out region reports exactly what a live region
reports, so it cannot distinguish the two states you are trying to tell apart. Build the assembly
and look for the type name in it. The DLL is what the server loads and what the release ships; it is
the only thing whose contents are a fact about the release.

**Give an artifact check a control.** A name-based search over a binary fails the same way whether
the type is absent or your spelling is wrong, and both look like success when absence is what you
are hoping for. Search alongside a name you know must be present, and confirm the name you are
searching for is the real one — the class in `HarvesterDrone.cs` is `HarvestDroneItem`, not
`HarvesterDroneItem`, and searching the filename's spelling returns zero for a type that is very
much in the DLL.

## Why This Matters

The failed version of this change would have shipped as a v0.2.0 that advertised two drones nobody
could build. That is a specific kind of bad: not a missing feature, which players never see, but a
visible dead end in the tech tree that reads as a broken mod and generates reports. The skill's
progression panel and the recipe browser are both driven by registration, so a registered-but-unbenched
recipe is maximally advertised and minimally functional.

The verification half matters more than the fix half, because the fix was one edit and the wrong
verification would have survived it. Commenting out the class and then grepping the source for
`MiningDroneRecipe` returns four hits — the same four it returned before the change, because the
declaration, the constructor, and both `typeof(...)` references are all still there as text. A
source grep confirms nothing here at all; run against a `/* … */` block it produces a positive
result that means nothing, and against a deletion it produces a negative result that would have been
true either way. Only the built assembly separates the two.

This shape is not specific to Eco. Anywhere a framework finds work by scanning — Rails
`ApplicationJob` subclasses, JUnit test classes, pytest collection by filename, Spring's component
scan, ASP.NET controller discovery, a plugin loader walking a directory — the declaration is the
registration, and there is no "off" switch inside the thing being declared. The instinct to disable
the body and leave the shell is the instinct that fails.

## When to Apply

- Cutting content from a release under any framework that discovers types by derivation, attribute,
  annotation, or convention rather than by explicit registration.
- Whenever the fix under consideration is "comment out the line that does the thing" rather than
  "remove the thing". Check first whether the framework already has a reference by the time that
  line runs.
- Before claiming any removal took effect, when the evidence on hand is a source search.
- When deciding how much to remove: content that a save file can already hold needs its type kept
  and only its means of creation withdrawn, unless the project ships migrations.
- When re-enabling withheld content — the restore is deleting the `/*` and `*/`, and it should be
  verified by the same artifact check, run in the opposite direction.

## Examples

The comment that records the correction, at `EcoServerMod/AdvancedElectronics/MiningDrone.cs:252`
(`HarvesterDrone.cs:252` is the same text for the harvest drone):

```csharp
// WITHHELD FOR THE NEXT RELEASE -- the mining drone's arm does not yet behave the way it is
// meant to during flight, so the drone is not offered to players.
//
// The whole class is commented out rather than just its table registration. RecipeFamily
// carries [ForceCreateViewAllDerived], so the type existing is enough: Eco instantiates it at
// startup and Initialize() registers the recipe, which leaves it visible in the recipe browser
// and the skill's tech tree even when it belongs to no bench. Only removing the type removes
// it from the game.
//
// The item and world object are deliberately left defined, so a save that already holds a
// mining drone keeps loading. Restore by deleting the /* and */ below.
```

The superseded theory is still readable at `MiningDrone.cs:305-311`, inside the commented-out block,
claiming that "Registering no table is what hides it". It is inert text now, but it is the wrong
explanation sitting a few lines below the right one, and anyone restoring the recipe should delete
it rather than trust it.

The verification, run against this tree. First the build:

```
$ dotnet build EcoServerMod/AdvancedElectronics/AdvancedElectronics.csproj
    639 Warning(s)
    0 Error(s)
```

Then the artifact, with live recipes as the control:

```
$ cd EcoServerMod/AdvancedElectronics/bin/Debug/net10.0
$ for n in MiningDroneRecipe HarvestDroneRecipe SurveyDroneRecipe DroneDockRecipe \
           MiningDroneItem MiningDroneObject HarvestDroneItem HarvestDroneObject; do
    echo "$n: $(strings -n 6 AdvancedElectronics.dll | grep -c -x "$n")"
  done
MiningDroneRecipe: 0
HarvestDroneRecipe: 0
SurveyDroneRecipe: 1
DroneDockRecipe: 1
MiningDroneItem: 1
MiningDroneObject: 1
HarvestDroneItem: 1
HarvestDroneObject: 1
```

Both withheld recipes are gone from the assembly. Both live recipes are present, which proves the
search works and the zeros mean something. All four item and world-object types are present, which
is the save-compatibility guarantee, checked rather than assumed.

And the check that proves nothing, for contrast:

```
$ grep -c MiningDroneRecipe EcoServerMod/AdvancedElectronics/MiningDrone.cs
4
```

Four hits in a file where the recipe has been withheld. The source and the artifact disagree, and
the artifact is the release.

## Related

- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — read this one
  next to that one. It names `CraftingComponent.AddRecipe` as the line to audit for template
  residue, which is right for its own purpose but, read alone, invites exactly the wrong conclusion
  here: `AddRecipe` controls bench attachment only, never whether the recipe exists.
- `docs/solutions/conventions/a-talent-that-does-not-inherit-unlocks-nothing.md` — the same
  discovery-by-reflection principle in the talent system, where nothing registers explicitly either.
- `docs/solutions/runtime-errors/a-mod-recipe-that-closes-a-cycle-in-the-skill-graph.md` — the other
  way a recipe type's mere existence has consequences beyond its bench, there by writing an
  unintended edge into the tech tree.
- `docs/solutions/conventions/requirecomponent-binds-at-creation-not-retroactively.md` — the same
  closing move from a different angle: check the shipped artifact, not the running world, when the
  question is what a release contains.
- `docs/solutions/conventions/usercode-cannot-name-a-mod-dll-type.md` — the other place where what
  the compiled assembly contains, rather than what the source says, decides the outcome.
