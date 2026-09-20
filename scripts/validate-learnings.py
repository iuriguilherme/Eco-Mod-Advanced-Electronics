#!/usr/bin/env python3
"""Check every learning under docs/solutions/ against the frontmatter contract.

    scripts/validate-learnings.py [--quiet]

Exits 0 when every doc conforms, 1 otherwise, 2 on a usage error. Run it from the
repository root.

Why this exists. The `ce-compound` plugin ships two validators and neither checks this:
`validate-frontmatter.py` checks that the header *parses* (delimiters, an unquoted `#`
that would truncate a value, an unquoted `: ` that would read as a nested mapping), and
`validate-doc-claims.py` checks the body's citations. Nothing checks the header against
the schema the store is written to, so an invalid enum or an over-long array survives
indefinitely -- a `problem_type` that is not a problem type, a bug-track doc missing the
three fields its track requires, a tag list twice the cap.

The contract below is a copy. The canonical version is `references/schema.yaml` inside the
compound-engineering plugin, which is not part of this repository and cannot be imported
from it, so this file restates the rules and has to be updated by hand if that schema
changes. Open vocabulary fields -- `component` and `root_cause` -- are deliberately not
checked against a list: the schema says they are corpus-first, so any value a doc uses is
admissible and only the closed enums are enforced here.
"""

import re
import sys
from pathlib import Path

SOLUTIONS = Path("docs/solutions")

BUG_TRACK = {
    "build_error", "test_failure", "runtime_error", "performance_issue", "database_issue",
    "security_issue", "ui_bug", "integration_issue", "logic_error",
}
KNOWLEDGE_TRACK = {
    "best_practice", "documentation_gap", "workflow_issue", "developer_experience",
    "architecture_pattern", "design_pattern", "tooling_decision", "convention",
}
SEVERITY = {"critical", "high", "medium", "low"}
RESOLUTION_TYPE = {
    "code_fix", "migration", "config_change", "test_fix", "dependency_update",
    "environment_setup", "workflow_improvement", "documentation_update",
    "tooling_addition", "seed_data_update",
}

REQUIRED = ("title", "date", "category", "module", "problem_type", "component", "severity")
BUG_REQUIRED = ("symptoms", "root_cause", "resolution_type")
ARRAY_CAPS = {"applies_when": 5, "symptoms": 5, "tags": 8}

DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
KEY_RE = re.compile(r"^([a-z_]+):\s*(.*)$")
ITEM_RE = re.compile(r"^\s+-\s+(.*)$")


def parse_frontmatter(text):
    """Split a doc's YAML header into scalars and arrays.

    Small on purpose: the store writes a narrow, regular subset of YAML -- scalars,
    block sequences and flow sequences of strings -- so a dependency-free reader is
    enough and keeps this script runnable on a bare Python install.
    """
    if not text.startswith("---"):
        return None, None
    end = text.find("\n---", 3)
    if end < 0:
        return None, None

    scalars, arrays, key = {}, {}, None
    for line in text[4:end].split("\n"):
        matched = KEY_RE.match(line)
        if matched:
            key, value = matched.group(1), matched.group(2).strip()
            if value.startswith("[") and value.endswith("]"):
                arrays[key] = [v.strip() for v in value[1:-1].split(",") if v.strip()]
            elif value:
                scalars[key] = value.strip('"').strip("'")
            else:
                arrays[key] = []
            continue
        item = ITEM_RE.match(line)
        if item and key:
            arrays.setdefault(key, []).append(item.group(1).strip())
    return scalars, arrays


def check(path, repo_root):
    text = path.read_text(encoding="utf-8", errors="replace")
    scalars, arrays = parse_frontmatter(text)
    if scalars is None:
        return ["no parsable YAML frontmatter"]

    problems = []

    for field in REQUIRED:
        if field not in scalars:
            problems.append(f"missing required field `{field}`")

    problem_type = scalars.get("problem_type")
    if problem_type and problem_type not in BUG_TRACK | KNOWLEDGE_TRACK:
        problems.append(f"problem_type `{problem_type}` is not one of the schema's values")

    severity = scalars.get("severity")
    if severity and severity not in SEVERITY:
        problems.append(f"severity `{severity}` is not one of {sorted(SEVERITY)}")

    resolution = scalars.get("resolution_type")
    if resolution and resolution not in RESOLUTION_TYPE:
        problems.append(f"resolution_type `{resolution}` is not one of the schema's values")

    if problem_type in BUG_TRACK:
        for field in BUG_REQUIRED:
            if field not in scalars and field not in arrays:
                problems.append(f"bug-track doc missing required `{field}`")

    date = scalars.get("date")
    if date and not DATE_RE.match(date):
        problems.append(f"date `{date}` is not YYYY-MM-DD")
    last_updated = scalars.get("last_updated")
    if last_updated and not DATE_RE.match(last_updated):
        problems.append(f"last_updated `{last_updated}` is not YYYY-MM-DD")

    for field, cap in ARRAY_CAPS.items():
        count = len(arrays.get(field, []))
        if count > cap:
            problems.append(f"{field} has {count} items, cap is {cap}")

    # The directory a doc lives in and the category it claims must agree; a mismatch
    # means one of the two is wrong and a reader searching by either will miss it.
    category = scalars.get("category")
    directory = path.parent.name
    if category and category != directory:
        problems.append(f"category `{category}` but the doc is in `{directory}/`")

    return problems


def main() -> int:
    args = [a for a in sys.argv[1:]]
    quiet = "--quiet" in args
    args = [a for a in args if a != "--quiet"]
    if args:
        print("usage: scripts/validate-learnings.py [--quiet]", file=sys.stderr)
        return 2

    repo_root = Path.cwd()
    if not SOLUTIONS.is_dir():
        print(f"missing {SOLUTIONS} - run this from the repository root", file=sys.stderr)
        return 2

    docs = sorted(p for p in SOLUTIONS.rglob("*.md") if p.name != "README.md")
    failures = 0
    for doc in docs:
        problems = check(doc, repo_root)
        if problems:
            failures += 1
            rel = str(doc.as_posix())
            print(f"\n{rel}")
            for problem in problems:
                print(f"    - {problem}")

    if failures:
        print(f"\nFAIL: {failures} of {len(docs)} learnings violate the frontmatter contract")
        return 1

    if not quiet:
        print(f"PASS: {len(docs)} learnings conform to the frontmatter contract")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
