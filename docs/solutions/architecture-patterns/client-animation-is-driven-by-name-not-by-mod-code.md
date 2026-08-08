---
title: "A mod cannot ship client code, so animation is driven by matching names"
date: 2026-08-08
category: architecture-patterns
module: EcoServerMod
problem_type: architecture_decision
component: animation
severity: high
applies_when:
  - "Making a modded WorldObject animate in response to server state"
  - "Reaching for a MonoBehaviour to bridge server data to a client component"
  - "Choosing names for the booleans a server pushes with SetAnimatedState"
  - "The client log says a referenced script on a mod prefab is missing"
tags: [eco-modding, animation, animator, modkit, il2cpp, client-server, silent-failure]
related_components: [Assets/Art/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Navigation]
---

# A mod cannot ship client code, so animation is driven by matching names

## Context

Making a drone animate looked like an ordinary Unity problem, so it got an ordinary Unity
answer: a `MonoBehaviour` on the prefab that listened for server state changes and called
`Animator.SetBool`. It was written, wired, shipped in the bundle, and did nothing at all.

The client log said so plainly, but only if you looked at the client log:

```
The referenced script (DroneAnimatorStates) on this Behaviour is missing!
```

A whole night went into the server half — names, push timing, state derivation — while the
client half could never have run.

## Guidance

**The Eco client cannot load mod code.** Its install is an IL2CPP build — the game directory
holds a single native `GameAssembly.dll` and no managed-assembly directory alongside it — so
every C# class is compiled to native code ahead of time and there is no runtime to load new
types into. Asset bundles carry no compiled code either. A
custom `MonoBehaviour` therefore arrives as *"the referenced script is missing"* — not a
packaging mistake, an architectural impossibility. The server DLL is server-only and never
reaches a player.

So a mod may use only components the client already has, and the ModKit's shipped library
under `Assets/EcoModKit/Scripts/` is that set.

**The client already does the animator bridging, by name.** When a modded object is built, the
client walks every parameter on its `Animator` and binds it to the server state of the same
name — bools to `SetBool`, floats to `SetFloat`, ints to `SetInteger`, triggers to
`SetTrigger`. Nothing is configured, and nothing appears in the Inspector, because the binding
is a runtime listener rather than a serialized call.

That makes the whole contract a name match:

```
server:  Parent.SetAnimatedState("IsAtHomeDock", true)
                                  └── same string ──┐
animator parameter:                    Bool "IsAtHomeDock"
```

The practical consequences:

- **An empty event list is correct.** `WorldObject`'s `OnStateEnabledEvents` /
  `OnStateDisabledEvents` / `OnStateChangedEvents` should stay empty for animation. They exist
  for wiring *other* reactions — audio, particles, toggling a renderer — and reading them as
  the animation mechanism leads to reimplementing what the client already does.
- **`Animator.SetBool` is unreachable from those events anyway.** It takes two arguments and a
  UnityEvent persistent call passes at most one. Its absence from the Inspector's method
  dropdown is a symptom of the design, not a gap to work around.
- **Declaring the names in `WorldObject.States` is optional but worth doing.** The client
  registers any animator parameter it does not already know. Declaring them costs nothing and
  covers the case where a prefab's parameters are not yet readable.

**Three state names are reserved and must never appear in `States`.** `WorldObject` registers
`Enabled`, `Operating` and `Using` itself — see the built-in event fields in
`Assets/EcoModKit/Scripts/WorldObject.cs` (`OnEnabledChanged`, `OnOperatingChanged`,
`OnUsingChanged`). A custom state repeating one of those makes archetype creation throw:

```
System.ArgumentException: An item with the same key has already been added. Key: Operating
```

The object is then never instanced — it does not render, previews no placement ghost, and
leaves the server log completely clean, because the failure is entirely client-side. Note the
asymmetry: the same name as an *animator parameter* is harmless and simply binds to the
engine's own channel; only the `States` array collides.

**Read the client log when the symptom is visual.** Everything in this failure class —
missing scripts, reserved-name collisions, archetype exceptions — is reported there and
nowhere else. A clean server log is not evidence that the server is innocent; it is evidence
that the server was never involved.

## Why This Matters

The instinct to write a bridging component is correct everywhere else in Unity, which is
exactly why it costs so much here. It compiles, it serializes into the prefab, it ships in the
bundle, and the only sign of trouble is one line in a log nobody thinks to open because the
symptom looks like a rendering problem.

The same instinct produced a second casualty in this repo: an in-world `TextMeshPro` readout
driven by a custom component, which rendered placeholder text forever because nothing could
ever update it.

Both were removed on the `feat/drone-animation-dock-footprint` branch. What replaced them is
nothing — the client was already doing the job, and the fix was to stop competing with it.

## When to Apply

- Before writing any `MonoBehaviour` intended to ship in a mod bundle. It will not run.
- When picking the names a server pushes: they are the animator's parameter names, and the
  match is the entire integration.
- When an object renders but never animates — check that the pushed names and the animator's
  parameter names are the same strings.
- When an object does not render *at all* and the server log is clean — check the client log
  for an archetype exception before touching server code.

## Examples

The whole client-side setup for a drone that animates:

```
Animator parameters:   IsAtHomeDock, IsWorking, ModeMining, ModeHarvest   (Bool)
WorldObject.States:    IsAtHomeDock, IsWorking, ModeMining, ModeHarvest
Event lists:           empty
```

And the server pushing them, in `DroneLifecycle.RefreshAnimationStates`:

```csharp
foreach (var (name, value) in state.AsNamedValues())
{
    if (this.lastPushedAnimationStates.TryGetValue(name, out var last) && last == value)
        continue;

    this.lastPushedAnimationStates[name] = value;
    this.Parent.SetAnimatedState(name, value);
}
```

No bridging component, on either side.

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the other
  name-matched contract, binding a prefab to its server class. Same failure shape: a string
  compared at runtime with nothing checking it, and silence when it does not match.
- `docs/solutions/build-errors/a-stray-bundle-tag-splits-the-bundle-and-nothing-renders.md` —
  the other reason a correct-looking prefab does nothing in game. Tell them apart by the client
  log: a missing script names the script, a split bundle says nothing at all.
