#!/usr/bin/env python3
"""Report what a Unity asset bundle actually contains, without opening Unity.

    scripts/read-mod-bundle.py <bundle.unity3d> [...] [--strings <dir>]

Answers "did this name reach the bundle?" for our own builds, and "what does this mod ship?"
for the reference mods under `.references/Mods/`. With --strings it also writes each bundle's
readable strings to a file for grepping.

Reads UnityFS containers with a pure-Python LZ4 block decompressor, so it needs no packages
beyond the standard library. It deliberately stops short of parsing the SerializedFile type
tree: the node table plus the readable strings answer the questions we actually ask, and a
full parser would be a dependency and a maintenance burden for no extra answer.
"""

import re
import struct
import sys
from pathlib import Path


def lz4_decompress(src: bytes, expected: int) -> bytes:
    """LZ4 block format. See github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md"""
    out = bytearray()
    i = 0
    n = len(src)
    while i < n:
        token = src[i]
        i += 1

        lit = token >> 4
        if lit == 15:
            while True:
                b = src[i]
                i += 1
                lit += b
                if b != 255:
                    break
        out += src[i:i + lit]
        i += lit

        if i >= n:
            break

        offset = src[i] | (src[i + 1] << 8)
        i += 2
        if offset == 0:
            raise ValueError("zero match offset")

        match = token & 0x0F
        if match == 15:
            while True:
                b = src[i]
                i += 1
                match += b
                if b != 255:
                    break
        match += 4

        start = len(out) - offset
        if start < 0:
            raise ValueError("match before start of output")
        for k in range(match):
            out.append(out[start + k])

    if expected and len(out) != expected:
        raise ValueError(f"expected {expected} bytes, produced {len(out)}")
    return bytes(out)


class Reader:
    def __init__(self, data: bytes, pos: int = 0):
        self.d = data
        self.p = pos

    def cstr(self) -> str:
        end = self.d.index(b"\x00", self.p)
        s = self.d[self.p:end].decode("utf-8", "replace")
        self.p = end + 1
        return s

    def u32(self) -> int:
        v = struct.unpack_from(">I", self.d, self.p)[0]
        self.p += 4
        return v

    def u16(self) -> int:
        v = struct.unpack_from(">H", self.d, self.p)[0]
        self.p += 2
        return v

    def i64(self) -> int:
        v = struct.unpack_from(">q", self.d, self.p)[0]
        self.p += 8
        return v

    def take(self, n: int) -> bytes:
        v = self.d[self.p:self.p + n]
        self.p += n
        return v


def read_bundle(path: Path) -> dict:
    raw = path.read_bytes()
    r = Reader(raw)

    signature = r.cstr()
    if signature != "UnityFS":
        raise ValueError(f"not a UnityFS bundle: {signature!r}")

    version = r.u32()
    unity_version = r.cstr()
    unity_revision = r.cstr()
    r.i64()                                   # total size
    compressed_info = r.u32()
    uncompressed_info = r.u32()
    flags = r.u32()

    if version >= 7:
        r.p = (r.p + 15) & ~15

    if flags & 0x80:                          # blocks info stored at the end
        info_raw = raw[-compressed_info:]
        data_start = r.p
    else:
        info_raw = r.take(compressed_info)
        data_start = r.p

    if flags & 0x200:                         # blockInfoNeedPaddingAtStart
        data_start = (data_start + 15) & ~15

    compression = flags & 0x3F
    if compression == 0:
        info = info_raw
    elif compression in (2, 3):
        info = lz4_decompress(info_raw, uncompressed_info)
    else:
        raise ValueError(f"unsupported blocksInfo compression {compression}")

    ir = Reader(info)
    ir.take(16)                               # uncompressed data hash
    blocks = []
    for _ in range(ir.u32()):
        blocks.append((ir.u32(), ir.u32(), ir.u16()))   # uncompressed, compressed, flags

    nodes = []
    for _ in range(ir.u32()):
        offset = ir.i64()
        size = ir.i64()
        node_flags = ir.u32()
        nodes.append((offset, size, node_flags, ir.cstr()))

    blob = bytearray()
    p = data_start
    for uncompressed_size, compressed_size, block_flags in blocks:
        chunk = raw[p:p + compressed_size]
        p += compressed_size
        c = block_flags & 0x3F
        if c == 0:
            blob += chunk
        elif c in (2, 3):
            blob += lz4_decompress(chunk, uncompressed_size)
        else:
            raise ValueError(f"unsupported block compression {c}")

    return {
        "version": version,
        "unity": f"{unity_version} {unity_revision}",
        "nodes": nodes,
        "data": bytes(blob),
    }


STRING = re.compile(rb"[ -~]{4,}")

USAGE = "usage: scripts/read-mod-bundle.py <bundle.unity3d> [...] [--strings <dir>]"


def main() -> int:
    args = sys.argv[1:]

    strings_dir = None
    if "--strings" in args:
        i = args.index("--strings")
        if i + 1 >= len(args):
            print("--strings needs a directory", file=sys.stderr)
            return 2
        strings_dir = Path(args[i + 1])
        strings_dir.mkdir(parents=True, exist_ok=True)
        del args[i:i + 2]

    if not args:
        print(USAGE, file=sys.stderr)
        return 2

    failures = 0
    for arg in args:
        path = Path(arg)
        print(f"\n===== {path.name} =====")
        try:
            bundle = read_bundle(path)
        except Exception as exc:                       # noqa: BLE001 - reporting tool
            print(f"  FAILED: {exc}")
            failures += 1
            continue

        print(f"  unity {bundle['unity']}  |  {len(bundle['data']):,} bytes decompressed")
        for offset, size, _flags, name in bundle["nodes"]:
            print(f"  node  {name}  ({size:,} bytes @ {offset})")

        seen, uniq = set(), []
        for match in STRING.findall(bundle["data"]):
            text = match.decode("ascii")
            if text not in seen:
                seen.add(text)
                uniq.append(text)

        if strings_dir is None:
            print(f"  {len(uniq):,} unique strings (pass --strings <dir> to dump them)")
        else:
            out = strings_dir / (path.stem + ".strings.txt")
            out.write_text("\n".join(uniq), encoding="utf-8")
            print(f"  {len(uniq):,} unique strings -> {out}")

    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
