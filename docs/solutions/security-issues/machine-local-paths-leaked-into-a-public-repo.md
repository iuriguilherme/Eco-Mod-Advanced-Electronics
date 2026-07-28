---
title: "A production machine's paths and account name reached a public repo, and forward-fixing was not enough"
date: 2026-07-28
category: security-issues
module: docs
problem_type: security_issue
component: tooling
severity: high
symptoms:
  - "Tracked documentation contains an absolute home-directory path including the account name"
  - "Tracked docs or commit messages name a local install location for an external runtime"
  - "The same information appears correctly parameterised in one doc and hardcoded in its sibling"
root_cause: configuration
resolution_type: config_change
tags: [git, public-repo, information-disclosure, history-rewrite, filter-repo, documentation]
related_components: [docs]
---

# A production machine's paths and account name reached a public repo, and forward-fixing was not enough

## Problem

Documentation written during a debugging session recorded a log file by its **literal absolute
path**, including the machine account name, and was pushed to a public repository. Two older
planning docs carried an absolute install path for the test server. None of it was a credential —
all of it described the layout of a machine the maintainer actually works on.

## Symptoms

Nothing fails. That is the whole difficulty: a leaked path is valid content, renders fine, and reads
as helpful specificity. It surfaces only when someone looks.

A single query across tracked files finds the class:

```bash
git grep -n -I -E "C:\\\\(Games|Users)|/home/|steamapps|AppData|LocalLow" -- .
```

The tell that it was carelessness rather than necessity: the **same information appeared correctly
parameterised in a sibling doc** written an hour earlier (`%USERPROFILE%\AppData\LocalLow\...`) and
hardcoded in this one. The right form was already established in the same session.

## What Didn't Work

**Fixing forward only.** The first response was to correct the files and push — which cleans the
current tree and leaves every prior commit intact on the public remote. The corrected tree gives a
false sense of resolution, because `git grep` on the working tree comes back clean while
`git log -S` still finds the string.

**Reasoning about severity instead of asking.** The initial assessment was that this was low
severity — no credential, and the account name is adjacent to a public commit identity anyway — so
history rewriting was proposed as optional and discouraged. That reasoning treated it as a
public-handle question. The maintainer's framing was different and decisive:

> "this is not a virtual machine. this is a production windows box. those informations are not to be
> sent to git history"

followed by the point that settles it regardless of severity:

> "the information is not even relevant, another machine does not have those paths and it's useless
> for other collaborators"

An absolute local path has **no value to anyone else**. There is no benefit to weigh the risk
against, so the severity debate was beside the point. Whose machine it is, and how it should be
handled, was not a judgment to make on the maintainer's behalf.

## Solution

**Prevention** — cite paths one of three ways, and never a fourth:

| Form | Example |
|---|---|
| Repo-relative | `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` |
| Environment variable | `%USERPROFILE%\AppData\LocalLow\…` |
| By role | "the test server's `Mods` folder" |

Machine-specific values belong in git-ignored config (this project uses a `Local.props` that MSBuild
imports when present) or in agent memory files outside the repo. This applies to **commit messages
as much as tracked files** — a message is as public as a blob and is missed by any check that only
scans the working tree.

**Remediation** — when it has already been pushed, rewrite history:

```bash
# 1. Back up every ref first.
git bundle create ../pre-rewrite-backup.bundle --all

# 2. Rules file: literal==>replacement, one per line.
#    C:\Users\<name>==>%USERPROFILE%
#    <install path>==><placeholder>

# 3. Rewrite blobs AND commit messages -- --replace-text alone misses messages.
git filter-repo --replace-text rules.txt --replace-message rules.txt --force

# 4. filter-repo strips the remote by design. Re-add it.
git remote add origin <url>

# 5. Force-push EVERY published ref -- branches and tags alike.
git push --force origin <branch>
git push --force origin refs/tags/<tag>
```

Then verify across all refs, not just the working tree:

```bash
for p in 'C:\Users\<name>' '<install path>'; do
  echo "$p: $(git log --all -S "$p" --oneline | wc -l)"   # want 0
done
```

Two environment notes that cost time here: `git filter-repo` installs via pip and its `git
filter-repo` subcommand works **even when `import git_filter_repo` fails**, so an import check is
not a availability check. And `git ls-remote` is what tells you which refs are actually published —
a tag that was pushed months ago is easy to forget and will otherwise preserve the old history.

## Why This Works

Rewriting makes the old objects unreachable from any ref, so ordinary clones and fetches never see
them. Two honest limits are worth stating to whoever asks for the cleanup:

- **Existing clones and forks diverge.** Anyone holding one needs `git fetch && git reset --hard`,
  or a fresh clone. On a repo with collaborators this is a real cost, and it is theirs to accept.
- **The host may still serve old objects by direct SHA** until its garbage collection runs.
  Unreachable is not the same as deleted; immediate purging is a support request.

Because the operation is destructive and outward-facing, it is the repository owner's call. Present
the scope, the cost, and the limits — then do it if they say so.

## Prevention

- **Check content and commit message before every commit** with the `git grep` query above. It is
  one command and it catches the whole class.
- **Never trust having written it correctly elsewhere.** The correct form existed in a sibling doc in
  the same session and did not prevent the mistake, because the check that matters is mechanical, not
  memory.
- **Point machine-specific config at a git-ignored file and reference the variable**, so the correct
  form is also the convenient one — here that doubled as a fix for a silent wrong-tree deploy
  (`docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md`).
- **When the maintainer flags something as sensitive, adopt their framing.** They know what the
  machine is. Do not weigh a risk on their behalf, and do not recommend leaving a leak in place on a
  severity judgment they did not ask for.

## Related

- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md` — the
  git-ignored config file that keeps machine paths out of the repo is the same one that keeps builds
  landing on the right server.
- `docs/solutions/conventions/excluding-third-party-from-a-unity-mod-repo.md` — the other question of
  what must not enter this repository, from the licensing side rather than the privacy side.
