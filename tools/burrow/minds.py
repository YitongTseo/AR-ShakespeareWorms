#!/usr/bin/env python3
"""Select the minds that become nodes: the best poet of every generation in both flasks.

Stratified per generation (not a global top-N) so the sample spans the whole run; raw
fitness climbs through the run, a global top-200 would all be late generations.

Where the generation's winner has no saved weights.json (a handful of early elite
carry-forwards), the best-ranked worm of that generation that does have one is used.

Writes build/burrow/minds.json and build/burrow/genomes.npy (one flattened synapse vector
per mind, in the same order; sorted (pre, post) key order like tools/grow_tree.py).
"""
import json
import sys
from pathlib import Path

import numpy as np

HAMLET = Path("/Users/yitong/Documents/GitHub/HamletRNAWorld/v6/data/generations")
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "build" / "burrow"
FLASKS = ["flask_1", "flask_2"]


def flatten_weights(path, keys=None):
    d = json.loads(path.read_text())
    if keys is None:
        keys = [(s, t) for s in sorted(d) for t in sorted(d[s])]
    return np.array([d[s].get(t, 0.0) for (s, t) in keys], dtype=np.float64), keys


def select():
    minds, keys = [], None
    for fi, flask in enumerate(FLASKS):
        for gen_dir in sorted((HAMLET / flask).glob("gen-*")):
            gen = int(gen_dir.name.split("-")[1])
            meta = json.loads((gen_dir / "metadata.json").read_text())
            ranks = meta["ranks"]
            chosen = None
            for rank, name in enumerate(ranks):
                if (gen_dir / name / "weights.json").exists():
                    chosen = (rank, name)
                    break
            if chosen is None:
                print(f"skip {flask} gen {gen}: no weights at all", file=sys.stderr)
                continue
            rank, name = chosen
            fit = json.loads((gen_dir / name / "fitness.json").read_text())
            poem = (gen_dir / name / "poem_clean.txt").read_text().strip()
            minds.append(dict(
                id=len(minds), name=name, flask=fi, gen=gen, rank=rank,
                fitness=float(fit.get("fitness", 0.0)), best_score=float(meta.get("best_score", 0.0)),
                sigma=float(meta.get("sigma_used", 0.0)),
                weights=str(gen_dir / name / "weights.json"), poem=poem,
            ))
    vecs = []
    for m in minds:
        v, keys = flatten_weights(Path(m["weights"]), keys)
        vecs.append(v)
    G = np.stack(vecs)
    # mutation magnitude from the previous mind in the same flask (genome distance)
    prev = {}
    for m in minds:
        p = prev.get(m["flask"])
        m["mut"] = float(np.linalg.norm(G[m["id"]] - G[p])) if p is not None else 0.0
        prev[m["flask"]] = m["id"]
    return minds, G, keys


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    minds, G, keys = select()
    (OUT / "minds.json").write_text(json.dumps(dict(
        count=len(minds), synapses=len(keys), flasks=FLASKS, minds=minds), indent=1))
    np.save(OUT / "genomes.npy", G)
    gens = {}
    for m in minds:
        gens.setdefault(m["flask"], []).append(m["gen"])
    print(f"{len(minds)} minds, genome dim {G.shape[1]}")
    for f, g in gens.items():
        print(f"  {FLASKS[f]}: gens {min(g)}..{max(g)} ({len(g)}), "
              f"fitness {min(m['fitness'] for m in minds if m['flask']==f):.1f}.."
              f"{max(m['fitness'] for m in minds if m['flask']==f):.1f}")
    rk = [m["rank"] for m in minds if m["rank"] > 0]
    print(f"  {len(rk)} minds are fallbacks (winner had no weights), ranks {rk}")


if __name__ == "__main__":
    main()
