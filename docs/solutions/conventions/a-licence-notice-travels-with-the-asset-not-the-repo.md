---
title: "Licensing a contributed asset: what decides the licence, and where the notice has to travel"
date: 2026-08-08
last_updated: 2026-08-09
category: conventions
module: AdvancedElectronics
problem_type: convention
component: tooling
severity: high
applies_when:
  - "You author a new asset against someone else's licensed model, rig, texture or animation"
  - "You are deciding which licence a file in a mixed-licence art folder falls under"
  - "A licensing rule you are about to write requires an exception or a carve-out"
  - "Contributed art arrives under a copyleft licence such as CC BY-SA"
  - "The contributed asset ships inside a packaged artifact as well as living in the repository"
tags: [licensing, cc-by-sa, sharealike, derivative-works, attribution, third-party-art, release-packaging]
related_components:
  - "README.md"
  - "LICENSE-ART"
  - "scripts/package-release.sh"
  - "Assets/Art/AdvancedElectronics/Sprites/HRVSTR"
---

# Licensing a contributed asset: what decides the licence, and where the notice has to travel

Two questions come up whenever someone else's work enters this repository, and they are
easy to conflate. **Which licence covers a given file?** and **where does the notice have to
be so that it reaches whoever ends up with the file?** The first half of this document is the
one that was got wrong; the second is the one that was got right first time and is worth
keeping.

## Context

`Assets/Art/AdvancedElectronics/Sprites/HRVSTR/HRVSTR_BladesMask.mask` is a Unity avatar
mask. The maintainer authored it himself — nobody else touched it, and it is not a copy of
anything. From that fact a conclusion was drawn that felt obvious: our file, our licence, so
it is LGPL-3.0-or-later like the rest of the project, even though it sits in a folder full of
Phlo123's CC BY-SA 4.0 drone art.

That was wrong, and it was recorded confidently in several places at once — in this document,
in the licence file of the day, and in the procedure telling future contributors to enumerate
covered files individually *because* of this case. The mask was described as "the sharpest
expression of the boundary": same folder as the contributed files, named after the contributed
model, authored against the contributed rig, and *ours*, under LGPL.

The reasoning underneath was: we made this file, therefore we license it. That answers
**authorship** — who holds copyright in the new contribution — and then silently substitutes
it for **licence**, which is a different question: under what terms may this contribution be
distributed, given what it was built from.

The maintainer corrected it, in his words:

> The blade mask file was done by me but it's covered in the same CC license because that is
> exactly what that license says: I am distributing remixed work under the same terms.

The mask is an avatar mask authored *against Phlo123's rig*. That makes it an adaptation, and
CC BY-SA's ShareAlike condition says adaptations go out under the same licence. It is CC
BY-SA 4.0, like everything else in that folder.

The wrong version had already shipped: the carve-out was in the `v0.2.0` release zip, public
on mod.io, before the correction landed in `docs(license): the avatar mask is an adaptation,
so it carries the same licence`.

## Guidance

### What decides the licence

**The question is not "who made this file" and not "where does this file live". It is "what
was this made from, and what does that thing's licence say about derivatives?"**

For the mask, the first half is visible in the file itself. It is a list of transform paths
lifted from Phlo123's rig, each with a weight:

```yaml
  - m_Path: HRVSTR_Armature/Drone_Base/Arm_L1/Arm_L2/Arm_L3/DrillL/DrillL_Hammer/DrillL_Bit
    m_Weight: 0
  - m_Path: HRVSTR_Armature/Drone_Base/Blades_BL
    m_Weight: 1
```

Every meaningful line names a bone in someone else's armature. Strip the rig away and the file
means nothing — it is not merely stored next to the model, it is written in the model's
vocabulary. That is what makes it derived rather than incidental.

The second half is in the licence, and is worth reading in the licence's own words rather than
a paraphrase — paraphrase is how the error was produced. `LICENSE-ART` is the verbatim CC
BY-SA 4.0 legal code. Section 1(a), at `LICENSE-ART:73-81`, defines what counts:

> Adapted Material means material subject to Copyright and Similar
> Rights that is derived from or based upon the Licensed Material
> and in which the Licensed Material is translated, altered,
> arranged, transformed, or otherwise modified in a manner requiring
> permission under the Copyright and Similar Rights held by the
> Licensor.

Section 3(b), at `LICENSE-ART:273-285`, is the obligation:

> b. ShareAlike.
>
> In addition to the conditions in Section 3(a), if You Share
> Adapted Material You produce, the following conditions also apply.
>
> 1. The Adapter's License You apply must be a Creative Commons
> license with the same License Elements, this version or
> later, or a BY-SA Compatible License.

Note the phrase "Adapted Material **You produce**". The licence has already anticipated the
case where you are the author of the new thing — that is the only case ShareAlike is about.
Authorship is assumed by the clause, not an exemption from it. "License Elements" at
`LICENSE-ART:109-111` are "Attribution and ShareAlike", which LGPL-3.0-or-later plainly is
not. Applying LGPL to the mask is not something this licence leaves open.

**Prefer the model that needs no exception.** The wrong rule required a carve-out: a named
file, listed against the folder, with an explanation of why it was special. The right rule
deletes all of that — the folder is one licence, so `README.md` scopes by folder and stops.
That asymmetry is a usable signal. When a licensing conclusion forces you to write an
exception, look again at the premise that produced it.

**Folder adjacency is a red herring in both directions.** The wrong rule was built on the
observation that "same folder does not mean same licence" — true about filesystems, useless as
a test. A derived file dropped in an unrelated directory is still derived; an independent file
placed among licensed art is still independent. Derivation decides, and derivation is about
provenance, not path.

**Frame the answer as a recorded decision, not a ruling.** This describes what CC BY-SA 4.0's
own text requires and what this project concluded about one file. It is not legal advice and
does not generalise to licences nobody here has read.

### Where the notice has to travel

**Put the notice wherever the asset can end up, and treat the repository as the least
important of those places.**

The drone model does not only live in the repo. It is baked into `AdvancedElectronics.unity3d`
and shipped in a zip to server admins who download it from mod.io and will very likely never
see GitHub. An asset gets lifted out of its repository as a matter of routine — that is what a
build *is*. A notice that exists only in the repo is attached to the one copy that needs it
least. The bundle went from roughly 1 MB to 6.7 MB when the model arrived; those bytes are the
argument in one number.

So the notice lives in three places, each aimed at a different reader:

1. **`LICENSE-ART` at the repo root** — the verbatim CC BY-SA 4.0 legal code, nothing else,
   the way `LICENSE` holds the LGPL text. For someone reading the source.
2. **The `Attribution` section of `README.md`** (`README.md:281-287`) — who made it, what it
   covers, scoped by folder, pointing at `LICENSE-ART` for terms. For someone evaluating the
   project.
3. **`LICENSE-ART.txt` inside the shipped zip**, beside `LICENSE.txt`, staged by
   `scripts/package-release.sh:122`, with the attribution repeated in the generated
   `README.txt`. For whoever ends up with the artifact and no context at all.

**A blanket "everything here is our own work" claim has an expiry date.** `README.md` carried
exactly that sentence, and it stopped being true the moment the model landed. Nothing
enforces it — no compiler, no test, no CI job diffing the contributor list against the licence
file. It stayed wrong for 46 commits across five days in a public repository. Re-read it
whenever a contribution arrives.

**Verify from the artifact, not from the tree.** The repo having a `LICENSE-ART` proves
nothing about the zip. Read the file back out of the archive; a green `git status` is not
evidence.

## Why This Matters

**Nothing catches a licensing error.** The wrong carve-out compiled, bundled, packaged,
uploaded and installed without a murmur. The tests passed. The release script's staleness
check passed. The documentation validators passed. There is no failing test for a licence
mistake — the only detector in the loop is a person who happens to think about it. That is how
a wrong statement travelled from a doc into the licence file, into `README.md`, into a shipped
`README.txt`, and out to mod.io as part of `v0.2.0`.

**The error pointed the wrong way, which is the worse direction.** Claiming a permissive
licence over an adaptation of someone else's copyleft work understates their terms to everyone
downstream. Someone who took the mask on the strength of an LGPL claim would have been misled
by this project, not by Phlo123. Copyleft was a term the contributor chose deliberately, and
quietly narrowing it on his behalf is a bigger failure than a missing credit.

**The confusion is structural, not careless.** "I made this file" is true, verifiable, and
arrives already sounding like an answer. It takes a deliberate second step to notice it
answers a question about copyright ownership while the question on the table is about
distribution terms. Both questions concern the same file, both have "me" as a plausible
answer, and only one of them is settled by who did the work.

## When to Apply

Apply the derivation test whenever you add or modify a file made *using* licensed material,
which in a game-mod repo covers more than it first appears:

- **Avatar masks, avatars and humanoid rig mappings** — written entirely in the source rig's
  transform paths. This is the case that caught us.
- **Retargeted or re-authored animation clips** — animating someone else's skeleton produces
  curves keyed to their bone names.
- **Materials authored on supplied textures**, and textures painted over a supplied layout.
- **Meshes edited, decimated, re-topologised or kitbashed from a supplied base.**
- **Icons or sprites rendered from a licensed model** — a render of a model is made from it.

Apply the placement rule whenever the packaging changes: a new output path, a new distribution
channel, or a new file staged into the zip is a new place the notice has to reach.

Apply both whenever you are about to write a licence exception. The exception is the smell.

**Where the line is genuinely unclear.** The mask is an easy case because its entire content is
the rig's structure. Others are not, and pretending otherwise would replace one overconfident
rule with another:

- A **material** referencing a licensed texture by GUID contains none of its pixels. Adaptation,
  or an independent work that points at one? Reasonable people differ.
- A **prefab** instantiating a licensed model is arguably assembly rather than modification —
  though a prefab whose hierarchy mirrors the model's internals starts to look like the mask.
- A **build artifact** like the asset bundle contains the model outright. Not an interesting
  question: the licensed bytes are in there, which is exactly why the notice ships in the zip.

Where it is unclear, the cheap move is to license the ambiguous file under the stricter of the
two and say so. Over-applying ShareAlike to one mask costs nothing; under-applying it is a
public misstatement about someone else's work.

None of this applies to Eco or the Eco ModKit. Those are never tracked here at all — the
correct handling for them is non-redistribution rather than attribution, which is why
`README.md` keeps them under a separate `Third Party` heading.

## Examples

### The wrong reasoning, written out

> The mask is named after the drone. It lives in the drone's folder. It is authored against the
> drone's rig. But we made it, so it is ours — LGPL, carved out of the contributed set.

Every sentence before the "but" describes derivation. The "but" throws them all away in favour
of a fact about authorship, and the conclusion follows from the discarded half. The tell is
that the premises and the conclusion are about different things.

### The right reasoning, same file

*What was it made from?* Phlo123's rig — the file is a weighted list of his bone paths and
means nothing without them.

*What does that licence say about derivatives?* `LICENSE-ART:73-81` calls material "derived
from or based upon the Licensed Material" Adapted Material; `LICENSE-ART:273-285` says Adapted
Material You produce must go out under a licence with the same License Elements.

*Therefore:* CC BY-SA 4.0. That the maintainer authored it is the reason ShareAlike is engaged
at all, not a reason it is not.

### What the correct model let us delete

The carve-out is gone from `README.md`, along with the per-file list it existed to qualify and
the procedure telling contributors to enumerate covered files. What remains is one folder path
and one licence name, plus one clause in the shipped readme — "and work adapted from them" —
which covers the mask and every future mask without naming any of them.

### What is still LGPL, and why that is different

The C# under `EcoServerMod/`, the navigation core, the dock prefab and the Unity scene are not
made from Phlo123's model. They sit alongside it in a repository and travel alongside it in a
zip, which is distribution together, not derivation. They remain LGPL-3.0-or-later. That is
compatible with the mask being CC BY-SA, because the mask fails the test the code passes: it
was made *from* the licensed material.

## Related

- `docs/solutions/conventions/excluding-third-party-from-a-unity-mod-repo.md` — the other half
  of the licensing picture: material that must stay out of a public repo entirely because
  redistribution is not permitted.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — the shape
  of why "everything here is our own work" went stale unnoticed: a claim keeps standing after
  the thing that made it true has changed.
- `docs/solutions/conventions/a-document-stored-in-its-own-generator-has-no-past-tense.md` —
  the same generated readme, a different failure. Its attribution block is hand-written prose,
  so it will not update itself when the next asset arrives.
- `docs/solutions/runtime-errors/override-animator-layer-without-avatar-mask-overwrites-base-layer.md`
  — what `HRVSTR_BladesMask.mask` does and why the animator needs it. A file can be technically
  necessary, fully understood, and still misfiled on licence; the two audits are independent.
