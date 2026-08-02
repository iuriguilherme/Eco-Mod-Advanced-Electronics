---
title: "A talent that does not inherit its base unlocks nothing, silently"
date: 2026-08-02
category: conventions
module: EcoServerMod
problem_type: convention
component: talents
severity: high
applies_when:
  - "Adding a Talent to a mod skill for the first time"
  - "Gating a recipe behind a talent with RequiresTalentUnlock"
  - "Copying the three-type talent shape out of a vanilla AutoGen Benefit file"
  - "A learnable talent appears to do nothing in game"
tags: [eco-modding, talents, talentgroup, bonusmanager, recipe-unlock, silent-failure, inheritance, autogen]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A talent that does not inherit its base unlocks nothing, silently

## Context

Paths beginning `Server/` and `Mods/__core__/` below are Eco's own trees — the engine source
checkout and the dedicated server's shipped core mod — not files in this repository.

Eco spreads one talent across three types, and vanilla's own file makes the split look like
three siblings. `Mods/__core__/AutoGen/Benefit/EtchingTechniques.cs` declares a base talent, a
`TalentGroup`, and a skill-bound talent — and the AutoGen half of the base talent is nearly empty:

```csharp
public partial class EtchingTechniquesTalent : Talent
{
    public override bool Base => true;
    public override Type TalentType { get { return typeof(CraftingTalent); } }
}
```

The `Bonus` that actually unlocks anything lives in a *different file*, a hand-written partial at
`Mods/__core__/Benefits/EngineerProfession.cs:234-244`. So reading the AutoGen file alone, the
three types look independent and the natural mod shape is three siblings, each deriving from
`Talent`.

That shape compiles, boots, and does nothing.

## Guidance

**The skill-bound talent must derive from the base talent subclass — not from `Talent`.**

```csharp
// Right: the skill-bound type inherits the base constructor, so it carries the unlock Bonus.
public partial class AdvancedElectronicsSulfuricBatteryTalent : SulfuricBatteryTalent
{
    public override bool Base => false;
    public override Type TalentGroupType => typeof(AdvancedElectronicsSulfuricBatteryTalentGroup);
}

// Wrong: compiles, is learnable, and unlocks nothing.
public partial class AdvancedElectronicsSulfuricBatteryTalent : Talent
{
    public override bool Base => false;
    public override Type TalentGroupType => typeof(AdvancedElectronicsSulfuricBatteryTalentGroup);
}
```

Vanilla only gets this right because `ElectronicsEtchingTechniquesTalent : EtchingTechniquesTalent`
— the inheritance is the mechanism, not a stylistic choice.

**`RequiresTalentUnlock = true` on the recipe does not name the talent.** The binding runs the
other way: the recipe's flag only makes it *gateable*, and the talent's `Bonus` is what ungates it.
A recipe with the flag and no talent naming it stays permanently unworkable.

## Why This Matters

`BonusManager.FindUnlockingTalents` skips every base talent before it looks at any bonus
(`Server/Eco.Gameplay/Bonuses/BonusManager.cs:100-108`):

```csharp
foreach (var talent in TalentManager.TypeToTalent.Values)
{
    if (talent.Base) continue;
    foreach (var bonus in talent.Bonuses)
        if (bonus.WouldApply(new BonusContext { Action = BonusAction.Unlock, Recipe = recipe }))
            { yield return talent; break; }
}
```

`Base` reads like metadata. It is a filter. The base talent holds the `Bonus` and is skipped; the
skill-bound talent is examined and — without inheritance — has an empty `Bonuses` collection. So
the search finds nothing, and two separate player-facing surfaces go quiet at once: labor
contribution stays blocked (`Server/Eco.Gameplay/Items/WorkOrder.Labor.cs:146`), and the
"Requires: …" note that would name the talent never renders, because it is built from the same
lookup (`Server/Eco.Gameplay/Items/Recipes/RecipeFamily.cs:91-97`).

**Nothing fails.** There is no build error, no test failure, and no log line. The server boots,
`TalentManager.InitializeTalents` discovers the type by reflection, the talent group appears on the
skill page at its level, and the player can spend a star to learn it. The only observable is a
recipe that stays unworkable forever — and the star is already gone.

The debugging path from that symptom is bad. The visible facts all say the talent system is
working, so the search starts at the recipe, then the flag, then the group's skill and level. The
cause is three inheritance edges away from anything that looked wrong.

## When to Apply

- When adding the first talent to any mod skill — this is exactly when the three-type shape has no
  local precedent to copy.
- When a talent is learnable but its recipe stays unworkable. Check the inheritance chain before
  anything else.
- When reading a vanilla AutoGen `Benefit/` file as a template. The AutoGen half is deliberately
  thin; the behavior lives in a hand-written partial elsewhere. A mod compiles as one assembly, so
  both halves can sit in one file — but they still have to be two types in an inheritance
  relationship, not one merged type.

## Examples

The shape, with the load-bearing edge marked. All three types can live in one file in a mod:

```csharp
// 1. Base — carries the Bonus. Skipped by FindUnlockingTalents.
public partial class SulfuricBatteryTalent : Talent
{
    public override bool Base => true;
    public override Type TalentType => typeof(CraftingTalent);

    public SulfuricBatteryTalent()
    {
        this.Bonuses.Add(new Bonus
        {
            Name    = Localizer.DoStr("Sulfuric Battery"),
            Causes  = new List<BonusCause>  { new CraftBonusCause { Action = BonusAction.Unlock,
                      Recipes = new HashSet<Type> { typeof(SulfuricBatteryRecipe) } } },
            Effects = new List<BonusEffect> { new BonusEffectOverride { Value = 1f } },
        });
    }
}

// 2. Group — binds skill and level. LocDisplayName/LocDescription go HERE, not on the talent.
[Serialized]
[LocDisplayName("Sulfuric Battery: Advanced Electronics")]
public partial class AdvancedElectronicsSulfuricBatteryTalentGroup : TalentGroup
{
    public AdvancedElectronicsSulfuricBatteryTalentGroup()
    {
        Talents          = new Type[] { typeof(AdvancedElectronicsSulfuricBatteryTalent) };
        this.OwningSkill = typeof(AdvancedElectronicsSkill);
        this.Level       = 3;
    }
}

// 3. Skill-bound — MUST derive from (1), not from Talent.
[Serialized]
public partial class AdvancedElectronicsSulfuricBatteryTalent : SulfuricBatteryTalent
{
    public override bool Base => false;
    public override Type TalentGroupType => typeof(AdvancedElectronicsSulfuricBatteryTalentGroup);
}
```

Two things that need no work and are easy to over-engineer:

- **Nothing registers explicitly.** `TalentManager.InitializeTalents` finds talents by reflection,
  and `TalentGroup` derives `Item`, so groups are enumerated through
  `Item.AllItemsIncludingHidden`. The mod's `ModRegistration.cs` is untouched.
- **`StarCost` defaults to 1 and `MaxTalentLevel` is auto-computed**
  (`Server/Eco.Gameplay/Skills/Talent.cs`). Both are `virtual`, so a mod *can* override them —
  but every vanilla talent leaves them alone, and a talent whose only effect is an unlock computes
  to level 1 correctly on its own.

## Related

- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — verify against the
  reference assemblies the mod actually compiles against, not the source tree beside them. The
  whole talent API was confirmed present in `Eco.Gameplay.dll` before any of this was designed.
- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the general
  form: a shape copied from a generated template carries assumptions the template never states.
  Here the omission is an inheritance edge rather than a stale identifier.
- `docs/solutions/runtime-errors/naming-a-component-hides-it-from-its-vanilla-consumer.md` — the
  same failure class from a different mechanism: a declaration that compiles and reads correctly
  while an engine lookup silently returns nothing.
