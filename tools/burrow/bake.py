#!/usr/bin/env python3
"""Bake a network + mesh into what the Unity app loads from StreamingAssets/burrow/.

    python3 bake.py build/burrow/network_7.json [out=Assets/StreamingAssets/burrow]

burrow_mesh.bytes  little-endian:
    char[4] "BRRW", u32 version=1, u32 nVerts, u32 nIndices, u32 nRuns
    f32[nVerts*3] position   (Unity frame, metres, sculpture centre at origin)
    f32[nVerts*3] normal
    f32[nVerts*4] attr0: s (m along run, upstream->downstream), r (m), phi (rad), prox (0..1 near a node)
    f32[nVerts*2] attr1: run id, tip weight (spurs 0..1 toward the tip)
    u32[nIndices] triangle indices
burrow.json  nodes, runs (with coarse flat spines for bolus scheduling), minds (name, flask, gen, fitness)
poems.json   one poem string per mind, same order
"""
import json
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]


def main():
    src = Path(sys.argv[1]).resolve()
    args = dict(a.split("=") for a in sys.argv[2:])
    out = Path(args.get("out", ROOT / "Assets" / "StreamingAssets" / "burrow"))
    out.mkdir(parents=True, exist_ok=True)
    net = json.loads(src.read_text())
    m = np.load(src.with_name(src.stem + "_mesh.npz"))
    minds = json.loads((ROOT / "build" / "burrow" / "minds.json").read_text())["minds"]
    G = np.load(ROOT / "build" / "burrow" / "genomes.npy")
    Gc = G - G.mean(0)
    _, _, Vt = np.linalg.svd(Gc, full_matrices=False)
    pc = Gc @ Vt[:3].T
    pc = pc / (np.abs(pc).max(0) + 1e-9)          # roughly -1..1 per component

    V = m["verts"].astype("<f4"); Nn = m["normals"].astype("<f4"); F = m["faces"].astype("<u4")
    a0 = np.stack([m["s"], m["r"], m["phi"], m["prox"]], 1).astype("<f4")
    a1 = np.stack([m["run"].astype(np.float32), m["tip"]], 1).astype("<f4")
    with open(out / "burrow_mesh.bytes", "wb") as f:
        f.write(b"BRRW"); f.write(struct.pack("<IIII", 1, len(V), F.size, len(net["edges"])))
        f.write(V.tobytes()); f.write(Nn.tobytes()); f.write(a0.tobytes()); f.write(a1.tobytes()); f.write(F.tobytes())

    runs = []
    for i, e in enumerate(net["edges"]):
        Q = np.asarray(e["path"])
        step = max(1, len(Q) // 12)
        spine = np.vstack([Q[::step], Q[-1:]])
        runs.append(dict(id=i, a=e["a"], b=e["b"], kind=e["kind"], r=e["r"], len=e["len"], flow=e["flow"],
                         sp=np.round(spine, 3).ravel().tolist()))      # flat x,y,z,... (JsonUtility)
    manifest = dict(
        version=1, seed=net["seed"], params=net["params"],
        bounds=dict(min=np.round(V.min(0), 3).tolist(), max=np.round(V.max(0), 3).tolist()),
        nodes=net["nodes"], runs=runs,
        minds=[dict(id=md["id"], name=md["name"], flask=md["flask"], gen=md["gen"], fitness=md["fitness"],
                    mut=md["mut"], pc=np.round(pc[md["id"]], 3).tolist()) for md in minds],
    )
    (out / "burrow.json").write_text(json.dumps(manifest, separators=(",", ":")))
    # poems separately (big, loaded lazily by the pop-up)
    (out / "poems.json").write_text(json.dumps(dict(poems=[md["poem"] for md in minds]), separators=(",", ":")))
    sz = (out / "burrow_mesh.bytes").stat().st_size / 1e6
    print(f"baked {len(V)} verts, {len(F)} tris, {len(runs)} runs -> {out}  (mesh {sz:.1f} MB)")


if __name__ == "__main__":
    main()
