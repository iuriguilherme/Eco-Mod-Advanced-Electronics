---
title: "Naming a component hides it from the vanilla component that consumes it"
date: 2026-08-01
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: worldobject_components
severity: high
symptoms:
  - "NullReferenceException in a vanilla component's Tick, on the first tick of real work"
  - "The object works until the exact moment the feature is first used"
  - "The paired component renders and accepts input, so it plainly exists"
root_cause: "A named component cannot be found by a vanilla consumer that resolves its partner with an unnamed GetComponent, because GetComponent matches on name as well as type."
resolution_type: code_fix
applies_when:
  - "Installing components dynamically through the module system"
  - "Giving a component a name via RequireComponent's name argument or ComponentInstallation.For"
  - "Adding a second component of a type an object already carries"
tags: [eco-modding, worldobjectcomponent, getcomponent, named-components, module-system, fuel, nullreference, crash]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Naming a component hides it from the vanilla component that consumes it

## Problem

A drone item installed a `FuelSupplyComponent` and a `FuelConsumptionComponent` onto its dock,
both name-stamped so the dock's own lookups would stay unambiguous. The dock accepted the drone,
showed a fuel tab, and took fuel. The server then crashed the moment the drone started working.

## Symptoms

The server dies inside a **vanilla** component's tick, on an object the mod defines:

```text
System.NullReferenceException: Object reference not set to an instance of an object.
   at Eco.Gameplay.Components.FuelConsumptionComponent.Tick()
   at Eco.Gameplay.Objects.WorldObject.TickComponents()
   at Eco.Mods.TechTree.DroneDockObject.Tick()
   at Eco.Gameplay.Objects.WorldObjectManager.TickObject(WorldObject worldObject)
```

The timing is the distinctive part. Placement is fine, slotting is fine, fuelling is fine. The
crash arrives at the first tick where the object is actually *operating* — in this case the instant
a survey area was assigned and the drone began work.

## What Didn't Work

Reading the mod's own code for a null. There is no null to find there: the mod never touches
`FuelConsumptionComponent`'s internals, and the component it depends on demonstrably existed —
the fuel tab rendered it and accepted biodiesel. The natural conclusion is that installation
failed, which sends the search into the module driver, where nothing is wrong either.

The install log said as much and was believed too readily:

```text
[Info] Drone Dock: installed SurveyDroneItem's components.
```

Both components *were* installed. Installation was never the problem.

## Solution

Install the pair unnamed:

```csharp
// Before -- name-stamped so dock lookups stayed unambiguous. Crashes on first work tick.
ComponentInstallation.For<FuelSupplyComponent>(
    name:      nameof(SurveyDroneItem),
    configure: c => c.Initialize(2, fuelTagList)),
ComponentInstallation.For<FuelConsumptionComponent>(
    name:      nameof(SurveyDroneItem),
    configure: c => c.Initialize(FuelJoulesPerSecond)),

// After -- the vanilla pairing is not name-aware, so it is not ours to name.
ComponentInstallation.For<FuelSupplyComponent>(
    configure: c => c.Initialize(2, fuelTagList)),
ComponentInstallation.For<FuelConsumptionComponent>(
    configure: c => c.Initialize(FuelJoulesPerSecond)),
```

Any lookup the mod does for that component must drop the name too, or it inherits the same bug
from the other side.

## Why This Works

`FuelConsumptionComponent` resolves its partner in its own `Initialize`, passing **no name**
(`Server/Eco.Gameplay/Components/FuelConsumptionComponent.cs:44`):

```csharp
this.fuelSupply = this.Parent.GetComponent<FuelSupplyComponent>();
```

And `GetComponent` matches on name as well as type, with the parameter defaulting to `null`
(`Server/Eco.Gameplay/Objects/WorldObjectComponent.cs:186`):

```csharp
if (componentType.IsInstanceOfType(component) && component.Name == name)
```

A component named `"SurveyDroneItem"` fails `component.Name == null`. So the consumer's lookup
returns null, `fuelSupply` stays null, and nothing notices — `Initialize` does not null-check it.
The field is dereferenced only in `Tick`, and only inside `if (this.Parent.Operating)`
(`FuelConsumptionComponent.cs:59`). That guard is why the crash waits for the first tick of real
work rather than arriving at placement.

The general rule: **a name makes a component findable only by lookups that pass the same name.**
Naming is a way to disambiguate *your own* lookups, and it silently breaks any consumer that does
not know to ask for it. Vanilla's `RequireComponentAttribute` says as much about its purpose
(`Server/Eco.Gameplay/Objects/WorldObjectUtil.cs:44`):

```csharp
public string ComponentName; // For objects that have multiple components of the same type.
```

Multiple components of the same type is the case names exist for. That was not the case here.

## Prevention

**Name a component only when the host already carries an unnamed one of that type.** That is the
ambiguity names solve. A component the host does not otherwise have needs no name, and naming it
can only cost you.

**Before naming, find out who consumes it.** Grep the engine source for unnamed lookups of the
type, and treat any hit as a veto:

```bash
# every consumer that will fail to find a NAMED FuelSupplyComponent
grep -rn "GetComponent<FuelSupplyComponent>()" Server/ --include=*.cs
```

**Suspect a resolution problem when a crash is late and the object is fine.** A null that survives
`Initialize` and dies in `Tick` behind an `Operating` guard is characteristic of a lookup that
returned nothing at construction time. Placement, rendering, and input all working is evidence
*for* this diagnosis, not against it — the component exists, it just was not found.

**Do not let an install log close the question.** "Installed N components" confirms attachment, not
resolution. The two components in this case were both attached and still could not see each other.

## Related

- `docs/solutions/conventions/requirecomponent-binds-at-creation-not-retroactively.md` — the other
  half of component-attachment surprise: there, when a component gets attached; here, whether a
  consumer can find it once it is.
- `docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md` — also a
  failure during component initialization, but loud and immediate rather than deferred to a later
  tick.
- `docs/solutions/conventions/usercode-cannot-name-a-mod-dll-type.md` — from the same work: another
  place where a mod's declaration compiles and reads correctly while the engine cannot resolve what
  it names.
