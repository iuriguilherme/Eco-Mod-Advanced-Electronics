---
title: "AllowPluginModules does not gate module admission — find the consumer before vendoring a table"
date: 2026-08-02
category: conventions
module: EcoServerMod
problem_type: convention
component: plugin_modules
severity: high
applies_when:
  - "Making a vanilla crafting table accept a mod's upgrade module"
  - "Considering a UserCode override of an AutoGen WorldObject file"
  - "Reading an attribute name as evidence of what it controls"
  - "Re-deriving an existing override on an Eco update"
tags: [eco-modding, plugin-modules, allowpluginmodules, usercode-override, attributes, verification, maintenance-cost]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/UserCode]
---

# AllowPluginModules does not gate module admission — find the consumer before vendoring a table

Paths beginning `Server/` and `Mods/__core__/` below are Eco's own trees — the engine source
checkout and the dedicated server's shipped core mod — not files in this repository.

## Context

Every vanilla crafting table declares which modules it accepts, and the declaration reads like a
gate:

```csharp
[AllowPluginModules(ItemTypes = new[] { typeof(ElectronicsUpgradeItem), typeof(IndustryUpgradeItem),
                                        typeof(BasicUpgradeItem), typeof(AdvancedUpgradeItem),
                                        typeof(ModernUpgradeItem) })] //noloc
public partial class ElectronicsAssemblyItem : WorldObjectItem<ElectronicsAssemblyObject>
```

A mod's own upgrade module is not in that list. The obvious conclusion — and the one this repo
acted on — is that the table will refuse it, so the table must be overridden. That conclusion
produced a vendored whole-file copy of upstream source under `EcoServerMod/UserCode/`, a deploy
script to install it, an install step in the release notes, and a re-derivation obligation on every
Eco update.

Nobody ever tested a module against an unmodified table. The attribute's *name* was the evidence.

## Guidance

**Grep for the attribute's consumers before treating it as a gate.** In Eco 0.14 the attribute is
read in exactly three places, and each reads a different property for a different purpose:

| Read | Where | What it does |
|---|---|---|
| `.Slots` | `Server/Eco.Gameplay/Modules/ModuleSlotRegistry.cs:83` | picks the table's slot set; `null` falls back to the four core slots |
| `Has<AllowPluginModulesAttribute>` | `Server/Eco.Gameplay/Modules/PluginModule.cs:109` | builds each module's "Plugs Into" tooltip — presence only, never the contents |
| `.GetStackables()` | `Server/Eco.Gameplay/Systems/NewTooltip/TooltipLibraryFiles/ItemTooltipLibrary.cs:494` | renders the table's accepted-modules tooltip |

`.Tags` and `.ItemTypes` feed `GetStackables()` and nothing else. The single consumer is a tooltip.

**Admission is decided by the slot, matching the module's own tags**
(`Server/Eco.Gameplay/Items/InventoryRelated/InventoryRestrictions.cs:495`):

```csharp
public override int MaxAccepted(Item item) =>
    item != null && item.Tags().Any(t => t.Name == this.SlotTagName) ? -1 : 0;
```

So a module carrying `[Tag("SpecialtyModule")]` is admitted by any table exposing a Specialty slot —
which is every vanilla table, because none of them declare `.Slots` and the fallback includes it.
`PluginModulesComponent` states the change outright: the legacy per-station restriction is gone,
and every craft station accepts every module of the right slot type.

**The mechanism above is verified from source, and partly observed in game.** A mod upgrade module
was slotted into the Robotic Assembly Line during live testing and landed in the **Specialty**
slot — which is `ModuleSlotRestriction` matching the module's own `SpecialtyModule` tag, since
`AllowPluginModules.Tags` has no path to slot routing at all. So slot-based admission is confirmed
operating, not merely inferred.

**What is still unverified is the conclusion, not the mechanism.** That test ran with the table's
override already deployed, so it is not an A/B: nobody has slotted a mod module into a table with
no override in place. Treat "the override is unnecessary" as strongly indicated and cheap to
confirm — one placement on a stock table settles it — rather than as established.

## Why This Matters

The wrong belief is expensive in a way that compounds quietly. Each table you "need" to override
costs a byte-complete copy of upstream source that must be re-derived on every Eco update, plus
whatever install plumbing carries it to a server. That cost lands on every future maintainer, and
it lands *per table* — the pattern scales with content, not with the mistake.

It is also self-concealing. A vendored override works: the module slots, the tooltip lists it,
everything looks correct. Nothing ever fails, so nothing prompts anyone to ask whether the override
was load-bearing. The only way the question comes up is by reading the engine.

And the belief propagates into writing. Before this was checked, three separate places in this repo
asserted it as fact — a solutions doc's opening premise, a deploy script's header comment, and a
class comment on the module itself. Each one was written in good faith by someone who had read the
previous one.

## When to Apply

- Before writing any UserCode override to make a vanilla object accept mod content. Find the
  consumer of the field you think is blocking you.
- When an attribute's name states a policy. `AllowX` reads as enforcement; here it is presentation.
  The name is a claim about intent, not about behavior — behavior is whatever reads the field.
- When re-deriving an existing override on an Eco update, before paying that cost again.
- When a comment in this repo says a table "needs" an override to accept a module. That claim is
  the one this doc corrects.

## Examples

The check that settles it, and the shape of a trustworthy answer:

```bash
# 1. Who reads the property you believe is a gate?
grep -rn "GetStackables()" Server/ --include=*.cs
#    -> the definition, plus ItemTooltipLibrary.cs:494. One consumer. A tooltip.

# 2. Who reads the attribute at all?
grep -rn "AllowPluginModulesAttribute" Server/ --include=*.cs
#    -> three sites, three different properties, three different purposes.

# 3. What actually decides admission, then?
grep -rn "class ModuleSlotRestriction" -A 10 Server/ --include=*.cs
#    -> MaxAccepted matches the ITEM's tags against the SLOT's tag name.
```

Two consumers reading two properties of one attribute is what makes the name misleading. The
property that sounds like the gate (`ItemTypes`) is display; the property that shapes real behavior
(`Slots`) is unrelated to the module list and usually null.

The corollary worth carrying: a module already advertises itself from its own side.
`PluginModule.Initialize` maps every module to every station carrying the attribute, so the
module's "Plugs Into" tooltip names the table regardless. An override adds a second listing from
the table side, not the only one.

## Related

- `docs/solutions/conventions/usercode-cannot-name-a-mod-dll-type.md` — opens on the premise this
  doc corrects. Its actual lesson, about the UserCode compile boundary, still holds: an override
  cannot name a mod DLL type, which is why the tag form exists. Only its framing of *why* an
  override is needed is wrong.
- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the same
  reflex applied to generated content: a field is present because the generator emits it, not
  because it carries information for your case.
- `docs/solutions/conventions/a-talent-that-does-not-inherit-unlocks-nothing.md` — from the same
  session, the mirror image: there a declaration reads as sufficient and is skipped by its
  consumer; here a declaration reads as load-bearing and is only rendered.
