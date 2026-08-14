---
title: "Source shows what the code does, not what it is meant to do — the 0.14 module-admission gap"
date: 2026-08-02
last_updated: 2026-08-10
category: conventions
module: EcoServerMod
problem_type: convention
component: plugin_modules
severity: high
applies_when:
  - "Deciding whether a workaround for a vanilla limitation can be removed"
  - "Making a vanilla crafting table accept a mod's upgrade module"
  - "A source read shows a restriction is gone and a workaround looks redundant"
  - "Weighing a maintenance cost against behavior observed in the current build"
tags: [eco-modding, plugin-modules, allowpluginmodules, usercode-override, upstream-intent, transient-behavior, verification]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/UserCode]
---

# Source shows what the code does, not what it is meant to do — the 0.14 module-admission gap

Paths beginning `Server/` and `Mods/__core__/` below are Eco's own trees — the engine source
checkout and the dedicated server's shipped core mod — not files in this repository.

## Context

This mod ships a UserCode override of a vanilla crafting table so the table's
`[AllowPluginModules]` names the mod's upgrade module. That override is a real cost: a byte-complete
copy of upstream source, re-derived on every Eco update, plus a script to install it.

**Resolved 2026-08-10: the gate came back, and keeping the override was right.**
`Server/Eco.Gameplay/Components/PluginModulesComponent.cs:407-409` now reads the table's allow-list
and applies it as a real restriction — `GetStackables()` feeding
`this.Inventory.AddInvRestriction(new StackableRestriction(this.allowedModules))`. The upstream
comment this doc quoted, saying every station accepts every module of the right slot type, is gone
and replaced by its opposite. The override at
`EcoServerMod/UserCode/AutoGen/WorldObject/RoboticAssemblyLine.override.cs:110` is now load-bearing
rather than defensively load-bearing. The account below is the state that produced the decision.

A source read appeared to show the cost bought nothing. In Eco 0.14 as it stood on 2026-08-02, the
attribute's `Tags` and `ItemTypes` fed exactly one consumer — an item tooltip — and slot admission
was decided elsewhere, by the module's own tag. That reading was correct and reproducible.

**It was also a description of a bug.** Strange Loop Games considered the permissiveness undesired;
it was reported and slated to be fixed before launch. The per-station gate was coming back — and it
did.

So the override was not redundant. It was a workaround that appeared redundant because the thing it
works around was temporarily broken.

## Guidance

**Keep the override.** A mod that removes it on the strength of current behavior breaks when the fix
lands — silently, at someone else's server, after release.

**The mechanism finding was accurate for the window it described**, and the reads below still exist —
what changed is that a fourth consumer joined them. During the gap:

| Read | Where | What it does |
|---|---|---|
| `.Slots` | `Server/Eco.Gameplay/Modules/ModuleSlotRegistry.cs:83` | picks the table's slot set; `null` falls back to the four core slots |
| `Has<AllowPluginModulesAttribute>` | `Server/Eco.Gameplay/Modules/PluginModule.cs:109` | builds each module's "Plugs Into" tooltip — presence only |
| `.GetStackables()` | `Server/Eco.Gameplay/Systems/NewTooltip/TooltipLibraryFiles/ItemTooltipLibrary.cs:494` | renders the table's accepted-modules tooltip |

Each slot is wired with three restrictions and none consults the table's allowed list
(`Server/Eco.Gameplay/Items/PluginModulesInventory.cs:69-74`):

```csharp
void WireSlot(AuthorizationInventory leaf, string slotTagName)
{
    leaf.RemoveAllRestrictions(r => r is ModuleSlotRestriction or StackLimitRestriction or PermanentModuleRestriction);
    leaf.AddInvRestriction(new StackLimitRestriction(1, staticLimit: true));
    leaf.AddInvRestriction(new ModuleSlotRestriction(slotTagName));   // matches the ITEM's tag
    leaf.AddInvRestriction(new PermanentModuleRestriction());
}
```

The mechanism that implements per-station gating, `StackableRestriction`, existed as a class that
nothing instantiated. `PluginModulesComponent.cs` called it "the legacy per-station
StackableRestriction" and said every craft station now accepts every module of the right slot type.
That comment read as a settled design change. It was describing the state the bug report was against
— and it has since been replaced by its opposite, with the allow-list applied at
`PluginModulesComponent.cs:407-409` and the class instantiated again.

**Treat "the restriction is gone" as a question, not an answer.** A removed guard has two possible
explanations that look identical in source: deliberate simplification, or a regression nobody has
fixed yet. The tree cannot distinguish them. Ask the maintainer, check the issue tracker, or read
release notes before hardening a decision on the gap.

## Why This Matters

The failure this prevents is a workaround deleted for good reasons that stop being good.

The evidence for removal was strong by every standard available inside the repository: three
consumers found by grep, a wiring function read in full, an unused restriction class, and an
upstream comment stating the change in upstream's own words. Two independently dispatched reviewers
reached the same conclusion from the same source. Nothing in that chain was wrong, and the
conclusion still did not follow.

What none of it could see is that the behavior is unintended and scheduled for removal. Intent lives
in a bug tracker, a roadmap, and a maintainer's head — never in the tree. A source read answers
"what does this do today," and quietly presents itself as an answer to "what can I rely on."

The asymmetry decides the call. Keeping an unnecessary override costs a re-derivation per Eco
update. Removing a necessary one costs a broken mod on every server after a patch nobody in this
repo controls, discovered by players rather than by a test.

## When to Apply

- Before deleting any workaround because current behavior makes it look redundant — especially a
  workaround for a limitation in someone else's code.
- When a source read contradicts a maintainer's expectation. The maintainer may know about a fix in
  flight; the tree never does.
- When a comment in upstream source describes a removal as intentional. It documents what changed,
  not whether the change survives.
- When weighing a recurring maintenance cost against a one-time correctness risk on a dependency
  you do not control. The recurring cost is visible and bounded; the risk is neither.

## Examples

The check that produced the wrong conclusion — sound method, incomplete inputs:

```bash
# Who reads the property that looks like a gate?
grep -rn "GetStackables()" Server/ --include=*.cs
#   -> the definition, plus ItemTooltipLibrary.cs:494. One consumer. A tooltip.

# Is the per-station restriction still applied anywhere?
grep -rn "new StackableRestriction" Server/ --include=*.cs
#   -> no output. The class exists; nothing instantiates it.
```

Both results were accurate. Neither could tell you the state was a defect — and both now return the
opposite, which is the point: the same two commands, run three months apart, would have justified
deleting the override and then justified keeping it. Re-running them is not what makes the answer
trustworthy.

The question that resolves it costs one message: *"is this deliberate, or a bug you know about?"*
Here the answer was that it is reported and will be fixed before launch, which turned a
recommended deletion into a recommended keep.

## Related

- `docs/solutions/conventions/usercode-cannot-name-a-mod-dll-type.md` — the override this doc is
  about, and why it matches on a tag rather than naming the module's type. Its compile-boundary
  lesson is independent of any of this and holds regardless.
- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — the adjacent trap:
  there, reading the wrong version of a dependency; here, reading the right version at the wrong
  moment in its life.
- `docs/solutions/conventions/a-talent-that-does-not-inherit-unlocks-nothing.md` — from the same
  session, the inverse: there the source told the whole truth and the intuitive reading was wrong;
  here the source read was right and still produced the wrong decision.
