---
title: "UserCode cannot name a type from a mod DLL, so a table override matches on a tag"
date: 2026-08-01
last_updated: 2026-08-02
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: high
applies_when:
  - "Adding a mod's plugin module to a vanilla craft table"
  - "Writing a Mods/UserCode override that needs to reference a mod's own type"
  - "Deciding whether a mod ships as a compiled DLL or as source"
  - "A UserCode file fails with CS0246 on a type that plainly exists"
tags: [eco-modding, usercode, plugin-modules, compilation, workaround, boot-failure]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/UserCode]
---

# UserCode cannot name a type from a mod DLL, so a table override matches on a tag

## Context

The Advanced Electronics Upgrade is a plugin module meant to slot into the Robotic Assembly Line,
which is a vanilla table. The table's `[AllowPluginModules]` does not list the module, and a mod
assembly cannot add to that attribute — attributes merge across partial declarations only within a
single assembly. Eco's escape hatch is a whole-file override: a file under `Mods/UserCode/` whose
path matches a `__core__` file, with `.override` before the extension, replaces it.

**That attribute does not actually gate admission** — see
`docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md`, which corrects the belief
this doc was originally written under. The override buys the table's accepted-modules tooltip
listing, not the ability to slot. What follows still holds regardless: any UserCode file that names
a mod type fails the same way, whatever the file is for.

The obvious override named the module by type, exactly as vanilla does:

```csharp
[AllowPluginModules(ItemTypes = new[] { typeof(AdvancedElectronicsUpgradeItem), /* vanilla ones */ })]
```

That took the server down at boot:

```text
RoboticAssemblyLine.override.cs(110,52): error CS0246: The type or namespace name
'AdvancedElectronicsUpgradeItem' could not be found (are you missing a using directive
or an assembly reference?)
   at Eco.ModKit.RoslynCompiler.HandleCompilerError(ImmutableArray`1 diagnostics)
```

## Guidance

**UserCode is compiled against the engine, never against mod DLLs.** `RoslynCompiler` builds every
`.cs` under `Mods/` into one assembly, and its reference list is a fixed array of engine assemblies
— `Eco.Core`, `Eco.Gameplay`, `Eco.ModKit`, `Eco.Shared`, `Eco.Simulation`, `Eco.World`,
`Eco.Stats`, `Eco.WorldGenerator`, plus a few third-party. A compiled mod is loaded separately and
is not among them. So no UserCode file can name a type from a mod DLL, and the failure is fatal at
boot rather than a skipped file.

**`CollectModSourceFiles` also decides what is even eligible.** It sorts every `.cs` under `Mods/`
into three buckets: `__core__` (overridable), `UserCode` (compiled), and everything else, which is
ignored with a warning. `.override.cs` is resolved only while iterating the `__core__` set, so an
override exists to replace a vanilla file and nothing else. Dropping a mod's own source next to its
DLL does not get it compiled.

**Match on a tag instead — it is a string, and a string needs no reference.**
`AllowPluginModulesAttribute` carries `Tags` (`string[]`) alongside `ItemTypes`. Give the module its
own tag and have the override add that:

```csharp
[Tag("AdvancedElectronicsUpgrade")] //noloc  -- on the module item, in the mod DLL

// in UserCode/AutoGen/WorldObject/RoboticAssemblyLine.override.cs
[AllowPluginModules(Tags = new[] { "AdvancedElectronicsUpgrade" }, ItemTypes = new[] { /* vanilla */ })]
```

Use the module's **own** tag, not a shared one like `SpecialtyModule`, or the override admits every
specialty upgrade rather than this one.

**Know that this is not what vanilla does.** No shipped table matches modules by `Tags` — every one
enumerates `ItemTypes`. `Tags` is a real, supported property, but using it is a workaround for a
compilation boundary, not the house style. Say so where it lives, or the next reader will assume it
is the pattern to copy.

**Do not reach for a runtime append.** An earlier version of this doc recommended mutating the
cached attribute at startup — `ItemAttribute.Get<T>` does return the cached instance rather than a
copy, and `ItemTypes` does have a public setter, so the mechanism works. It buys nothing. The
properties it would append to are read by one consumer, an item tooltip; there is no later gate in
`PluginModulesComponent` or `ModuleSlotRegistry` for the append to reach. A runtime append and a
whole-file override produce the same tooltip line at different costs.

## Why This Matters

The compile boundary is invisible from the source. A UserCode override reads like ordinary C# in the
same namespace as the type it references, and the type demonstrably exists — it is in the DLL sitting
one directory away, and the rest of the mod compiles against it fine. Nothing in the file hints that
it is compiled by a different compiler, into a different assembly, with a different reference set.

The failure mode is also maximally expensive. It is not a warning, not a skipped mod, not a broken
tab: the mods assembly fails to compile and the server does not start. A one-line attribute change
costs a boot, and the error names a type the developer can see with their own eyes, which sends the
search toward usings and namespaces rather than toward assembly boundaries.

It also forces a real decision about how the mod ships. Everything under `Mods/UserCode/` is
patchable by server owners through partial methods; everything in a DLL is not, because partials do
not span assemblies either. A compiled mod buys a test project, a separate navigation library, and
no per-server compile — and gives up user-side customization and any UserCode reference to its own
types. That trade should be made deliberately, not discovered through a boot failure.

## When to Apply

- Before writing any `Mods/UserCode` file that references a mod's own type.
- When adding a mod's plugin module to a vanilla craft table.
- When a mod needs to change a vanilla item attribute that a mod assembly cannot extend.
- When choosing between shipping as a DLL and shipping as UserCode source.
- Whenever a boot fails with CS0246 on a type that exists — check which assembly is compiling.

## Examples

The reference list that decides everything, from `Eco.ModKit/RoslynCompiler.cs`:

```csharp
"Eco.Core.dll", "Eco.Gameplay.dll", "Eco.ModKit.dll", "Eco.Shared.dll",
"Eco.Simulation.dll", "Eco.World.dll", "Eco.Stats.dll", "Eco.WorldGenerator.dll",
"LiteDB.dll", "Priority Queue.dll", "PropertyChanged.dll",
"NetFabric.Hyperlinq.Abstractions.dll"
```

No mod DLL appears, and nothing adds one.

Why a whole-file override is generated rather than committed, and why it is verified:

```bash
# scripts/deploy-usercode-overrides.sh --refresh re-derives it from the server's own __core__,
# then refuses to install a copy whose line count drifted from the original -- a truncated
# whole-file override silently deletes the table it was meant to extend.
CORE_LINES=$(wc -l < "$CORE"); NEW_LINES=$(wc -l < "$TRACKED")
[ "$CORE_LINES" -eq "$NEW_LINES" ] || exit 1
```

## Related

- `docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md` — corrects this doc's
  original premise. `[AllowPluginModules]` is presentation, not admission; a table admits a module
  by matching the module's own slot tag. Read that one to decide whether you need an override at
  all; read this one for what happens when you write one.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the same shape on
  the client side: what a mod may and may not extend without shipping into the game's own build.
- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — also about assuming a
  type is reachable because it is readable. There the wrong version; here the wrong assembly.
