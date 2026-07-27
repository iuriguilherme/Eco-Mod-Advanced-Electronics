---
title: "Two shells, one repo: Windows toolchain assumptions that fail silently"
date: 2026-07-27
category: developer-experience
module: AdvancedElectronics
problem_type: developer_experience
component: development_workflow
severity: medium
applies_when:
  - "Running shell commands against this repo from a harness that exposes both PowerShell and Git Bash"
  - "Writing multi-line git commit messages from a script or an agent"
  - "Writing a repo script that shells out to common POSIX utilities on Windows"
  - "A tracked file shows as modified but git diff prints nothing"
tags: [windows, git-bash, powershell, heredoc, crlf, tooling, developer-experience]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Two shells, one repo: Windows toolchain assumptions that fail silently

## Context

This project is developed on Windows from a harness that exposes **two** shells: PowerShell and Git
Bash. They take different syntax, ship different utilities, and inherit different path limits. A
command written with one in mind will often run in the other — sometimes erroring immediately,
sometimes producing a subtly wrong result and reporting success.

Four such mismatches were hit in a single session. Two announced themselves; two did not. The split
between those categories is the useful part.

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

Caught here before pushing, so `git commit --amend -F -` was enough. After a push it is a history
rewrite, so the check belongs immediately after committing: read back the subject line.

### Silent: CRLF normalization looks like a modification

A tracked file can appear in `git status` as modified while `git diff` prints nothing but a warning:

```
warning: in the working copy of '<file>', LF will be replaced by CRLF the next time Git touches it
```

That is line-ending bookkeeping, not a content change. Chasing it wastes time, and — worse —
committing it produces a diff-less commit that pollutes history. `git checkout -- <file>` clears it.
The tell is an empty `git diff` for a file `git status` calls modified.

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
are exposed to. If it is silent, add a read-back — check the commit subject, check the artifact
exists, check the diff is non-empty — because nothing else will tell you.

## When to Apply

- Any time a multi-line string is passed to a command from a shell, especially git messages.
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

The read-back that catches it, worth running after any scripted commit:

```bash
git log --format="%h %s" -1
# if the subject starts with a stray delimiter, amend before pushing:
#   git commit --amend -F - <<'EOF' ...
```

## Related

- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the same instinct
  applied to release artifacts: when a failure is silent, add a check rather than trusting that it
  ran fine.
