#!/usr/bin/env python3
"""Fail when a farm's persisted members stop making the crossing into the area.

The v0.4.0 farm fold moves eighteen `[Serialized]` members off `FarmAreaEntry` and onto
`SurveyAreaEntry`, through two Eco-free shapes in between:

    FarmAreaEntry  ->  LegacyFarmRow  ->  FoldedFarmArea  ->  SurveyAreaEntry
    (Eco, saved)       (pure)             (pure)              (Eco, saved)

Nothing in the compiler or the test suite checks that chain end to end, and the gap runs
one way. `LegacyFarmRow`'s constructor defaults nothing, so mirroring a member onto it
without updating the call site is a build error. The opposite -- adding a `[Serialized]`
member to `FarmAreaEntry` and never mirroring it -- compiles, keeps every test green, and
silently drops that member from every farm the fold touches. The unit tests cannot catch
it either: they assert `LegacyFarmRow` and `FoldedFarmArea` against each other, and the
test project cannot reference the Eco side at all.

A dropped member is not cosmetic. `LevelFirst` decides whether the drone levels ground
before planting; `LevelBankedSpoil` is material a player is owed back from an unfinished
level pass. Losing either reads at the table as the mod forgetting a setting, days after
the update that did it.

This is that check, written down. Names only -- it compares which members exist at each
hop, not what they mean, because a name is what BSON keys a saved field by and a name is
what a hand-written mapper forgets.

Usage: scripts/validate-farm-fold.py [--verbose]
Exit:  0 the chain is complete; 1 otherwise.
"""

import re
import subprocess
import sys
from pathlib import Path

# A member may legitimately change name or leave the chain, but only on purpose and only
# with the reason recorded here. Anything else is a hole.
RENAMED = {
    # on FoldedFarmArea and SurveyAreaEntry the legacy row's Id is the AREA's id, which the
    # fold mints fresh -- the legacy value survives separately as the fold marker.
    "Id": "AreaId",
}

NOT_CARRIED = {
    # An assignment is not a member on the area side. It crosses as a claim, written by
    # MigrateLegacyFarmAreas through RecordClaim with the farming work value, because that
    # is how every other assignment on an area is recorded.
    "Assigned": "crosses as a claim (RecordClaim, ClaimWorkFarming), not as a member",
}


def repo_root() -> Path:
    out = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=False
    )
    if out.returncode != 0:
        sys.exit("validate-farm-fold: not inside a git repository")
    return Path(out.stdout.strip())


def read(root: Path, rel: str) -> str:
    path = root / rel
    if not path.is_file():
        sys.exit(f"validate-farm-fold: missing {rel}")
    return path.read_text(encoding="utf-8", errors="replace")


def class_body(source: str, name: str, rel: str) -> str:
    """The text of one class, from its declaration to the brace that closes it."""
    match = re.search(rf"\bclass\s+{re.escape(name)}\b", source)
    if not match:
        sys.exit(f"validate-farm-fold: no class {name} in {rel}")
    depth, start = 0, source.index("{", match.end())
    for i in range(start, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[start : i + 1]
    sys.exit(f"validate-farm-fold: class {name} in {rel} is not closed")


def method_body(source: str, signature: str, rel: str) -> str:
    """One method's body, whether it is a brace block or an expression body.

    Both forms are in play here and they cannot be told apart by the signature: a brace
    body ends at its matching `}`, an expression body (`=> ...;`) at the first `;` outside
    any bracket. Hunting for `{` alone reads an expression-bodied method's NEXT sibling and
    reports whatever that one happens to mention -- which is a false pass or a false fail
    depending on the sibling, and the first version of this script did exactly that.
    """
    match = re.search(re.escape(signature), source)
    if not match:
        sys.exit(f"validate-farm-fold: no {signature} in {rel}")

    # Walk past the parameter list to whichever body form follows it.
    i, depth = source.index("(", match.end()), 0
    while i < len(source):
        if source[i] == "(":
            depth += 1
        elif source[i] == ")":
            depth -= 1
            if depth == 0:
                break
        i += 1
    rest = source[i + 1 :]

    arrow = re.match(r"\s*=>", rest)
    if arrow:
        start, depth = arrow.end(), 0
        for j in range(start, len(rest)):
            ch = rest[j]
            if ch in "([{":
                depth += 1
            elif ch in ")]}":
                depth -= 1
            elif ch == ";" and depth == 0:
                return rest[start : j + 1]
        sys.exit(f"validate-farm-fold: expression body of {signature} in {rel} is not closed")

    start, depth = rest.index("{"), 0
    for j in range(start, len(rest)):
        if rest[j] == "{":
            depth += 1
        elif rest[j] == "}":
            depth -= 1
            if depth == 0:
                return rest[start : j + 1]
    sys.exit(f"validate-farm-fold: {signature} in {rel} is not closed")


def main() -> int:
    verbose = "--verbose" in sys.argv[1:]
    root = repo_root()

    farming = read(root, "EcoServerMod/AdvancedElectronics/DroneDock.Farming.cs")
    legacy = read(root, "EcoServerMod/AdvancedElectronics.Navigation/LegacyFarmAreas.cs")
    migration = read(root, "EcoServerMod/AdvancedElectronics/DroneDock.Migration.cs")

    entry = class_body(farming, "FarmAreaEntry", "DroneDock.Farming.cs")
    row = class_body(legacy, "LegacyFarmRow", "LegacyFarmAreas.cs")
    folded = class_body(legacy, "FoldedFarmArea", "LegacyFarmAreas.cs")
    to_row = method_body(migration, "LegacyFarmRow ToLegacyFarmRow", "DroneDock.Migration.cs")
    to_area = method_body(migration, "SurveyAreaEntry ToAreaEntry", "DroneDock.Migration.cs")

    saved = re.findall(r"\[Serialized\]\s+public\s+\S+\s+(\w+)", entry)
    if not saved:
        sys.exit("validate-farm-fold: found no [Serialized] members on FarmAreaEntry -- "
                 "the check would pass vacuously")

    row_members = set(re.findall(r"public\s+\S+\s+(\w+)\s*\{\s*get", row))
    folded_members = set(re.findall(r"public\s+\S+\s+(\w+)\s*\{\s*get", folded))

    problems = []

    def fail(msg: str) -> None:
        problems.append(msg)
        print(f"  FAIL  {msg}")

    for member in saved:
        carried = RENAMED.get(member, member)

        # Hop 1: FarmAreaEntry -> LegacyFarmRow. This is the direction nothing else guards.
        if member not in row_members:
            fail(f"{member} is [Serialized] on FarmAreaEntry but absent from LegacyFarmRow "
                 f"-- it would be dropped from every folded farm")
            continue

        # Hop 2: the read side has to actually pass it.
        if not re.search(rf"\b{re.escape(member)}\b", to_row):
            fail(f"{member} exists on LegacyFarmRow but ToLegacyFarmRow never reads it")

        # Hop 3: LegacyFarmRow -> FoldedFarmArea.
        if carried not in folded_members:
            fail(f"{member} is carried by LegacyFarmRow but absent from FoldedFarmArea "
                 f"(expected {carried})")
            continue

        # Hop 4: the write side, unless this member deliberately leaves the chain.
        if member in NOT_CARRIED:
            if verbose:
                print(f"        {member} -- not a member on the area: {NOT_CARRIED[member]}")
            continue
        if not re.search(rf"\b{re.escape(carried)}\b", to_area):
            fail(f"{member} reaches FoldedFarmArea as {carried} but ToAreaEntry never writes it")
        elif verbose:
            arrow = f" -> {carried}" if carried != member else ""
            print(f"        {member}{arrow}")

    # The no-defaults discipline is the one mechanical guard that already exists. If it is
    # ever softened, the compile error that catches the mirrored case disappears too.
    ctor = re.search(r"public\s+LegacyFarmRow\s*\(([^)]*)\)", legacy, re.S)
    if not ctor:
        fail("no LegacyFarmRow constructor found -- the no-defaults guard cannot be checked")
    elif "=" in ctor.group(1):
        fail("LegacyFarmRow's constructor has a defaulted parameter. Defaulting one turns the "
             "compile error that catches a mirrored member into a folded farm missing a value.")

    if problems:
        print(f"FAIL: {len(problems)} farm-fold problem(s).")
        print("      A member added to FarmAreaEntry must be mirrored onto LegacyFarmRow and")
        print("      FoldedFarmArea, read in ToLegacyFarmRow and written in ToAreaEntry -- or")
        print("      declared in this script with the reason it leaves the chain.")
        return 1

    print(f"PASS: all {len(saved)} of FarmAreaEntry's serialized members make the crossing "
          f"({len(NOT_CARRIED)} declared as claims rather than members).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
