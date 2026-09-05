---
title: "A timestamp says when a file was written, never what is in it — read the artifact"
date: 2026-09-05
category: workflow-issues
module: AdvancedElectronics
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "A claim of the form \"X did or did not reach the built artifact\" is about to drive an Editor session, a redeploy, a restart, or a request for someone else's time"
  - "The evidence offered for that claim is a modification time, a directory listing, a file size, or a commit date"
  - "The artifact is a container format - an asset bundle, a zip, a jar, a compiled assembly, a packed sprite atlas"
  - "The question is whether a name, symbol, string, or asset is present inside the built output"
  - "A question has been parked on \"we would need library X, which is not installed\""
tags: [asset-bundle, verification, unityfs, lz4, tooling, live-testing, eco-modding, workflow]
related_components: [scripts, AssetBundles, Assets/Art/AdvancedElectronics]
---

# A timestamp says when a file was written, never what is in it — read the artifact

## Context

This mod ships as two halves, and they are built in two completely different ways. The server half
lives in `EcoServerMod/` and is built from the command line with `dotnet build`, so anyone — a
person or an agent — can rebuild it, inspect it, and rebuild it again as many times as they like.
The client half is the Unity project, and `CLAUDE.md:12` states the constraint plainly: *"No CLI
build, lint, or test — all building happens inside the Unity Editor."* The client half's output is
a Unity asset bundle, a single `.unity3d` file, produced by hand through the **Eco Tools > Mod Kit**
menu described at `CLAUDE.md:73-75`. That bundle is then copied into the live server's `Mods/`
directory, and the server has to be restarted before the client can see it.

Every step of that client-half loop is expensive in a way the server half is not. An Editor session
has to be opened and driven by hand. The auto memory entry *"Unity MCP approval is exhaustible"*
records that Editor access here is a finite grant rather than a standing connection, so Editor work
has to be batched into as few commands as possible (auto memory [claude]). The restart at the end of
the loop is performed by the repository's owner, not by the agent, and the auto memory entry *"No
restart-loop testing"* records that using the owner as an iteration loop is forbidden outright (auto
memory [claude]). The same discipline is written up at length in
`docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`. So a wrongly-asserted "the bundle
needs rebuilding" does not cost a build; it costs an Editor session **and** a human-driven server
restart, both of which are scarce and neither of which is refundable.

That is exactly what nearly happened. A session handoff document,
`docs/protocols/2026-08-30-tech-tree-icons-handoff.md`, listed as the blocking first item of the
remaining work a rebuild-and-redeploy of the asset bundle. Per this session's account, the claim
read approximately:

> "Rebuild and redeploy the bundle — blocking, mechanical. `422e4d6` removed four scene objects; the
> deployed bundle predates it. Until it is rebuilt, the retirement has not reached the client."

`422e4d6` is a commit on the branch `feat/tech-tree-icons` and is not on `main`; there is no pull
request for it, so it is quoted here as the handoff wrote it rather than as a durable reference — a
branch-local SHA can be rewritten by a rebase or squash merge. The handoff's state table carried a
bundle modification time older than the scene change it was being compared against. The entire claim rested on one operation: **comparing file modification
timestamps**. The bundle's `mtime` looked older than the commit that changed the scene, therefore
the bundle must not contain the change.

It was wrong. The rebuild had already been done. The document now records its own correction at
`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:56-58`:

> "**The rebuild is already done.** The earlier entry here said the bundle predated `422e4d6` and
> had to be rebuilt; that was wrong, and it was wrong because it compared timestamps. Reading the
> bundle itself settles it"

and the state table at `:12-13` now reads *"bundle 08-30 16:25, md5-identical to the repo's"* and
*"Pending deploy: None. Verified 2026-08-31 by reading the bundle's own contents, not its
timestamp."*

The gap this doc exists to close is the one between those two sentences. A modification time is
metadata that the filesystem happens to keep about a write event. It answers *when was this file
last written*. It does not answer, and cannot be made to answer, *what is inside this file*. A copy,
a touch, a checkout, a restore from backup, a build that produced byte-identical output, and a build
that produced completely different output are all indistinguishable through an `mtime`. Where the
only other way to answer the "what is inside" question is an expensive Editor trip and a human-driven
restart, the temptation to substitute the cheap metadata question for the expensive content question
is strong — and the substitution is invalid every single time.

## Guidance

**A build artifact is a file. Its contents are the ground truth about what it holds. When the
question is "did this change reach the artifact?", open the artifact and look.**

That is the whole rule. What follows is how it was made practical here, and the two implementation
traps that were paid for on the way.

### Write the reader, once

For this repository the reader is `scripts/read-mod-bundle.py`. Its own docstring at
`scripts/read-mod-bundle.py:2-13` states both what it does and what it deliberately does not do:

> "Report what a Unity asset bundle actually contains, without opening Unity. … Answers 'did this
> name reach the bundle?' for our own builds, and 'what does this mod ship?' for the reference mods
> under `.references/Mods/`. … Reads UnityFS containers with a pure-Python LZ4 block decompressor,
> so it needs no packages beyond the standard library. It deliberately stops short of parsing the
> SerializedFile type tree: the node table plus the readable strings answer the questions we
> actually ask, and a full parser would be a dependency and a maintenance burden for no extra
> answer."

Two design choices in that paragraph are worth stating separately, because they are what make the
tool something you actually reach for rather than something you keep meaning to install.

**No third-party packages.** The LZ4 block decompressor is written out in pure Python at
`scripts/read-mod-bundle.py:22-68`. That means the tool runs on a bare Python install with nothing
but the standard library — `re`, `struct`, `sys`, `pathlib` (`:16-19`). Before this existed, the
open question about the reference mods had been parked for weeks behind the note "needs an LZ4
reader (`lz4` or `UnityPy`, neither currently installed)". A dependency that is not installed is a
question that does not get answered; a fifty-line function that is checked into `scripts/` is a
question that gets answered whenever anyone asks it.

**Stop short of a full parser.** The tool does not decode Unity's SerializedFile type tree. It
reports two things: the container's **node table** — the named entries inside the bundle, printed at
`:208-209` as `node <name> (<size> bytes @ <offset>)` — and the **readable strings** recovered from
the decompressed payload by the regex at `:174`, `re.compile(rb"[ -~]{4,}")`, which is every run of
four or more printable ASCII characters. That is enough to answer a name-presence question, which
is the question that actually gets asked. Building the full parser would have added a maintenance
burden and answered nothing extra. Match the reader to the question, not to the format.

### Run it

```bash
scripts/read-mod-bundle.py AssetBundles/AdvancedElectronics.unity3d
scripts/read-mod-bundle.py AssetBundles/AdvancedElectronics.unity3d --strings /tmp/bundle-strings
```

The usage line is at `scripts/read-mod-bundle.py:4`,
`scripts/read-mod-bundle.py <bundle.unity3d> [...] [--strings <dir>]`. It accepts more than one
bundle in a single invocation and prints a `===== <name> =====` banner per file (`:199`). Without
`--strings` it prints only the count of unique strings (`:218-219`); with `--strings <dir>` it
writes each bundle's unique strings, in first-seen order, to `<dir>/<stem>.strings.txt`
(`:220-223`), which is the form you want when the next step is `grep`. It exits non-zero if any
bundle failed to parse (`:225`), and a failure on one bundle does not stop the others (`:202-205`).

Then the actual check is a grep against the strings file: is the name there, or is it not?

### Trap 1 — the UnityFS header has two independent alignment steps

Both are in `read_bundle`, and each is a single line that is easy to omit.

The first is the header alignment for container version 7 and above, at
`scripts/read-mod-bundle.py:119-120`:

```python
    if version >= 7:
        r.p = (r.p + 15) & ~15
```

After the flags field is read (`:117`), the read position is rounded **up to the next 16-byte
boundary**. That is the expression `(p + 15) & ~15` — add fifteen, then clear the low four bits.

The second is the block-data alignment, at `scripts/read-mod-bundle.py:129-130`:

```python
    if flags & 0x200:                         # blockInfoNeedPaddingAtStart
        data_start = (data_start + 15) & ~15
```

When the container flag `0x200` (`blockInfoNeedPaddingAtStart`) is set, the **start of the block
data** is rounded up to a 16-byte boundary as well — a second, separate alignment, applied to a
different offset than the first.

The reason this trap is worth a section of its own is the *shape* of the failure it produces when
the second alignment is missing. It does not fail where the mistake is. The block-info directory is
located and decompressed perfectly well, because that read does not depend on `data_start` at all
(`:122-138`). The node table parses. The block list parses. Everything looks like a working parser.
The failure arrives later, inside the first data block, as a misaligned byte stream fed to the LZ4
decompressor — and per this session's account it surfaced as the `ValueError` raised at
`scripts/read-mod-bundle.py:62`:

```python
        start = len(out) - offset
        if start < 0:
            raise ValueError("match before start of output")
```

That message points at the decompressor. The decompressor is correct. The bug is an offset computed
sixty lines earlier. This is the same category of failure this store already names in
`docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md`: when a new
instrument reports a problem, the instrument itself is a hypothesis under test, and an error message
tells you where the symptom surfaced rather than where the cause lives.

### Trap 2 — compression is flagged in two independent places, and both must be honoured

A UnityFS bundle records compression **twice**, for two different pieces of itself, and honouring
one does not honour the other.

The **container flags** govern the block-info directory — the small structure holding the block list
and the node table. `scripts/read-mod-bundle.py:132-138`:

```python
    compression = flags & 0x3F
    if compression == 0:
        info = info_raw
    elif compression in (2, 3):
        info = lz4_decompress(info_raw, uncompressed_info)
    else:
        raise ValueError(f"unsupported blocksInfo compression {compression}")
```

The **per-block flags** govern the payload. Each entry in the block list carries its own flags
(`:143-144`, a triple of uncompressed size, compressed size, block flags), and each block is
decompressed according to *its own* value at `scripts/read-mod-bundle.py:155-164`:

```python
    for uncompressed_size, compressed_size, block_flags in blocks:
        chunk = raw[p:p + compressed_size]
        p += compressed_size
        c = block_flags & 0x3F
```

In both places the low six bits (`& 0x3F`) select the method, `0` means stored uncompressed, and
`2` and `3` are the two LZ4 variants. A reader that reads the container flags and then assumes the
data blocks use the same method will work on bundles where the two happen to agree and produce
garbage on bundles where they do not — which is, again, a failure that looks like a decompressor bug
rather than like a flags bug.

### The rest of the format, for whoever extends the reader

The header parse is at `scripts/read-mod-bundle.py:107-117`: a NUL-terminated signature that must be
the literal `"UnityFS"` (`:107-109`), then a version `u32`, the Unity version and revision strings,
a total-size `i64`, the compressed and uncompressed sizes of the block-info directory, and the
container flags. Every multi-byte integer in the container is **big-endian** — see the `>I`, `>H`
and `>q` format strings in the `Reader` helper at `:82-95`. Container flag `0x80` means the
block-info directory is stored at the *end* of the file rather than inline, handled at `:122-127`.
Inside the decompressed directory, the first 16 bytes are a hash and are skipped (`:141`), then a
count-prefixed block list (`:143-144`), then a count-prefixed node table of
`(offset, size, flags, name)` (`:146-151`).

### Note the drift in the scripts inventory

`CLAUDE.md:49` lists the contents of `scripts/` as `gather-eco-refs.sh`, `package-release.sh`,
`validate-name-match.sh` and `deploy-usercode-overrides.sh`. The directory on disk currently also
holds `scripts/read-mod-bundle.py` and `scripts/validate-icon-binding.sh`, neither of which appears
in that list. `scripts/read-mod-bundle.py` currently exists only on the branch
`feat/tech-tree-icons` and is **not** present on `main` — `git cat-file -e main:scripts/read-mod-bundle.py`
reports *"exists on disk, but not in 'main'"*. No pull request exists for it yet, so there is no PR
number to cite; the state to record is simply "branch-only, not merged". Whoever merges that branch
should add both scripts to the `CLAUDE.md` inventory, because a tool nobody knows about is a tool
nobody reaches for, and the whole value of this one is that it is reached for reflexively.

## Why This Matters

**A timestamp comparison is not a weak form of evidence about file contents. It is not evidence
about file contents at all.** The two questions are unrelated. `mtime` records the moment of the
last write; it says nothing about what that write put in the file. Any of the following will produce
a timestamp that misleads: a copy that preserves or resets times, a `git checkout` that rewrites a
working-tree file, a build that ran and produced byte-identical output, a build that ran from stale
inputs, a restore from a backup, a file touched by an unrelated tool, or — as here — simply a
comparison made against the wrong reference point. In the other direction, a *newer* timestamp is
equally uninformative: it proves a write happened, not that the write contained the change you
wanted. This repository already learned that second half the hard way, and it is written up at
`docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md:54-55`:
*"A fresh timestamp only proves a copy happened, not that it copied what you think."* This doc is
the same lesson approached from the opposite direction: there, a fresh timestamp wrongly implied
presence; here, a stale timestamp wrongly implied absence. The common cause is that the timestamp
was consulted at all.

**The cost of being wrong is asymmetric and it lands on someone else.** Had the handoff's claim been
acted on, the sequence would have been: open a Unity Editor session, spend part of a finite approval
grant, rebuild the bundle, copy it into the server's `Mods/` directory, and ask the repository's
owner to restart the live server — to redo work that was already complete. The owner has explicitly
forbidden being used as an iteration loop (auto memory [claude], *"No restart-loop testing"*), and
`docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md` records why their
attention is the scarcest resource in this project's loop. Reading the artifact costs one command
and a few seconds. The asymmetry is not close.

**A wrong claim in a handoff outlives the session that wrote it.** The handoff document exists
precisely so that a cold reader can act without re-deriving anything — its own opening says it is
*"Written 2026-08-30 for whoever picks this up next, cold"*
(`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:3`). That is exactly what makes a wrong entry
in it dangerous: it arrives pre-trusted, listed as *blocking* and *mechanical*, which is the
labelling most likely to get an item executed without re-examination.
`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` covers the general
shape; this is one instance of it, caught before the cost was paid.

**The tool keeps paying after the question that prompted it.** This is the part that turns a
one-off check into a practice worth writing down. The same reader immediately settled an unrelated
question that had been blocked for weeks. The question was whether four public reference mods
redistribute the base game's icon art — a licensing precedent question, not a technical one, and one
that could not be answered without decompressing their bundles. It had been parked behind the
missing LZ4 dependency. `docs/protocols/2026-08-31-slg-icon-licensing-ask.md:58-80` records the
answer: four mods — AnimalHusbandryReloaded, ArcaneKnowledge, IntelligenceSkillMod and Mixology —
were decompressed and inspected with `scripts/read-mod-bundle.py`, and the finding was that all four
ship their own art under their own class names, **none** ships an asset named after a base-game one,
and none uses the game's `_FG` foreground-sprite convention. Those bundles are on disk under
`.references/Mods/`, which is **git-ignored** (`.gitignore:119`) — the files are present locally and
will not be found by any search of the tracked tree (`ArcaneKnowledge/ArcaneKnowledgeMod.unity3d`,
`AnimalHusbandry/Assets/AnimalHusbandryReloaded.unity3d`, `Mixology/Unity/MixologyMod.unity3d`,
`IntelligenceSkillMod/IntelligenceSkillMod.unity3d`, among others).

That document is also careful about the limit of what its own instrument proves, and the care is
worth copying. `docs/protocols/2026-08-31-slg-icon-licensing-ask.md:76-79` states: *"The check is by
name only. A mod could still have traced or copied vanilla art and shipped it under its own
filename; comparing pixels would need a Texture2D decoder the reader deliberately does not have. The
claim here is narrow: none of the four ships an asset named after a vanilla one."* A reader that
stops short of a full parser answers a narrower question than a full parser would, and the honest
move is to say which question it answered rather than to let the narrow answer stand in for the
broad one.

## When to Apply

Apply this practice when:

- A claim of the form "X did/did not reach the built artifact" is about to drive an expensive
  action — an Editor session, a redeploy, a restart, a request for someone else's time.
- The evidence offered for that claim is a modification time, a directory listing, a file size, or
  a commit date. All four are metadata about writes, none of them is content.
- The artifact is a container format — an asset bundle, a zip, a jar, a compiled assembly, a
  packed sprite atlas. A container's contents are readable by definition; that is what makes it a
  container. Reading it is a programming task, not an impossibility.
- The question is a **presence** question: does this name, symbol, string, or asset exist inside the
  built output. Presence questions are answerable by a partial reader and almost never need a full
  parser.
- A question has been parked on "we would need library X, which is not installed". Weigh writing the
  fifty lines that answer your specific question against the dependency that would answer every
  possible question. Here the fifty lines won, and then answered a second question for free.
- You are writing a handoff, plan, or protocol entry that asserts the state of a build artifact.
  Say how you established it. `docs/protocols/2026-08-30-tech-tree-icons-handoff.md:13` now reads
  *"Verified 2026-08-31 by reading the bundle's own contents, not its timestamp"* — the method is
  part of the claim, and a claim without a method is not checkable by the next reader.

This does **not** apply, or applies differently, when:

- **Trackedness, not existence, is the question.** If you need to know whether a file is committed,
  `git ls-files` is the right instrument and the file's contents are irrelevant. The distinction is
  drawn at length in
  `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md`: index for
  "is it tracked", disk for "does it exist".
- **Identity, not content, is the question.** Two files being the same file is answered by a hash,
  not by a reader. The handoff's *"md5-identical to the repo's"* claim
  (`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:12`, recorded per this session's
  verification) is the right instrument for the right question — a hash proves the deployed copy and
  the repo copy are the same bytes, which the reader alone would not establish.
- **The question is about runtime behaviour, not content.** Reading a bundle proves a name is
  registered in it; it does not prove the client draws the right sprite. That still needs the
  restart and the owner's eyes. `docs/protocols/2026-08-30-tech-tree-icons-handoff.md:54-70` keeps
  precisely this separation: the reader closed the rebuild question, and protocol rows T12 and T13
  remain open because they are behaviour questions that only the owner can answer.
- **The artifact is genuinely opaque** — encrypted, or in a format whose partial parse would be more
  work than the question is worth. Then say so explicitly and pick a different form of evidence;
  do not fall back to the timestamp and present it as though it answered the question.

## Examples

### Before — a timestamp comparison, stated as a settled fact

The handoff's blocking first item, per this session's account of the pre-correction text:

```text
### 1. Rebuild and redeploy the bundle -- blocking, mechanical

`422e4d6` removed four scene objects; the deployed bundle predates it. Until it is
rebuilt, the retirement has not reached the client.
```

with a supporting state-table row of the form `| Deployed | ... bundle 08-29 21:27 |`, an
`mtime` older than the scene change it was implicitly being compared against.

Reasoning chain: bundle `mtime` < scene-change date, therefore the bundle predates the change,
therefore the change is not in the bundle, therefore rebuild. Every arrow after the first is
invalid, because the first one measures a write event and the rest are about content.

Cost had it been executed: one Unity Editor session out of a finite grant, one hand-copied deploy,
and one live-server restart performed by the repository's owner — to reproduce a bundle that already
existed and was already correct.

### After — read the bundle, then grep it

```bash
scripts/read-mod-bundle.py AssetBundles/AdvancedElectronics.unity3d --strings /tmp/bundle-strings

# the four retired names -- expect no output from any of them
grep -E 'AdvancedElectronicsSkill$|AdvancedElectronicsSkillBook|AdvancedElectronicsSkillScroll|AdvancedElectronicsUpgradeItem' \
  /tmp/bundle-strings/AdvancedElectronics.strings.txt

# the seven live names -- expect a hit for each
grep -E 'SurveyDroneItem|MiningDroneItem|HarvestDroneItem|DroneDockItem|BatteryItem' \
  /tmp/bundle-strings/AdvancedElectronics.strings.txt
grep -E 'EngineeringResearchPaperPostModernItem|AdvancedElectronicsAssemblyItem' \
  /tmp/bundle-strings/AdvancedElectronics.strings.txt
```

The result, as recorded at `docs/protocols/2026-08-30-tech-tree-icons-handoff.md:64-66`:

> "The four retired names — `AdvancedElectronicsSkill`, `...SkillBook`, `...SkillScroll`,
> `AdvancedElectronicsUpgradeItem` — appear **nowhere** in the built bundle, while the seven live
> ones do. The deployed copy is md5-identical to the repo's. The retirement reached the client."

The seven live names, enumerated so the count is checkable rather than asserted, are
`SurveyDroneItem`, `MiningDroneItem`, `HarvestDroneItem`, `DroneDockItem`, `BatteryItem`,
`EngineeringResearchPaperPostModernItem` and `AdvancedElectronicsAssemblyItem`.

The blocking item was deleted from the handoff, the state table row became *"Pending deploy: None"*
(`:13`), and the remaining work reduced to the two protocol rows that genuinely need the owner.

### The second payoff — the same tool, an unrelated question, no new work

```bash
scripts/read-mod-bundle.py \
  ".references/Mods/AnimalHusbandry/Assets/AnimalHusbandryReloaded.unity3d" \
  ".references/Mods/ArcaneKnowledge/ArcaneKnowledgeMod.unity3d" \
  ".references/Mods/IntelligenceSkillMod/IntelligenceSkillMod.unity3d" \
  ".references/Mods/Mixology/Unity/MixologyMod.unity3d" \
  --strings /tmp/refmod-strings
```

Passing several bundles in one invocation is supported directly (`scripts/read-mod-bundle.py:197`),
and one bundle failing to parse does not abort the others (`:202-205`). The findings are tabulated
at `docs/protocols/2026-08-31-slg-icon-licensing-ask.md:64-70`: no icon-related code in any of the
four, objects named after their own classes in all four, **no** object named after a vanilla asset
in any of them, and **zero** uses of the `_FG` foreground convention across the whole set.

The practical consequence was to stop a document from citing a precedent that does not exist. The
licensing ask now says so in as many words at `:72-74`: *"No precedent was found for redistributing
vanilla art, which means there is nothing to cite in the ask — and that is worth knowing before
citing something that turns out not to exist."*

### The trap, as it presents itself

What a missing second alignment looks like from the outside — a parser that appears to work right up
until it does not, failing in the component that is correct:

```text
$ scripts/read-mod-bundle.py AssetBundles/AdvancedElectronics.unity3d
===== AdvancedElectronics.unity3d =====
  FAILED: match before start of output
```

Read literally, that message accuses the LZ4 decompressor. The decompressor is fine. The block-info
directory decompressed correctly — which is why the run got far enough to attempt a data block at
all — and the fault is the value of `data_start`, computed at `scripts/read-mod-bundle.py:129-130`,
sixty lines above the line that raised. When a freshly written reader fails, suspect the offsets
before the algorithm: the offsets are the part you derived, and the algorithm is the part that has a
published specification (`scripts/read-mod-bundle.py:23` cites
`github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md`).

## Related

- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md` — the
  mirror image. There a *fresh* timestamp wrongly implied the new build had landed; here a *stale*
  one wrongly implied a change had not. Same root cause, opposite sign, and that doc already carries
  the sentence this one generalises: "A fresh timestamp only proves a copy happened, not that it
  copied what you think."
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — a newly written
  reader is an instrument, and its first error message is a claim about itself as much as about the
  file it read.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why an Editor session and a
  restart are the expensive units in this project, which is what makes reading the artifact worth
  the fifty lines it took.
- `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md` — the
  broader discipline of not letting a plausible-looking assertion become an input to a decision, and
  the index-versus-disk distinction that bounds when this doc's rule applies.
- `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` — the icon
  mechanism whose verification prompted the reader, and the reason a name's presence in the bundle
  is the thing worth checking.
- `docs/protocols/2026-08-30-tech-tree-icons-handoff.md` — the corrected handoff, including the
  entry that was wrong and the sentence recording why.
- `docs/protocols/2026-08-31-slg-icon-licensing-ask.md` — the second question the same tool answered,
  and a model for stating what a partial reader does and does not establish.
