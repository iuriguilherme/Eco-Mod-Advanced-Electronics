---
title: "[Serialized] needs somewhere to write the value back, so a computed property is never a candidate"
date: 2026-08-07
category: conventions
module: EcoServerMod
problem_type: convention
component: serialization
severity: high
applies_when:
  - "Adding [Serialized] to a member so a value survives save and load"
  - "Deciding which booleans an animator or client view should read"
  - "A mod builds cleanly but never appears in the server's load lines"
  - "Reaching for an existing property because its name matches what you need"
tags: [eco-modding, serialization, worldobject-component, animation, silent-failure, persistence]
related_components: [EcoServerMod/AdvancedElectronics]
---

# [Serialized] needs somewhere to write the value back, so a computed property is never a candidate

## Context

Wiring a drone's animator meant finding which booleans the client should receive. The server already had
promising ones — `IsWorking`, `ShouldSample`, `Operating` — with exactly the right names for what the
animation needed to know.

`[Serialized]` looked like the attribute that exposes a member to the engine, so it went onto all three.
The build stayed green. The mod then stopped loading, and the server log said nothing at all: no
exception, no stack trace, not even the `Loading AdvancedElectronics...` line that a healthy start
prints. There was nothing to search for.

## Guidance

**`[Serialized]` means "save this and load it back", and loading it back is a write.** A member that
computes its value on every read has nothing to write into. Every valid use in this mod has a setter:

```csharp
[Serialized] public ThreadSafeList<SurveyAreaEntry> SurveyAreas { get; private set; } = new();
[Serialized] public int AssignedSurveyAreaId { get; private set; }
[Serialized] public ThreadSafeList<string> MaterialFilter { get; set; } = new();
```

The three that broke the load had none — they are expression-bodied and recomputed each time:

```csharp
public bool IsWorking =>                                                    // DroneLifecycle.cs:99
    this.stateMachine.Status == DroneStatus.Surveying || ...;
public bool ShouldSample => this.stateMachine.ShouldSample;                 // DroneLifecycle.cs:109
public bool Operating => this.Parent is DroneDockObject dock               // SurveyComponent.cs:78
                         && dock.DroneIsWorking;
```

**Separate saved state from live signal — they pull in opposite directions.** Both are "a value the
engine knows about", which is why one attribute looks right for both:

| | Saved state | Live signal |
|---|---|---|
| Question it answers | what was true when we last saved | what is true right now |
| Needs | a setter, so load can restore it | nothing — it is derived |
| Carried by | `[Serialized]` on a settable member | `SetAnimatedState(name, value)` per change |
| Wrong to persist because | — | a save file could disagree with the code that computes it |

**The test:** if the value can be recomputed from something you already have, it is a signal. Persisting
it creates a second source of truth that will eventually disagree with the first.

**Grep for the load line before hunting for an exception.** A mod that fails during type registration
never reaches the point where errors are attributed to it, so its absence is the only evidence:

```bash
grep "Loading <YourMod>" <server>/Logs/<newest>.log
```

A healthy start prints one line per assembly. No line means the mod never loaded — a different failure
from "loaded and threw", and the reason no exception is worth searching for.

## Why This Matters

The failure gives you nothing to work with. The C# compiles, so the build gate passes. The server starts,
so the process looks healthy. The mod is simply absent, and the log's silence reads as "nothing went
wrong" rather than "the thing you are looking for never ran". Time goes into re-reading the code that was
changed rather than into noticing what is missing from the log.

The naming coincidence is what makes it inviting. An animator that needs to know "is this thing working"
finds `IsWorking` already declared, already public, already a bool. Nothing at the call site distinguishes
a property that answers a question from a field that remembers an answer, and the attribute that would
have flagged the difference is the one being added.

## When to Apply

- Before adding `[Serialized]` to any member — confirm it has a setter, not just a getter.
- When choosing which values a client view or animator will read. Those are signals; push them, do not
  persist them.
- When a value is "a fact about what this object *is*" rather than what happened to it. A drone's tool
  type never changes, so there is nothing to remember and persisting it only invites a save file to
  contradict the class.
- Whenever a mod stops loading with a clean build and a clean log — check for the load line first.

## Examples

The same booleans, on both sides of the line:

```csharp
// Signal — recomputed, pushed to the client on change, never stored.
public bool IsWorking => this.stateMachine.Status == DroneStatus.Surveying;
// ... elsewhere, on transition:
this.Parent.SetAnimatedState("IsWorking", isWorking);

// Saved state — has a setter, so load has somewhere to put the value.
[Serialized] public int AssignedSurveyAreaId { get; private set; }
```

The diagnostic that turns silence into a fact:

```text
healthy:   [Info] [Eco] Loading AdvancedElectronics...
           [Info] [Eco] Loading AdvancedElectronics.Navigation...

broken:    (no such line anywhere in the log)
```

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the other Eco attribute
  rule whose omission fails silently: a `WorldObjectComponent` missing `[Serialized]` and `[NoIcon]`
  renders an empty window rather than reporting anything. Same attribute, opposite error — there the fix
  is adding it, here it is not adding it, and neither direction produces a usable message.
- `docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md` —
  when derived data *should* be persisted. The distinction is a settable snapshot field written
  deliberately, never the computed property itself.
