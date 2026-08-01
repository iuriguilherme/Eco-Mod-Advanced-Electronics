---
title: "The control you are testing is not a readout of the thing you are testing"
date: 2026-07-31
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "Probing whether a UI control's input actually reaches the server"
  - "A widget visibly responds to a click but nothing downstream changes"
  - "Designing a probe where several controls read or write the same field"
  - "Reading a rendered value as evidence of stored state in any client-server UI"
tags: [methodology, probes, false-positive, optimistic-ui, eco-modding, client-server, instrumentation]
related_components: [EcoServerMod/AdvancedElectronics]
---

# The control you are testing is not a readout of the thing you are testing

## Context

One question — *does this checkbox's setter reach the server?* — took **five probe rounds and five
server restarts** to answer. The question was well posed, the code was small, and every round
produced a clear-looking observation. Four of those observations were worthless.

The reason is one sentence: **Eco's client draws a checkbox tick when you click it, before and
independently of the server agreeing.** So "the box ticked" was consistent with the write landing,
and equally consistent with it vanishing. Every round that read the tick was reading the client's
intent, not the server's state.

The answer, once measured properly: `[SyncToView, Autogen, AutoRPC]` renders the control, displays
derived state, refreshes live — and silently drops every click. No exception, no log line, and the
tick appears anyway.

## Guidance

**Ask what the widget is a picture of.** In a client-server UI a control is the *client's* model,
maintained locally for responsiveness. It is downstream of user input and upstream of nothing. It
answers "did the player click?", never "did the write land?". Before trusting any rendered value as
evidence, name the process that produced the pixels; if that process is the thing under test, or
runs on the side of the wire you are not testing, it is not evidence.

The tell is available even inside one screenshot. One round drew a box **unticked** while the server
held `true` — the two disagreed in both directions across the session, which is only possible
because they were never the same fact.

**Read the far side through a channel already proven to work.** The instrument that finally answered
the question was a `StringDisplay` line printing server-side values, pushed by the dock's existing
one-second tick:

```csharp
public void RefreshMirror()
{
    this.P_ServerMirror = $"server state -- calls A:{this.p_writeA} B:{this.p_writeB} C:{this.p_writeC}";
    this.Changed(nameof(this.P_ServerMirror));
}
```

Both halves were already independently verified — `StringDisplay` refreshes, and the dock's tick
fires — so the mirror introduced no new unknown. Building an instrument out of unverified parts just
moves the question.

**Count calls; do not read values.** A boolean readout cannot distinguish "never called" from
"called twice" from "called and reverted". A monotonic counter survives all three, and the difference
matters: two of these rounds involved a value that the client changed and the server did not.

**Do not put a side effect in your baseline.** Round three had the right idea — server-side counters
— and broke itself anyway. To have something to count, the control member was given a *computed*
getter, which no longer matched the auto-property shape whose behaviour was being used as the
reference. The baseline and the candidate then differed in two ways, so its failure proved nothing.
A baseline earns its name by differing from the candidate in exactly one thing.

**Give every probe member its own state.** Rounds one and two had several controls deriving from one
field, and a button that could also write that field. A ticked box therefore had at least three
possible causes and no observation could separate them. Worse, the *read* path working masked the
*write* path failing: the boxes updated correctly whenever anything moved the shared field, which
looked like success.

Shared state in a probe is the same defect as a shared fixture in a test suite, and it is easy to
introduce precisely when the members are meant to model something that really is shared.

**Enumerate silent failure as a possible outcome before you start.** This probe was designed around
a loud failure — an unreachable setter disconnects every player with `Missing RPC call Set<Prop>` —
and that framing quietly excluded the middle. The actual behaviour was neither success nor crash:
the click was accepted by the client, discarded, and nothing anywhere recorded it. If the outcome
table has only "works" and "crashes", the probe cannot report the third thing, and will report one
of the two instead.

## Why This Matters

This is the sibling of `validate-the-instrument-before-the-hypothesis.md`, with a different and
nastier mechanism. There the instrument was accidentally broken. **Here the instrument worked exactly
as designed and was still useless** — optimistic rendering is a deliberate feature, correct for its
purpose, and simply not a measurement.

That makes it invisible to the usual check. Asking "is my tooling working?" returns yes. The right
question is narrower: *does this reading come from the side of the system I am making a claim about?*

The cost compounds the way it always does when an instrument agrees with you. Each round produced a
confident write-up; two of them contained conclusions later shown to be wrong in the opposite
direction — one round concluded a label attribute blanked a row (a cropped screenshot), another
concluded a setter never fired when its counters had been incrementing all along and only the
readout was stale. Rounds are cheap in tokens and expensive in the maintainer's restarts, which is
the currency that actually ran out here.

## When to Apply

- Before treating any rendered control state as evidence about stored state.
- When a click produces a visible response but no downstream effect — suspect the render, not the logic.
- When designing a probe: check that each member has private state and that the outcome table has a
  row for "silently ignored".
- When a probe's baseline needs modification to be observable — that modification is a second variable.
- When a result contradicts an earlier one from the same apparatus; the apparatus is the common term.

## Examples

Two probe members that cannot answer the question, because both read one field that a third control
also writes:

```csharp
// WRONG -- a tick on either box could come from the other box, or from the button.
int source;
public bool BoxA { get => this.source > 0; set => this.SetSource(value ? 1 : 0); }
public bool BoxB { get => this.source > 0; set => this.SetSource(value ? 1 : 0); }
public void Button(Player p) => this.SetSource(this.source + 1);
```

The same members with private state and call counting, so each observation has one cause:

```csharp
// RIGHT -- A's counter moves only when A's setter runs.
int callsA, callsB;
public bool BoxA { get => this.callsA % 2 == 1; set { this.callsA++; this.Push(); } }
public bool BoxB { get => this.callsB % 2 == 1; set { this.callsB++; this.Push(); } }
```

And the reading that settled it — server-side, and unambiguous, where five rounds of tick marks were
not:

```text
server state -- StoredNoTitle: False | calls A:1 B:0 C:0 | source:0
```

`A` fired, `B` and `C` did not, while all three were drawn ticked on screen.

## Related

- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — the same family:
  conclusions reached by reading an instrument rather than reasoning badly. That one is about an
  instrument that was broken; this one about an instrument that was fine and measured the wrong side.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — a check that
  cannot fail. An optimistic control is a check that cannot report failure.
- `docs/solutions/runtime-errors/autogen-template-binding-contract.md` — what this probe was
  measuring, and where its findings landed.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why probe design matters so much
  here: the unit of cost is a human restart, so a round that answers nothing is expensive.
