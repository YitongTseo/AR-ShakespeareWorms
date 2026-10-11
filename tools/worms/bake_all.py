#!/usr/bin/env python3
"""Bake every mind in build/burrow/minds.json into Assets/StreamingAssets/worms/.

    python3 tools/worms/bake_all.py [--minds build/burrow/minds.json] [--out Assets/StreamingAssets/worms]

Output: mind_<id>.hwrm per mind plus index.json (id, name, flask, gen, fitness,
seed, file, bytes) for the Unity loader. The world seed is derived from the
mind id (see mind_seed) so every mind's life is reproducible from the index
alone and no two minds share a seed.

The shared tables are computed once; per-mind work is just the connectome
sections, so the whole burrow bakes in well under a minute.
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))

from bake_mind import SHARED_TAGS, Shared, bake_mind  # noqa: E402


def mind_seed(mind_id: int) -> int:
    """World seed for a mind: 1000 + id. Small, positive, unique per mind and
    easy to read back from a trace; fits CPython's single-word seed path like
    the v7 seeds (Liam's is 12) do."""
    return 1000 + int(mind_id)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--minds", type=Path, default=REPO / "build" / "burrow" / "minds.json")
    ap.add_argument("--out", type=Path, default=REPO / "Assets" / "StreamingAssets" / "worms")
    ap.add_argument("--passage", default="full")
    ap.add_argument("--embedder-seed", type=int, default=0)
    ap.add_argument("--embedder-genome", type=Path, default=None)
    ap.add_argument("--limit", type=int, default=None, help="bake only the first N minds (smoke test)")
    args = ap.parse_args()

    doc = json.loads(args.minds.read_text())
    minds = doc["minds"] if isinstance(doc, dict) else doc
    if args.limit:
        minds = minds[: args.limit]
    print(f"{len(minds)} minds from {args.minds}")

    t0 = time.time()
    shared = Shared(args.embedder_seed, args.embedder_genome, args.passage)
    print(f"shared tables ready in {time.time() - t0:.1f}s")

    args.out.mkdir(parents=True, exist_ok=True)
    index = []
    total = 0
    shared_bytes = per_mind_bytes = 0
    section_sizes: dict[str, int] = {}
    t1 = time.time()
    for m in minds:
        mid = int(m["id"])
        wpath = Path(m["weights"])
        if not wpath.exists():
            sys.exit(f"mind {mid}: missing weights {wpath}")
        seed = mind_seed(mid)
        extra = {
            "mind_id": mid, "worm": m.get("name"), "flask": m.get("flask"), "gen": m.get("gen"),
            "fitness": m.get("fitness"), "rank": m.get("rank"),
        }
        data, S = bake_mind(wpath, seed, shared, extra, quiet=True)
        fname = f"mind_{mid}.hwrm"
        (args.out / fname).write_bytes(data)
        total += len(data)
        for tag, payload in S:
            section_sizes[tag] = len(payload)
            if tag in SHARED_TAGS:
                shared_bytes += len(payload)
            else:
                per_mind_bytes += len(payload)
        index.append({
            "id": mid, "name": m.get("name"), "flask": m.get("flask"), "gen": m.get("gen"),
            "fitness": m.get("fitness"), "seed": seed, "file": fname, "bytes": len(data),
        })
        print(f"  mind_{mid:<4} {m.get('name', ''):<10} flask={m.get('flask')} gen={m.get('gen'):<4} "
              f"seed={seed} {len(data):>9,} B")

    (args.out / "index.json").write_text(json.dumps({
        "count": len(index), "total_bytes": total, "seed_rule": "1000 + id", "minds": index,
    }, indent=1))

    print()
    print(f"baked {len(index)} minds in {time.time() - t1:.1f}s -> {args.out}")
    print(f"total {total:,} B ({total/1024/1024:.1f} MB), {total/len(index)/1024:.0f} KB per mind")
    print("per-asset section sizes (last mind):")
    for tag, n in section_sizes.items():
        print(f"  {tag:<9} {n:>9,} B{'  shared across minds' if tag in SHARED_TAGS else ''}")
    print(f"shared-table bytes across all files: {shared_bytes:,} B ({100*shared_bytes/max(total,1):.1f}% of total); "
          f"mind-specific: {per_mind_bytes:,} B")


if __name__ == "__main__":
    main()
