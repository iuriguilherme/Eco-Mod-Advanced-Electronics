---
title: "A failed worker can still have finished its work — check the drop-box before re-running"
date: 2026-09-06
category: workflow-issues
module: AdvancedElectronics
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "A dispatched subagent returns status failed, is killed by a rate limit, or times out"
  - "About to re-dispatch a pass whose output file location was fixed in the dispatching brief"
  - "Judging whether an agent got anywhere from its reported partial output or its last narrated line"
  - "A parallel fan-out ends with some agents returning normally and some not"
  - "The workflow's own contract already names an artifact path for each agent's output"
symptoms:
  - "An agent reported as failed left a complete, full-size artifact on disk"
  - "The agent's last narrated words describe work its finished artifact shows was already done"
  - "Killed agents in the same batch differ only in whether their output file exists, and the notification does not say which"
  - "A re-run of a failed pass pays for work already completed and re-rolls corrections the first pass had verified"
root_cause: "The failure notification is a claim about the worker process, not about its output, and the orchestrator read the two as one claim. A death at the return step, after the artifact was complete on disk, is reported identically to a death before any work was done -- and the agent's own narration is not a progress indicator either, so nothing in the notification distinguishes them. Only a directory listing does."
resolution_type: workflow_improvement
tags:
  - subagent-dispatch
  - ce-compound
  - verification
  - rate-limit
  - parallel-agents
  - orchestration
  - false-failure
  - methodology
related_components:
  - docs/solutions
  - docs/plans
---

# A failed worker can still have finished its work — check the drop-box before re-running

## Context

This repository's learning-capture workflow runs on delegation. The `ce-compound` skill launches
three research subagents in parallel — a Context Analyzer, a Solution Extractor, and a Related Docs
Finder — and each of them is told to write its full output to a file in a per-run scratch directory
rather than to hand its prose back through the conversation. The skill's own research reference states
the arrangement in its opening line
(`compound-engineering/3.24.0/skills/ce-compound/references/research.md:28`):

> "Launch research subagents. Each writes its full output to a per-run scratch artifact and returns
> only the artifact path to the orchestrator."

The contract each subagent receives is spelled out a few lines further down, at
`references/research.md:49`:

> "Each subagent **writes its full structured output** to its own file under `{run_dir}/`, **confirms
> the write succeeded** (the file exists and is non-empty), and then **returns only a one-line
> confirmation containing the artifact path** — not the prose body inline."

The filenames are fixed per role, listed at `references/research.md:51-54`: `context.json` for the
Context Analyzer, `solution.md` for the Solution Extractor, `related.json` for the Related Docs
Finder, and `session-history.md` for the session-history synthesis subagent when it runs. The
subagents write nowhere else; `compound-engineering/3.24.0/skills/ce-compound/SKILL.md:50` is explicit
that "**Only the orchestrator writes product files.** Phase 1 subagents write to per-run scratch only,
and never touch `<root>/`, project instruction files, or any other tracked path."

Two consequences follow from that design, and the second one is the whole subject of this document.

The first consequence is intended: the real work of a subagent lands on disk, in a location the
orchestrator already knows, at the moment the subagent finishes writing — which is *before* it
composes and returns anything. The reference says why the pattern exists at all, at
`references/research.md:84`, describing the Solution Extractor: "This is the subagent most prone to
the issue #956 summary-collapse, so its prose must land on disk rather than only in the inline
return."

The second consequence is the one that is easy to miss: **the moment a subagent's work becomes durable
and the moment the subagent reports success are two different moments, separated by a return trip that
can fail on its own.** The artifact is written first. The report comes afterwards. Anything that kills
the worker in between destroys the report while leaving the work intact.

Per this session's account, that gap opened three times in a single run, and it opened differently
each time. Three subagents were terminated by an API rate limit — an HTTP 429. All three were surfaced
to the orchestrator as failures, with the same status word, `failed`, and no indication of how far each
had got. What the run directory held, when it was finally listed, was this:

- **One subagent died early, having written nothing.** The partial output reported for it was a single
  opening line — *"I'll investigate both docs. Let me start by reading them and checking the current
  tree."* — and no file existed for it in the run directory. This one had genuinely lost its pass. The
  orchestrator redid that pass inline, in the parent context, which is exactly what the dispatching
  contract prescribes for the case.
- **Two subagents died at the return step, with their artifacts already complete on disk.** Per this
  session's observation the two files were roughly 27 KB and 19 KB. Both of those subagents were
  reported as `failed`. Re-dispatching them would have thrown away two finished research passes and
  paid for them a second time.

The three byte sizes, the status words and the quoted notification text above are what this session
observed at the time. They are session events, not repository state: there is nothing in the tracked
tree that records them and nothing a later reader can re-derive from a checkout. They are attributed
here rather than presented as verifiable facts. What *is* verifiable in the tree is the downstream
evidence, and it is given in the **Examples** section below: the doc written from the 27 KB artifact
exists, is substantial, and landed in history.

**The reported text is not a progress indicator either, and this is the trap within the trap.** It is
tempting to read the last thing a dying worker said as a rough gauge of how far it got — an opening
sentence means it had barely started, a closing sentence means it was nearly done. That inference does
not hold. Per this session's account, one of the two subagents whose artifact was *complete* reported
as its last words:

> "I'll start by exploring the docs/solutions structure and grepping for related material."

That is the opening sentence of a run whose roughly 19 KB of structured output was already written to
disk. The other complete subagent reported *"Now I have everything verified. Writing the doc."*, which
happened to be accurate. And the subagent that had written nothing reported *"I'll investigate both
docs. Let me start by reading them and checking the current tree."* — an opening line, from a run that
really was at its beginning.

So across three cases the reported text was: an opening line with no artifact, an opening line with a
complete artifact, and a closing line with a complete artifact. Two of the three lines were openings,
and they came from opposite outcomes. The text tells you nothing about the artifact. Only the artifact
tells you about the artifact.

The resolution was already written down, in the very workflow that was running. The assembly reference
says, at `compound-engineering/3.24.0/skills/ce-compound/references/assembly.md:11`:

> "**Collect Phase 1 results from the run artifacts.** For each Phase 1 subagent, `Read` its artifact
> file under `{run_dir}/` (`context.json`, `solution.md`, `related.json`, and `session-history.md`
> when session history ran). The artifact holds the subagent's full output. **Fall back to the
> subagent's inline return only when its artifact file is absent or empty** (e.g., `{run_id}` did not
> resolve, or the subagent failed to write). The artifact is authoritative when present — this is what
> makes the workflow resilient to the issue #956 summary-collapse, where the inline return is only an
> executive summary."

Read that sentence with the three deaths in mind. It does not say "fall back to the inline return when
the subagent failed". It says fall back only when the **file** is absent or empty. The condition is a
property of the drop-box, not a property of the worker. The check that distinguishes the three cases is
one directory listing, and it costs nothing.

### What earlier sessions already did, and the gap that left (session history)

The habit this document argues for is not new here — but it was built for a different kind of
worker, and that is why this case slipped through.

Across sessions on other branches, rate-limit deaths were routine rather than exceptional, and the
recovery move was consistent: **inspect the tree before deciding whether to resume or restart**. In
the `ce-work` unit loop the orchestrator ran `git status` after every reset before touching a killed
worker's unit, narrating it explicitly — *"Checking whether U2's worker wrote anything before it
died, then resuming it"* — and applied the same check to three further units. The reasoning it
produced was exactly the triage described below: *"nothing was lost, since it died before writing"*
for one unit, against *"its partial work is intact in the tree"* for another (session history).

**`git status` cannot see a `ce-compound` research artifact.** Those subagents write to a per-run
scratch directory outside the repository by design, so a worker can finish its entire pass and leave
the tracked tree completely unchanged. The established check was the right instinct pointed at the
wrong location, which is why the same session that had the habit still nearly re-dispatched two
completed passes.

Two further findings from those sessions sharpen the practice:

- **No prior case of a worker finishing and then dying silently was found at all.** Every earlier
  death was before writing, mid-write leaving a partial change, or mid-read before producing
  findings (session history). This class is new to the store, which is a reason to write it down
  rather than a reason to doubt it.
- **Reusing an artifact is safer than resuming an agent.** A prior session that resumed a killed
  worker had to instruct it *"to distinguish evidence it witnessed before the interruption from
  evidence gathered after, rather than reconstructing a red it didn't see"* (session history) — a
  resumed worker can confabulate continuity it does not have. An artifact carries no such risk: it
  is what the worker actually produced, fixed on disk, with nothing to reconstruct.

Those sessions also settled the mechanics of the limit itself. The deaths were **wall-clock session
resets** carrying a stated reset time, not a transient error that a backoff clears, and the learned
lever was batch size: a large simultaneous fan-out produced correlated mass death every time, and
the fix was always smaller waves or serial dispatch, never a larger batch (session history).

The skill paths quoted throughout this document are given relative to the plugin's versioned root
(`compound-engineering/3.24.0/…`) rather than as absolute paths on this machine. That is deliberate:
this repository is public, and machine-local absolute paths are kept out of tracked files (auto memory
[claude], *"No machine-local paths in the repo"*). The files themselves live in the local plugin cache
under that relative path.

## Guidance

**When a background worker is reported as failed, list its artifact directory before you decide
anything at all. The failure notification is a claim about the worker's process, not about the
worker's output.**

That is the rule in one sentence. The rest of this section is how to apply it without over-applying it.

### Step 1 — separate the two questions the notification does not separate

A failure notification bundles together two questions that have different answers and different
evidence:

1. *Did the worker finish its process?* The notification answers this, and it answers it correctly. A
   worker killed by a rate limit did not finish its process.
2. *Did the worker's output reach durable storage?* The notification does not answer this at all. It
   cannot. The worker writes its artifact and only afterwards composes its return; a death between
   those two events produces a truthful "the process failed" alongside a complete, usable artifact.

The habit worth building is to notice that you have only ever been told the answer to question 1, and
that question 2 — the one that actually decides whether you re-run — is answered by looking at the
disk.

### Step 2 — list the directory, do not reason about it

Do not reason about whether the worker plausibly got far enough. Do not weigh the wording of its last
message. Do not estimate from elapsed time. List the run directory:

```bash
ls -la "$RUN_DIR"
```

For a `ce-compound` run the expected filenames are known ahead of time and are fixed per role
(`references/research.md:51-54`), so the listing is directly interpretable without opening anything:

| File present and non-empty | Which pass it belongs to |
|---|---|
| `context.json` | Context Analyzer |
| `solution.md` | Solution Extractor |
| `related.json` | Related Docs Finder |
| `session-history.md` | Session History synthesis |

A file that is present and non-zero-length is a completed pass. A file that is absent, or present at
zero bytes, is not.

### Step 3 — the two-outcome triage

Every reported failure resolves into exactly one of two outcomes, and the directory listing is what
resolves it.

**Outcome A — the artifact is present and non-empty. Use it. Do not re-dispatch.** The pass is done.
Read the file and continue assembly as though the worker had returned normally, because in every sense
that matters it did: the contract says the artifact "holds the subagent's full output"
(`references/assembly.md:11`), and the inline return was never the authoritative copy in the first
place. A `failed` status next to a complete artifact is a report about a return trip, and the return
trip is not the deliverable.

Do give the file a glance before trusting it — the point of checking is to establish that the work is
there, and a truncated file is not the work. Look for the structure the role was contracted to produce:
a `solution.md` should carry its track-appropriate section headings, a `context.json` should parse as
JSON with the frontmatter skeleton the Context Analyzer was told to emit. A file that is present but
obviously cut off mid-sentence is treated as Outcome B. This is a cheap sanity check, not a re-review;
a file whose expected sections are all present is the completed pass.

**Outcome B — the artifact is absent or empty. The pass must be redone.** This is the genuine failure,
and it is the case the contract's fallback clause was written for. Two sub-cases matter:

- If the subagent returned its full structured output inline — which the contract requires it to do
  when the write did not succeed, per `references/research.md:56`: *"Return the full output inline
  whenever the artifact write did not succeed"* — then use that inline return. The reference closes
  that paragraph by noting that the arrangement is defensive rather than mandatory: *"The artifact
  pattern is a reliability improvement, not a hard requirement; the orchestrator handles a missing
  artifact in Phase 2 by using the inline return."*
- If there is no artifact and no usable inline output, the work has to be performed again. **Where
  dispatch itself is the thing that is failing, perform the pass in the orchestrator rather than
  re-dispatching into the same failure.** Both skills carry this rule explicitly.
  `references/research.md:60` says:

  > "Classify a rejected dispatch by whether an agent launched: correct a pre-launch argument
  > rejection once, leave capacity-limited work queued, and if another launch failure survives
  > correction, run that role in the parent context with the same contract and artifact path rather
  > than dropping it."

  and `compound-engineering/3.24.0/skills/ce-compound-refresh/SKILL.md:22` states the same rule for the
  refresh workflow, adding a reporting obligation:

  > "Classify a rejected subagent dispatch by whether an agent launched: correct a pre-launch argument
  > rejection once, leave capacity-limited work queued, and if another launch failure survives
  > correction, perform that pass in the orchestrator with the same inputs and report the
  > substitution."

  Note the two details in those sentences that are easy to skim past. First, the pass is run **with
  the same contract and artifact path** — the inline re-run still writes to `{run_dir}/solution.md`,
  so the assembly step downstream does not need to know which passes were delegated and which were
  performed in the parent. Second, the substitution is **reported**, so the run's output records that
  a pass was done inline rather than by a subagent.

### Why the returned text is not evidence in either direction

It is worth stating separately, because the temptation to use it is strong and it feels like free
information.

A worker's last reported words are whatever happened to be in flight when it was killed. They are a
snapshot of narration, and narration is not correlated with completion. A worker that narrates its plan
up front and then works silently will, if killed at the return step, report an opening sentence
alongside a finished artifact — which is precisely one of the cases this session saw. A worker that
narrates continuously will report something late-sounding whether or not the artifact was written. And
a worker killed genuinely early reports an opening sentence too, indistinguishable from the first case.

The same applies to the inverse temptation: text that sounds confident and final (*"Now I have
everything verified. Writing the doc."*) is not proof the doc was written. It happened to be accurate
in one of this session's three cases. It could equally have been the last thing said by a worker killed
one instruction before its `Write` call.

Treat the notification as a signal to **go and look**, and nothing more. This is the same discipline
this store already records for build artifacts in
`docs/solutions/workflow-issues/a-timestamp-says-when-a-file-was-written-not-what-is-in-it.md`: a piece
of metadata about a process is not evidence about the content that process was supposed to produce, and
the fix in both cases is to open the artifact.

## Why This Matters

**The two errors are not symmetric, and the asymmetry runs strongly in one direction.**

Checking the directory when the artifact turns out to be missing costs one `ls`. That is the entire
downside of the discipline: a listing that told you what you already suspected.

Re-running a pass whose artifact was already complete costs considerably more than one listing:

- **It pays for the work twice.** The pass is redone from scratch — the same reading, the same
  searching, the same drafting, on the same inputs. In a workflow whose subagents produce twenty to
  thirty kilobytes of structured research each, that is a substantial and entirely avoidable spend.
- **It discards verified corrections that a second attempt may not reproduce.** This is the serious
  cost, and it is the one that is invisible at the moment you decide to re-run. A research pass is not
  a deterministic function of its prompt. When a subagent checks a claim from its own brief against the
  tree and finds the brief wrong, that correction exists in the artifact and nowhere else. Throw the
  artifact away and you are betting that a fresh run will independently decide to check the same claim,
  check it the same way, and reach the same conclusion. Per this session's account, one of the two
  recovered artifacts had done exactly this: it corrected the dispatching brief on two points of fact,
  having checked them against `git` history rather than accepting the brief's summary. Those
  corrections survived into the written doc and are visible there today — see **Examples** below.
- **Under a rate limit, the re-run is the operation most likely to fail again.** This is what makes the
  mistake compounding rather than merely wasteful. The reason the workers died was capacity. Answering
  a capacity failure by immediately issuing more work of the same shape is the move most likely to
  produce another capacity failure, which — if the same reasoning is applied to it — produces another
  re-dispatch. The dispatching contract anticipates this and says to *"leave capacity-limited work
  queued"* rather than to retry it into the wall (`references/research.md:60`). And these limits are
  wall-clock resets rather than transient errors a backoff clears (session history), so the re-run
  does not merely risk failing — it fails until the stated reset, and the work it was going to
  redo was sitting on disk the whole time.

**The failure mode is quiet, which is what makes it worth writing down.** Re-running a completed pass
does not error. It does not warn. It produces a plausible result, on schedule, that nobody has reason
to question — and the only trace of what was lost is a file in a scratch directory that was never
opened. Nothing in the run's output would say "the corrections from the first attempt are gone". That
is the signature of the failure class this store names in
`docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md`: a check that
silently produces a confident-looking outcome is worse than one that fails loudly, because there is no
moment at which anyone is prompted to look.

**And the safeguard was already written.** No new tooling, no new protocol, and no change to the
workflow was required to avoid this. The rule was in the skill being executed
(`references/assembly.md:11`), phrased in terms of the file rather than the worker, and the cost of
following it is one directory listing. The gap was between reading the notification and consulting the
contract that says what a notification is worth.

## When to Apply

Apply this practice when:

- **A background worker, subagent, or delegated task reports as failed, killed, timed out,
  rate-limited, cancelled, or interrupted** — and its role includes writing output to a known location
  before returning. The specific cause of death does not change the check; a rate limit, a timeout, a
  crashed process and a user interrupt all sit in exactly the same position relative to the artifact.
- **You are about to re-dispatch, redo, or discard the work of such a worker.** The listing goes before
  the decision, not after it. Once the re-run has started, the question of whether it was necessary
  costs the same to answer and no longer changes anything.
- **The workflow separates "write the output" from "return the confirmation."** This is the structural
  precondition, and it is precisely what `ce-compound`'s Phase 1 does: full output to
  `{run_dir}/<role-file>`, then a one-line path back (`references/research.md:49`). Any workflow with
  that shape has the same window between durable output and reported success.
- **Several workers fail together from one shared cause.** A shared cause does not imply a shared
  outcome. Per this session's account, one rate limit produced one empty-handed worker and two workers
  with complete artifacts. Check each drop-box separately; do not generalise from the first one you
  open.
- **You are reading a report about a process in order to decide something about that process's
  output.** The general form of the rule. Notifications, statuses, exit codes and last-message text
  describe execution. Files describe results. When the decision turns on results, consult files.

This does **not** apply, or applies differently, when:

- **The worker writes no artifact by design.** A read-only worker whose entire deliverable is its
  inline answer — a search that returns a list of paths, a reviewer that returns findings in its reply —
  has no drop-box to check, and its failure really does mean its output is gone. The check presupposes
  a contract that puts output on disk. Where there is no such contract, there is nothing to list, and
  the redo is unambiguous.
- **The artifact is absent or empty.** Then this document has done its job and the answer is to redo
  the pass. **This is not a reason to be suspicious of a genuine early failure.** One of this session's
  three subagents really had written nothing, and redoing its pass inline was correct — not a
  concession, not a fallback, but the contract's own prescribed handling of a launch failure that
  survives correction (`references/research.md:60`,
  `compound-engineering/3.24.0/skills/ce-compound-refresh/SKILL.md:22`). The purpose of the check is to
  *distinguish* the two cases, not to talk yourself out of either one.
- **The artifact is present but visibly truncated.** Treat it as absent. The point of the check is that
  the work is there; half of the work is not the work. Confirm the role's expected sections are
  present, as described under Outcome A above.
- **The worker's contract lets it write partial output as it goes.** Then presence of a file is no
  longer equivalent to completion of a pass, and the completeness check has to be more than "non-empty"
  — you need whatever completion marker the contract defines. `ce-compound`'s Phase 1 subagents write
  once, at the end, and confirm the write before returning (`references/research.md:49`), which is what
  makes non-empty-plus-expected-sections a sufficient test here.
- **The concern is correctness rather than existence.** A recovered artifact is still subject to every
  normal quality check the workflow applies downstream — grounding validation, the claims validator,
  overlap assessment. Recovering it says the pass happened; it does not say the pass was right. The
  two questions are separate and both still get asked.

## Examples

### The three deaths, side by side

Per this session's account, one rate limit (HTTP 429) killed three subagents in one `ce-compound` run.
All three surfaced with the status `failed`. Laid out against what the run directory actually held:

| Reported last words (per this session's observation) | Artifact on disk | Correct action |
|---|---|---|
| *"I'll investigate both docs. Let me start by reading them and checking the current tree."* | none | Redo the pass — performed inline in the orchestrator |
| *"I'll start by exploring the docs/solutions structure and grepping for related material."* | complete, ~19 KB | Use the artifact; do not re-dispatch |
| *"Now I have everything verified. Writing the doc."* | complete, ~27 KB | Use the artifact; do not re-dispatch |

Read the first column on its own and rows one and two are the same observation: an agent announcing its
plan. Read the second column and they are opposite outcomes. That is the entire lesson in two rows. The
byte sizes and the quoted text are what the session observed; they are not recoverable from the
repository.

### Before — deciding from the notification

```text
Agent "Related Docs Finder" failed: HTTP 429 (rate limit)
  last output: "I'll start by exploring the docs/solutions structure and grepping
                for related material."

→ reads as: barely started, nothing to salvage
→ decision: re-dispatch the Related Docs Finder
```

The reasoning chain is: status is `failed`, therefore the pass did not complete; the last words are an
opening sentence, therefore it had not got far; therefore re-run. Both premises are about the worker's
process, and the conclusion is about the worker's output. Nothing in the chain touches the disk.

Cost had it been executed here: one complete research pass thrown away and re-purchased, dispatched
into a rate limit that had just rejected three requests.

### After — deciding from the directory

```bash
ls -la "$RUN_DIR"
```

```text
context.json        14K   # Context Analyzer      -> complete, use it
solution.md         27K   # Solution Extractor    -> complete, use it
related.json        19K   # Related Docs Finder   -> complete, use it
                          # (no session-history.md)
```

Three `failed` notifications; three files, and the two that mattered were whole. The assembly step then
proceeds exactly as written, per `references/assembly.md:11`: read each artifact, and fall back to an
inline return *only* for a file that is absent or empty.

### The tree evidence: the recovered work was real work

The incidents above cannot be verified from the repository, but their product can, and it is
substantial.

`docs/solutions/workflow-issues/a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md` is
the doc written from the 27 KB artifact. It is **454 lines** on disk, and it landed as a single commit
adding 454 lines — `b6c6983`, dated 2026-09-05, subject *"docs(solutions): the link is checked, the
sentence around it is not"*. That commit is on `feat/tech-tree-icons`, which is unmerged and has no
pull request, so `b6c6983` is a branch-local short SHA: it is the only way to name the commit today, it
is cited the way this store's other docs cite branch-local SHAs, and it is not a durable reference. If
the branch is squashed or rebased, look the commit up by its subject line. The doc itself opens by
saying the same thing about its own citations, at
`a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md:39-43`:

> "Every commit named below is on `feat/tech-tree-icons`, which is unmerged and has no PR. The short
> SHAs are therefore branch-local: they are the only way to name this work today, they are cited here
> the way two other docs in this store cite branch-local SHAs, and they are not durable references."

That is the doc a re-dispatch would have discarded.

### The corrections that would have been re-rolled

The concrete reason the loss would have mattered beyond the token spend is visible in that same doc's
third instance, at
`a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md:121-157`. The subagent had been
handed a claim in its brief and, instead of writing it up, checked it against history. The doc opens
the section by saying so, at `:123-124`:

> "The third instance is real but its shape is not quite what it was first reported to be, and the tree
> is what settles it."

It then sets out what it found, at `:131-139`, naming the two commits it inspected — `717899f`, which
created one of the two docs, and `6304e2a`, which *"added **both directions in a single commit**"* —
and concludes at `:141` that *"no committed tree ever held the one-way state."* The brief's account of
the incident was wrong on that point, and the artifact carried the correction rather than the brief's
version.

The second correction lands in the same passage. Having disposed of the reported shape, the subagent
found the same class of error one artifact over, in a commit message, and kept the instance on that
revised basis — `:141-152` quotes `6304e2a`'s own commit body (*"Also adds the reciprocal link on the
companion, which had only pointed one way."*), observes at `:148-150` that at that commit's parent the
pair pointed **neither** way, and notes that the end state is nonetheless correct today, citing the two
reciprocal links at `an-audience-model-sets-the-sources-not-just-the-tone.md:302` and
`a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md:148`. The doc then draws the
generalisation at `:154-157`: commit messages carry description claims too, and nothing validates them
at all.

None of that was in the brief. All of it came from the subagent opening `git` and looking. A re-run
starts from the brief again — the same wrong claim, with no memory that it was ever checked — and
whether it re-derives the correction is a matter of chance. That is the asymmetry in its concrete form:
the re-run costs the work twice and gambles the part of the work that was most valuable, all to avoid a
directory listing.
