---
title: "Two shells, one repo: Windows toolchain assumptions that fail silently"
date: 2026-07-27
last_updated: 2026-08-21
category: developer-experience
module: AdvancedElectronics
problem_type: developer_experience
component: development_workflow
severity: high
applies_when:
  - "Running shell commands against this repo from a harness that exposes both PowerShell and Git Bash"
  - "Writing multi-line git commit messages from a script or an agent"
  - "Writing a repo script that shells out to common POSIX utilities on Windows"
  - "A tracked file shows as modified but git diff prints nothing"
  - "An agent is choosing between the PowerShell tool and the Bash tool for the same command"
  - "Verifying a commit, tag, note, or PR body that was assembled by a tool rather than typed"
  - "Writing a rule whose whole job is to catch a failure that produces no error"
tags: [windows, git-bash, powershell, heredoc, crlf, tooling, developer-experience, silent-failure, verification, read-back]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Two shells, one repo: Windows toolchain assumptions that fail silently

## Context

This project is developed on Windows from a harness that exposes **two** shells: PowerShell and Git
Bash. They take different syntax, ship different utilities, and inherit different path limits. A
command written with one in mind will often run in the other — sometimes erroring immediately,
sometimes producing a subtly wrong result and reporting success.

Several such mismatches have been hit here. Some announce themselves; some do not. The split between
those two categories is the useful part, and it is what organises the sections below.

**One of them recurred.** The here-string trap immediately below was written down on 2026-07-27 and
hit again on 2026-08-20, three and a half weeks later, twice in one session. The rule was correct,
specific, and in the repository. It did not fire. That recurrence is why this doc now spends more
words on the *check* than on the syntax: the check it originally prescribed could not tell a clean
commit from a corrupted one.

## Guidance

**Sort every cross-shell assumption by whether it fails loudly or silently, and spend your attention
on the silent ones.** A loud failure is self-diagnosing — you read the error and fix it once. A
silent one lands in an artifact and stays there.

### Silent: PowerShell here-strings inside Bash

PowerShell's here-string is `@'...'@`. Bash has no such construct, so the delimiters are passed
through as ordinary text. Used for a multi-line commit message, this produces a commit whose subject
line begins with a literal `@`:

```
452f6d8 @ chore(git): ignore only third-party content, track project config
```

(That SHA is deliberately unreachable from `main` — it is the pre-amend commit, kept here only as
evidence of the symptom. Tooling that checks docs for commit references will flag it as orphaned,
which is correct.)

Nothing errors. `git commit` succeeds, the body is intact, and the damage is a corrupted subject in
permanent history. In Bash, use a heredoc — and prefer `-F -` over `-m` so the message is read from
stdin verbatim:

```bash
git commit -q -F - <<'EOF'
subject line

Body paragraph.
EOF
```

Quote the delimiter (`<<'EOF'`, not `<<EOF`) so `$`, backticks and `!` in the message are not
expanded — a commit body describing shell code will otherwise mangle itself.

#### The check has to be `%B`. A subject-line read-back is not enough.

The 2026-08-20 recurrence landed a stray `@` at **both** ends -- one before the subject and one after
the trailer -- and the subject-line check this doc used to prescribe missed it. Reproduced in a
scratch repository created for the purpose, so the hash below is **not** a commit in this repo and a
docs-audit pass will flag it as unresolvable, which is correct:

```console
$ git log --oneline -1
152e43b @ docs: document InOutLinkedInventoriesComponent as a client-UI feature flag

$ git log --format='%h %s' -1
152e43b @ docs: document InOutLinkedInventoriesComponent as a client-UI feature flag

$ git log -1 --format='%B' | cat -A
@$
docs: document InOutLinkedInventoriesComponent as a client-UI feature flag$
$
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>$
@$
```

Two separate things go wrong in the subject views, and both favour the mistake:

1. **The leading `@` is folded into the subject.** Git computes `%s` from the first *paragraph*, not
   the first line, collapsing the newlines inside it. With nothing separating `@` from the real
   subject, both lines become one subject printed joined by a space. The result is a tidy line that
   reads as a correct subject wearing a stray prefix -- a marker, a scope token, a rendering
   artifact. It does not read as damage.
2. **The trailing `@` is invisible.** It sits in the body, so no subject-oriented view shows it at
   all. A reader who notices the leading `@` and mentally strips it still has no idea a second one is
   there.

So the check is the raw body, run before the push, on any commit whose message was assembled by a
tool rather than typed:

```bash
git log -1 --format='--- %h%n%B'
```

Add `| cat -A` to see exact bytes, including trailing whitespace. For a stack, widen to `git log -5`.

**After a push there is no fix.** This repo is public and released, and the standing rule is to keep
history always -- no rewrite, pushed or not. Before the push an amend is enough; after it, a purely
cosmetic defect is permanent on a public log and the project's own rules forbid the cheap remedy.
The pre-push window is the only place this check is worth anything, which is also why it has to be
fast enough to run reflexively.

### Silent: `git commit` is the surface that disguises this best

The same corruption on other surfaces is far more obvious, which is worth knowing so the shape is
recognisable when it turns up somewhere else:

```console
$ git tag -a v0.0.1 -m @'
Release 0.0.1
'@
$ git tag -n1
v0.0.1          @              # obviously broken -- the first LINE is the whole preview
```

`git tag -n1` previews the first line, so it self-reports. `git commit` previews the first paragraph,
so it does not. The surface whose output is permanent and public is the one that hides it.

The general rule underneath: **distrust exit 0 specifically when the command's job is to store a
string you supplied.** `git commit -m`, `git tag -a -m`, `git notes add -m`, `gh pr create --body`
and friends accept arbitrary text by design. Validating it is not their job and they will not do it,
so a zero exit proves the call succeeded and says nothing about whether the content is what you
meant. Prefer `-F -` (or `--body-file -`) on every one of them, and read the artifact back.

### Silent: CRLF normalization looks like a modification

A tracked file can appear in `git status` as modified while `git diff` prints nothing but a warning:

```
warning: in the working copy of '<file>', LF will be replaced by CRLF the next time Git touches it
```

That is line-ending bookkeeping, not a content change. Chasing it wastes time, and — worse —
committing it produces a diff-less commit that pollutes history. `git checkout -- <file>` clears it.
The tell is an empty `git diff` for a file `git status` calls modified.

### Loud: a long heredoc through the Bash tool dies on a phantom quote

A command whose heredoc body runs to roughly two hundred lines fails with:

```
/usr/bin/bash: -c: line 146: unexpected EOF while looking for matching '''
```

The delimiter is quoted and correctly terminated at column 0, and the body is valid. Hit twice: once
writing a doc with `cat > file <<'DOC'`, once running a `python - <<'PY'` edit script. The reported
line was 146 and 141 -- both well short of the real end of the body, which is the tell.

**What it is not.** Each of these was tested directly and passes: odd apostrophes in the body, a
literal three-quote sequence, `cd ... &&` before the heredoc, and `python - <<'PY'` with quotes
inside a raw string. None of them reproduce it.

**What correlates.** Length. The identical edit, split into two calls of roughly forty and eighty
five lines, succeeded on both. The failing versions were around two hundred. The exact threshold was
not established and no mechanism is claimed -- the quote named in the error is most likely an
artifact of the body being cut mid-content, not a real unbalanced quote.

**The working rule:** keep a heredoc through the Bash tool short. Split a long edit into several
calls, or use the Write tool, which has no quoting layer to get wrong. When one fails this way, do
not iterate on the quoting -- the quoting is not the problem.

### Loud: assumed POSIX utilities are missing

Git Bash on Windows is not a full POSIX userland. `zip` is **absent**; `python3` is present. A repo
script that shells out to common tools should degrade rather than assume:

```bash
if command -v zip >/dev/null 2>&1; then
    ( cd "$STAGE" && zip -qr "../$(basename "$OUT")" . )
elif command -v python3 >/dev/null 2>&1; then
    python3 - "$STAGE" "$OUT" <<'PY'
# ... zipfile fallback
PY
else
    fail "neither 'zip' nor 'python3' available to build the archive"
fi
```

Check that the artifact exists afterwards, before deleting the staging directory — a fallback that
silently produced nothing would otherwise destroy its own inputs.

### Loud: Windows path length breaks git operations

Cloning into a deep scratch directory fails partway through:

```
fatal: failed to unlink '.../.git/objects/info/commit-graphs/graph-<40-hex>.graph': Filename too long
```

Git's own object paths are long; a deep parent pushes them past the limit. Use a short path
(`/c/Users/<user>/AppData/Local/Temp/<short-name>`) for scratch clones and temporary checkouts rather
than nesting under an already-deep session directory.

## Why This Matters

The two silent failures both write into things that are hard to take back. A commit message is
effectively immutable once pushed; a line-ending-only commit is noise every future reader has to
scroll past. Neither produces an error at the moment of the mistake, so neither is caught by "it
ran fine".

The loud two are worth writing down for a different reason: they are cheap to *prevent* but annoying
to *rediscover*. A script that assumes `zip` works on the author's machine and fails on everyone
else's is a small, avoidable tax on the next contributor.

The general habit this suggests: when a command spans two environments, ask which failure mode you
are exposed to. If it is silent, add a read-back — check the commit message, check the artifact
exists, check the diff is non-empty — because nothing else will tell you.

**And the read-back has to be unambiguous, not merely present.** This is the lesson of the
recurrence, and it is a defect this document itself had. The check originally written here *did*
display the leading `@`; it was still not enough, because its passing output and its failing output
looked alike enough to be confused. A verification whose failure state reads as plausible is not a
verification — it is a second place the error gets a signature. Ask of any check you write down: *if
this were broken, would the output look obviously broken, or would it look mostly fine?* If the
answer is "mostly fine", choose a different output format.

**A recurrence is evidence about the check, not about the prose.** The rule here was correct and
discoverable and was violated anyway. Treating that as "someone should have read the doc" wastes the
incident; the doc was not the missing piece.

The four properties that compose the here-string failure are each survivable alone: two shells with
different syntax reachable from one turn, where the wrong choice is a silent no-op rather than an
error; a receiving command that validates nothing, so a quoting mistake becomes stored content
instead of a shell error; a default inspection command that renders the damage as readable; and a
blast radius that steps up sharply at the push. Together, every step self-reports success and the one
step that could have reported failure renders the failure as success.

## When to Apply

- Any time a multi-line string is passed to a command from a shell, especially git messages. Use
  `-F -` with a quoted heredoc rather than `-m`, on every surface that takes one.
- Before every push, on any commit, tag, note, or PR body assembled by a tool, a script, or an agent
  rather than typed into an editor.
- When a command's job is to *store what you gave it*. Those commands cannot fail on bad input, so
  their success tells you nothing.
- When writing or refreshing a rule that prescribes a verification step: check that the step's
  failing output is visibly distinct from its passing output.
- When a rule in `docs/solutions/` has been violated *again* — the recurrence is the signal that its
  check is wrong, not that its prose needs to be louder.
- When writing a script that will live in the repo and run on other people's machines.
- When `git status` and `git diff` disagree about whether a file changed.
- When a git operation fails on a path rather than on content — suspect length before suspecting
  corruption.

## Examples

The failure and the fix, side by side:

```bash
# WRONG in Bash — @'...'@ is PowerShell; the @ leaks into the subject line
git commit -q -m @'
chore(git): ignore only third-party content
'@

# RIGHT in Bash — quoted heredoc, message read from stdin
git commit -q -F - <<'EOF'
chore(git): ignore only third-party content

Body text with $variables and `backticks` left literal.
EOF
```

The read-back that catches it, next to the one that does not — same commit, three views (scratch
repo again; the hash is not from this repository):

```text
$ git log --oneline -1
152e43b @ docs: document InOutLinkedInventoriesComponent as a client-UI feature flag
          ^ folded into the subject; reads as a stray prefix, not as damage.
            The trailing @ does not appear at all.

$ git log --format='%h %s' -1
152e43b @ docs: document InOutLinkedInventoriesComponent as a client-UI feature flag
          ^ identical. This is what this doc used to prescribe. It is not enough.

$ git log -1 --format='%B' | cat -A
@$
docs: document InOutLinkedInventoriesComponent as a client-UI feature flag$
$
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>$
@$
          ^ both delimiters visible, on their own lines. Unambiguous.
```

The command to keep — short enough to run reflexively, and self-identifying when several commits are
in flight:

```bash
git log -1 --format='--- %h%n%B'
# if anything but subject / blank line / trailer appears, amend BEFORE pushing:
#   git commit --amend -F - <<'EOF' ...
```

## Related

- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the same instinct
  applied to release artifacts: when a failure is silent, add a check rather than trusting that it
  ran fine.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  defect one level up: a check whose output cannot distinguish the failure it exists to find will
  report clean forever. A subject-line read-back is a gate of that kind for this corruption.
- `docs/solutions/conventions/commit-bodies-list-changes-not-lessons.md` — what a correct message
  looks like here (subject, blank line, trailer; no prose rationale), and the forward-only stance on
  never rewriting pushed history to fix one.
- `docs/solutions/security-issues/machine-local-paths-leaked-into-a-public-repo.md` — the other
  commit-content rule, and what remediation costs once a bad message has been pushed.
- `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md` — the
  upstream half of "distrust the self-report". Here the unverified claim is a tool's own exit code.
