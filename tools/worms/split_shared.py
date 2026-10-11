#!/usr/bin/env python3
"""Split the per-mind .hwrm assets into one shared.hwrm (VOCAB, ETABLE, NET, CORPUS: identical
across minds, ~1 MB) and tiny per-mind files holding only the connectome sections (~47 KB).
WmAsset.Merge() on the Unity side rebuilds a full asset from the pair.

    python3 tools/worms/split_shared.py   (in place on Assets/StreamingAssets/worms)
"""
import hashlib
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORMS = ROOT / "Assets" / "StreamingAssets" / "worms"
SHARED_TAGS = {"VOCAB", "ETABLE", "NET", "CORPUS"}
MAGIC = b"HWRM"


def read_sections(d):
    assert d[:4] == MAGIC, "bad magic"
    version, n, _ = struct.unpack_from("<III", d, 4)
    assert version == 1
    out = []
    for i in range(n):
        tag = d[16 + 16 * i:16 + 16 * i + 8].decode("ascii").rstrip()
        off, size = struct.unpack_from("<II", d, 16 + 16 * i + 8)
        out.append((tag, bytes(d[off:off + size])))
    return out


def pack(sections):
    head = bytearray(MAGIC + struct.pack("<III", 1, len(sections), 0))
    toc_size = 16 * len(sections)
    body_start = (len(head) + toc_size + 15) // 16 * 16
    toc, payload, cur = bytearray(), bytearray(), body_start
    for tag, data in sections:
        toc += tag.encode("ascii").ljust(8, b" ") + struct.pack("<II", cur, len(data))
        pad = (-len(data)) % 16
        payload += data + b"\0" * pad
        cur += len(data) + pad
    out = bytearray(head + toc)
    out += b"\0" * (body_start - len(out))
    return bytes(out + payload)


def main():
    files = sorted(WORMS.glob("mind_*.hwrm"), key=lambda p: int(p.stem.split("_")[1]))
    if not files:
        sys.exit("no mind_*.hwrm files")
    shared = None
    total = 0
    for f in files:
        secs = read_sections(f.read_bytes())
        mine = [(t, b) for t, b in secs if t not in SHARED_TAGS]
        sh = [(t, b) for t, b in secs if t in SHARED_TAGS]
        if not sh:   # already split
            total += f.stat().st_size
            continue
        if shared is None:
            shared = sh
        else:
            assert [(t, hashlib.md5(b).digest()) for t, b in sh] == [(t, hashlib.md5(b).digest()) for t, b in shared], f"{f} shared tables differ"
        f.write_bytes(pack(mine))
        total += f.stat().st_size
    if shared is not None:
        (WORMS / "shared.hwrm").write_bytes(pack(shared))
    idx_p = WORMS / "index.json"
    idx = json.loads(idx_p.read_text())
    for m in idx["minds"]:
        m["bytes"] = (WORMS / m["file"]).stat().st_size
    idx["shared"] = "shared.hwrm"
    idx["total_bytes"] = total + (WORMS / "shared.hwrm").stat().st_size
    idx_p.write_text(json.dumps(idx, indent=1))
    print(f"{len(files)} minds: shared {(WORMS / 'shared.hwrm').stat().st_size/1e6:.2f} MB + "
          f"per mind ~{total/len(files)/1e3:.0f} KB = {idx['total_bytes']/1e6:.1f} MB total")


if __name__ == "__main__":
    main()
