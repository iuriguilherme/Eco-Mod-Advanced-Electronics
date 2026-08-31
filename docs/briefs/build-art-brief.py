#!/usr/bin/env python3
"""Inline the reference icons into the art brief so it can be published standalone.

The tracked brief carries `__ICON_*__` placeholders rather than the image bytes, so the
document stays diffable and does not duplicate art that already lives in the repo. This
script swaps each placeholder for a data URI and writes a self-contained page.

    python docs/briefs/build-art-brief.py <output.html>

Run it from the repository root. The output path is required and is deliberately not
defaulted into `dist/`, which holds shipped release archives.
"""

import base64
import sys
from pathlib import Path

BRIEF = Path("docs/briefs/2026-08-31-icon-art-brief.html")
ICONS = Path("Assets/Art/AdvancedElectronics/Sprites/Icons")

SUBSTITUTIONS = {
    "__ICON_SURVEY__": "SurveyDroneItem_icon.png",
    "__ICON_MINING__": "MiningDroneItem_icon.png",
    "__ICON_HARVEST__": "HarvestDroneItem_icon.png",
}


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: python docs/briefs/build-art-brief.py <output.html>", file=sys.stderr)
        return 2
    out = Path(sys.argv[1])

    if not BRIEF.is_file():
        print(f"missing {BRIEF} - run this from the repository root", file=sys.stderr)
        return 1

    html = BRIEF.read_text(encoding="utf-8")

    for token, filename in SUBSTITUTIONS.items():
        icon = ICONS / filename
        if not icon.is_file():
            print(f"missing icon {icon}", file=sys.stderr)
            return 1
        if html.count(token) != 1:
            print(f"expected exactly one {token} in the brief", file=sys.stderr)
            return 1
        encoded = base64.b64encode(icon.read_bytes()).decode("ascii")
        html = html.replace(token, f"data:image/png;base64,{encoded}")

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(html, encoding="utf-8", newline="\n")
    print(f"wrote {out} ({len(html)} chars)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
