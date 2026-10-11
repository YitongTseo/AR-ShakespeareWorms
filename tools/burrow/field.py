#!/usr/bin/env python3
"""Signed distance field of the burrow network -> marching cubes -> decimated mesh with
per-vertex spine attributes (what the Unity motion shader needs).

Sculptural rules (from the XPS worm-mount takes):
  * calibre constant along a run, stepping at junctions (set upstream in network.calibre)
  * swollen, flattened turning chambers at junctions (ellipsoid in the branch plane)
  * bulbous blind ends, never tapering to a point
  * a nodule swelling at every mind, where its glass dome sits
  * a rough bioturbation skin (low amplitude; the wire-wrap detail is a shader normal map)

    python3 field.py build/burrow/network_7.json [vox=0.015] [faces=240000]
writes <network>_mesh.npz  (verts, normals, faces, attrs) and <network>_mesh.ply (preview)
"""
import json
import math
import sys
import time
from pathlib import Path

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree
from skimage.measure import marching_cubes


def unit(v):
    v = np.asarray(v, float); n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def transport_frames(Q):
    """parallel-transport frames along polyline Q -> T, N, B arrays."""
    T = np.gradient(Q, axis=0)
    T /= np.linalg.norm(T, axis=1, keepdims=True) + 1e-12
    N = np.zeros_like(Q)
    ref = np.array([0, 1, 0.0]) if abs(T[0] @ [0, 1, 0]) < 0.9 else np.array([1, 0, 0.0])
    N[0] = unit(np.cross(np.cross(T[0], ref), T[0]))
    for i in range(1, len(Q)):
        n = N[i - 1] - (N[i - 1] @ T[i]) * T[i]
        N[i] = unit(n) if np.linalg.norm(n) > 1e-9 else N[i - 1]
    B = np.cross(T, N)
    return T, N, B


class Spine:
    """all path samples with their run id, arc length, radius and frame."""

    def __init__(self, net):
        P, E, S, R, Nn, Bb, Tt = [], [], [], [], [], [], []
        self.edges = net["edges"]
        for ei, e in enumerate(net["edges"]):
            Q = np.asarray(e["path"], float)
            seg = np.linalg.norm(np.diff(Q, axis=0), axis=1)
            s = np.concatenate([[0], np.cumsum(seg)])
            T, N, B = transport_frames(Q)
            P.append(Q); E.append(np.full(len(Q), ei)); S.append(s); R.append(np.full(len(Q), e["r"]))
            Nn.append(N); Bb.append(B); Tt.append(T)
        self.P = np.vstack(P); self.E = np.concatenate(E); self.S = np.concatenate(S)
        self.R = np.concatenate(R); self.N = np.vstack(Nn); self.B = np.vstack(Bb); self.T = np.vstack(Tt)
        self.tree = cKDTree(self.P)
        # a denser copy for per-vertex attributes (projected arc length, stable frames)
        Pd, Ed, Sd, Rd, Nd, Bd, Td = [], [], [], [], [], [], []
        for ei, e in enumerate(net["edges"]):
            Q = np.asarray(e["path"], float)
            seg = np.linalg.norm(np.diff(Q, axis=0), axis=1)
            s = np.concatenate([[0], np.cumsum(seg)])
            n = max(int(s[-1] / 0.005), 2)
            si = np.linspace(0, s[-1], n)
            Qd = np.stack([np.interp(si, s, Q[:, j]) for j in range(3)], 1)
            T, N, B = transport_frames(Qd)
            Pd.append(Qd); Ed.append(np.full(n, ei)); Sd.append(si); Rd.append(np.full(n, e["r"]))
            Nd.append(N); Bd.append(B); Td.append(T)
        self.Pd = np.vstack(Pd); self.Ed = np.concatenate(Ed); self.Sd = np.concatenate(Sd)
        self.Rd = np.concatenate(Rd); self.Nd = np.vstack(Nd); self.Bd = np.vstack(Bd); self.Td = np.vstack(Td)
        self.dtree = cKDTree(self.Pd)
        self.nodes = net["nodes"]
        self.node_p = np.stack([n["p"] for n in self.nodes])
        # incident radii / directions per node
        inc = {}
        for ei, e in enumerate(net["edges"]):
            Q = np.asarray(e["path"])
            inc.setdefault(e["a"], []).append((e["r"], unit(Q[min(6, len(Q) - 1)] - Q[0])))
            inc.setdefault(e["b"], []).append((e["r"], unit(Q[max(-7, -len(Q))] - Q[-1])))
        self.inc = inc
        # swellings
        self.chambers, self.bulbs, self.nodules = [], [], []
        for n in self.nodes:
            k = n["id"]; lst = inc.get(k, [])
            if not lst: continue
            rmax = max(r for r, _ in lst)
            c = np.asarray(n["p"])
            if n["kind"] in ("spur", "sculpture") or (n["kind"] == "mind" and len(lst) == 1 and False):
                self.bulbs.append((c, rmax * 1.3))
            if n["kind"] == "mind":
                self.nodules.append((c, n.get("nod_r", max(1.25 * rmax, 0.046)), np.asarray(n["face"])))
            if len(lst) >= 3 or n["kind"] == "root":
                acc = np.zeros((3, 3))
                for _, d in lst: acc += np.outer(d, d)
                w, V = np.linalg.eigh(acc)
                nrm, u = V[:, 0], V[:, 2]
                B = np.stack([u, np.cross(nrm, u), nrm], 1)
                r = rmax * 1.55
                self.chambers.append((c, B, np.array([r * 1.2, r * 1.05, r * 0.78])))
        self.noise = self._tile_noise(11)

    @staticmethod
    def _tile_noise(seed, n=96, sig=(5.0, 1.6)):
        rng = np.random.default_rng(seed); out = []
        for s in sig:
            a = ndimage.gaussian_filter(rng.standard_normal((n, n, n)).astype(np.float32), s, mode="wrap")
            out.append(a / a.std())
        return out

    @staticmethod
    def _ellipsoid(P, c, B, ax):
        L = (P - c) @ B
        k0 = np.sqrt(((L / ax) ** 2).sum(1)); k1 = np.sqrt(((L / ax ** 2) ** 2).sum(1))
        return k0 * (k0 - 1) / np.maximum(k1, 1e-6)

    def tubes(self, P, k=8):
        """min over nearby samples of (dist - r), smoothly unioned across distinct runs."""
        d, i = self.tree.query(P, k=k, distance_upper_bound=0.5, workers=-1)
        bad = ~np.isfinite(d)
        i = np.where(bad, 0, i); d = np.where(bad, 9.0, d)
        f = d - self.R[i]
        f[bad] = 9.0
        e = np.where(bad, -1, self.E[i])
        # per point: best per distinct run, smooth-min across the best 3 runs
        order = np.argsort(f, axis=1)
        f = np.take_along_axis(f, order, 1); e = np.take_along_axis(e, order, 1)
        out = f[:, 0].copy()
        used1 = e[:, 0]
        # second distinct run
        m2 = (e != used1[:, None]) & (e >= 0)
        has2 = m2.any(1)
        f2 = np.where(m2, f, 9.0).min(1)
        out = np.where(has2, smin(out, f2, 0.035), out)
        return out

    def field(self, P, noise=True):
        F = self.tubes(P)
        for c, r in self.bulbs:
            m = np.linalg.norm(P - c, axis=1) < r + 0.08
            if m.any(): F[m] = smin(F[m], np.linalg.norm(P[m] - c, axis=1) - r, 0.02)
        for c, r, face in self.nodules:
            m = np.linalg.norm(P - c, axis=1) < r + 0.1
            if m.any(): F[m] = smin(F[m], np.linalg.norm(P[m] - c, axis=1) - r, 0.03)
        for c, B, ax in self.chambers:
            m = np.linalg.norm(P - c, axis=1) < ax.max() + 0.1
            if m.any(): F[m] = smin(F[m], self._ellipsoid(P[m], c, B, ax), 0.04)
        if noise:
            n1 = ndimage.map_coordinates(self.noise[0], ((P / 0.011) % 96.0).T, order=1, mode="grid-wrap")
            n2 = ndimage.map_coordinates(self.noise[1], ((P / 0.0045) % 96.0).T, order=1, mode="grid-wrap")
            F = F + 0.0055 * n1 + 0.0016 * n2
        return F

    def attributes(self, V):
        """per-vertex: run id, s (m, upstream->downstream), r, phi (rad around the tube),
        node proximity (0 far .. 1 at a node), tip weight (spurs: 0 base .. 1 tip)."""
        d, i = self.dtree.query(V, workers=-1)
        rel = V - self.Pd[i]
        phi = np.arctan2(np.einsum("ij,ij->i", rel, self.Bd[i]), np.einsum("ij,ij->i", rel, self.Nd[i]))
        run = self.Ed[i]; s = self.Sd[i] + np.einsum("ij,ij->i", rel, self.Td[i]); r = self.Rd[i]
        dn, _ = cKDTree(self.node_p).query(V, workers=-1)
        prox = np.clip(1 - dn / 0.14, 0, 1)
        tip = np.zeros(len(V))
        for ei, e in enumerate(self.edges):
            if e["kind"] in ("spur",):
                m = run == ei
                tip[m] = np.clip(s[m] / max(e["len"], 1e-3), 0, 1)
        return dict(run=run.astype(np.int32), s=s.astype(np.float32), r=r.astype(np.float32),
                    phi=phi.astype(np.float32), prox=prox.astype(np.float32), tip=tip.astype(np.float32))


def mesh(net, vox=0.015, target_faces=240000, verbose=True):
    t0 = time.time()
    sp = Spine(net)
    lo = sp.P.min(0) - 0.2; hi = sp.P.max(0) + 0.2
    shape = np.ceil((hi - lo) / vox).astype(int) + 1
    if verbose: print(f"grid {shape.tolist()} = {np.prod(shape)/1e6:.1f} M voxels @ {vox*1000:.0f} mm")
    # coarse pass: plain nearest distance everywhere, then the full field only in the band
    gi = np.indices(shape).reshape(3, -1).T.astype(np.float32)
    Pts = lo + gi * vox
    F = np.full(len(Pts), 1.0, np.float32)
    B = 4_000_000
    for s0 in range(0, len(Pts), B):
        d, i = sp.tree.query(Pts[s0:s0 + B], workers=-1)
        F[s0:s0 + B] = d - sp.R[i]
    band = (F < 0.09) | (F > -0.09) & (F < 0.0)
    band = F < 0.11
    idx = np.nonzero(band)[0]
    if verbose: print(f"coarse {time.time()-t0:.1f}s, band {len(idx)/1e6:.2f} M voxels")
    for s0 in range(0, len(idx), 1_000_000):
        sl = idx[s0:s0 + 1_000_000]
        F[sl] = sp.field(Pts[sl])
    F = F.reshape(shape)
    if verbose: print(f"field {time.time()-t0:.1f}s")
    V, Fc, Nrm, _ = marching_cubes(F, level=0.0, spacing=(vox, vox, vox), allow_degenerate=False)
    V = V + lo
    if verbose: print(f"marching cubes {time.time()-t0:.1f}s: {len(V)} verts, {len(Fc)} faces")
    # keep the biggest connected component(s): drop floating specks
    import trimesh
    tm = trimesh.Trimesh(V, Fc, process=False)
    comps = tm.split(only_watertight=False)
    comps = sorted(comps, key=lambda c: -len(c.faces))
    keep = [c for c in comps if len(c.faces) > 0.002 * len(Fc)]
    tm = trimesh.util.concatenate(keep) if len(keep) > 1 else keep[0]
    if verbose: print(f"components kept {len(keep)}/{len(comps)}: {len(tm.faces)} faces")
    if len(tm.faces) > target_faces:
        import fast_simplification
        V2, F2 = fast_simplification.simplify(tm.vertices.astype(np.float32), tm.faces.astype(np.int32),
                                              target_reduction=1 - target_faces / len(tm.faces))
        tm = trimesh.Trimesh(V2, F2, process=True)
        if verbose: print(f"decimated {time.time()-t0:.1f}s: {len(tm.vertices)} verts, {len(tm.faces)} faces")
    tm.fix_normals()
    V = np.asarray(tm.vertices, np.float32); Fc = np.asarray(tm.faces, np.int32)
    Nrm = np.asarray(tm.vertex_normals, np.float32)
    attrs = sp.attributes(V.astype(float))
    if verbose: print(f"attributes {time.time()-t0:.1f}s")
    return dict(verts=V, normals=Nrm, faces=Fc, **attrs)


def write_ply(path, m):
    """PLY with (s, phi) as UVs so Blender can drive a wire-wrap bump the way the Unity shader will."""
    import trimesh
    uv = np.stack([m["s"], (m["phi"] / (2 * np.pi)) % 1.0], 1).astype(np.float32)
    # cos/sin of phi as vertex colour so the seam interpolates correctly (atan2 in the shader)
    col = np.stack([np.cos(m["phi"]) * 0.5 + 0.5, np.sin(m["phi"]) * 0.5 + 0.5, m["prox"], np.ones(len(m["phi"]))], 1)
    tm = trimesh.Trimesh(m["verts"], m["faces"], vertex_normals=m["normals"], process=False)
    tm.visual = trimesh.visual.TextureVisuals(uv=uv)
    tm.visual.vertex_attributes["color"] = (col * 255).astype(np.uint8)
    tm.export(path)


def main():
    args = dict(a.split("=") for a in sys.argv[2:])
    src = Path(sys.argv[1])
    net = json.loads(src.read_text())
    m = mesh(net, vox=float(args.get("vox", 0.015)), target_faces=int(args.get("faces", 240000)))
    out = src.with_name(src.stem + "_mesh.npz")
    np.savez_compressed(out, **m)
    write_ply(src.with_name(src.stem + "_mesh.ply"), m)
    print("wrote", out)


if __name__ == "__main__":
    main()
