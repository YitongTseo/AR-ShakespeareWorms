#!/usr/bin/env python3
"""Funnel layout + graph + organic routing + Murray calibre for the burrow network.

Frame: metres, Unity axes (X right, Y up, Z forward). The sculpture centre is the origin,
its top surface at SCULPT_TOP. The wearer stands near the sculpture and looks up into the
funnel; the newest minds (gen 104) sit at the throat beside the sculpture and the oldest
spread into a blanket across the ceiling (gravity-well profile).

    python3 network.py [seed=7] [ceiling=2.6] [plot=1]
writes build/burrow/network_<seed>.json (+ a matplotlib preview).
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / "build" / "burrow"

# ---------------------------------------------------------------- shape parameters (m)
P = dict(
    ceiling=2.6,          # room ceiling height above the floor
    sculpt_top=0.28,      # top of the sculpture (cushion + wire) above the floor
    throat_y=0.62,        # height of the funnel throat (lowest minds)
    throat_r=0.24,        # funnel radius at the throat
    flare_r=1.6,          # funnel radius where it meets the ceiling blanket
    well_exp=2.3,         # profile exponent: higher = more vertical throat, flatter rim
    blanket_r=2.2,        # outer radius of the ceiling blanket
    blanket_density=0.35,  # node density on the blanket relative to the funnel wall
    blanket_drop=0.18,    # blanket hangs this far below the ceiling
    blanket_sag=0.10,     # ...and sags a further amount toward its rim
    turns=1.35,           # helical drift of each lineage, in turns, throat -> rim
    jitter_theta=0.55,    # rad, genome-driven angular scatter
    jitter_normal=0.09,   # m, scatter off the funnel surface
    min_spacing=0.34,     # m, node relaxation target
    cross_frac=0.6,      # cross-links per mind
    cross_dmax=1.2,      # m, longest cross-link
    spur_frac=0.24,       # blind spurs per mind
    spur_len=(0.22, 0.48),
    r_min=0.02, r_max=0.056,   # tunnel radii, Murray-graded
    path_step=0.02,
)


def unit(v):
    v = np.asarray(v, float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


# ---------------------------------------------------------------- the funnel profile
class Funnel:
    """(rho, y) profile sampled densely; u in [0,1] is normalised arc length throat -> rim."""

    def __init__(self, p):
        self.p = p
        n = 600
        y_top = p["ceiling"] - p["blanket_drop"]
        # gravity well: y(rho) rises steeply at the throat and meets the ceiling tangentially
        rho = np.linspace(p["throat_r"], p["flare_r"], n)
        f = (p["flare_r"] - rho) / (p["flare_r"] - p["throat_r"])
        y = y_top - (y_top - p["throat_y"]) * f ** p["well_exp"]
        # blanket: continue outward on the ceiling with a slight sag
        tb = np.linspace(0, 1, 300)[1:]
        rho_b = p["flare_r"] + (p["blanket_r"] - p["flare_r"]) * tb
        y_b = y_top - p["blanket_sag"] * tb ** 1.5
        self.rho = np.concatenate([rho, rho_b])
        self.y = np.concatenate([y, y_b])
        seg = np.hypot(np.diff(self.rho), np.diff(self.y))
        s = np.concatenate([[0], np.cumsum(seg)])
        self.length = s[-1]
        self.u = s / s[-1]
        dA = 2 * math.pi * 0.5 * (self.rho[1:] + self.rho[:-1]) * seg
        dA[len(rho) - 1:] *= p["blanket_density"]
        self.A = np.concatenate([[0], np.cumsum(dA)])
        self.i_flare = len(rho) - 1

    def at(self, u):
        return np.interp(u, self.u, self.rho), np.interp(u, self.u, self.y)

    def u_from_area_quantile(self, q):
        return np.interp(q * self.A[-1], self.A, self.u)

    def point(self, u, theta):
        rho, y = self.at(u)
        return np.array([rho * math.cos(theta), y, rho * math.sin(theta)])

    def normal(self, u, theta):
        """outward surface normal (away from the funnel axis / up through the blanket)."""
        h = 1e-3
        r0, y0 = self.at(max(u - h, 0)); r1, y1 = self.at(min(u + h, 1))
        t2 = unit([r1 - r0, y1 - y0])            # tangent in the (rho, y) plane
        n2 = np.array([t2[1], -t2[0]])           # rotate: points to +rho when going up
        if n2[0] < 0: n2 = -n2
        return np.array([n2[0] * math.cos(theta), n2[1], n2[0] * math.sin(theta)])

    def project(self, pt):
        rho = math.hypot(pt[0], pt[2]); y = pt[1]
        d = (self.rho - rho) ** 2 + (self.y - y) ** 2
        i = int(np.argmin(d))
        return self.u[i], math.atan2(pt[2], pt[0])


# ---------------------------------------------------------------- placement
def place_minds(minds, G, F, p, rng):
    N = len(minds)
    order = sorted(range(N), key=lambda i: (-minds[i]["gen"], minds[i]["flask"]))   # newest first
    # genome scatter: 2 principal axes of the genomes, normalised
    Gc = G - G.mean(0)
    U_, S_, Vt = np.linalg.svd(Gc, full_matrices=False)
    pc = Gc @ Vt[:2].T
    pc = pc / (np.abs(pc).max(0) + 1e-9)
    u = np.zeros(N); th = np.zeros(N); off = np.zeros(N)
    for k, i in enumerate(order):
        q = (k + 0.5) / N
        u[i] = F.u_from_area_quantile(q)
        base = minds[i]["flask"] * math.pi
        th[i] = base + 2 * math.pi * p["turns"] * u[i] + p["jitter_theta"] * pc[i, 0] + rng.normal(0, 0.12)
        off[i] = p["jitter_normal"] * pc[i, 1] + rng.normal(0, 0.03)
    pos = np.stack([F.point(u[i], th[i]) + F.normal(u[i], th[i]) * off[i] for i in range(N)])
    # relax for spacing: tangential repulsion, then re-project onto the (offset) surface
    for it in range(60):
        tree = cKDTree(pos)
        pairs = tree.query_pairs(p["min_spacing"])
        if not pairs: break
        disp = np.zeros_like(pos)
        for a, b in pairs:
            d = pos[b] - pos[a]; L = np.linalg.norm(d) + 1e-9
            push = (p["min_spacing"] - L) * 0.5 * d / L
            disp[a] -= push; disp[b] += push
        pos += disp * 0.6
        for i in range(N):
            ui, ti = F.project(pos[i])
            # keep the generation ordering loosely: clamp u drift
            u[i] = 0.75 * ui + 0.25 * u[i]
            th[i] = ti
            pos[i] = F.point(u[i], th[i]) + F.normal(u[i], th[i]) * off[i]
    return pos, u, th, off


# ---------------------------------------------------------------- graph
def seg_dist(p1, q1, p2, q2):
    """min distance between segments p1-q1 and p2-q2."""
    d1, d2, r = q1 - p1, q2 - p2, p1 - p2
    a, e, f = d1 @ d1, d2 @ d2, d2 @ r
    if a < 1e-12 and e < 1e-12: return np.linalg.norm(r)
    if a < 1e-12: s, t = 0.0, np.clip(f / e, 0, 1)
    else:
        c = d1 @ r
        if e < 1e-12: t, s = 0.0, np.clip(-c / a, 0, 1)
        else:
            b = d1 @ d2; den = a * e - b * b
            s = np.clip((b * f - c * e) / den, 0, 1) if den > 1e-12 else 0.0
            t = (b * s + f) / e
            if t < 0: t, s = 0.0, np.clip(-c / a, 0, 1)
            elif t > 1: t, s = 1.0, np.clip((b - c) / a, 0, 1)
    return np.linalg.norm((p1 + d1 * s) - (p2 + d2 * t))


def build_graph(minds, G, pos, F, u, th, p, rng):
    N = len(minds)
    nodes = [dict(id=i, kind="mind", mind=i, p=pos[i].copy()) for i in range(N)]
    edges = []          # dict(a, b, kind)
    deg = np.zeros(N + 512, int)
    adj = {}

    def add(a, b, kind):
        edges.append(dict(a=a, b=b, kind=kind))
        deg[a] += 1; deg[b] += 1
        adj.setdefault(a, set()).add(b); adj.setdefault(b, set()).add(a)

    # root junction at the throat + tendril down to the sculpture
    newest = [i for i in range(N) if minds[i]["gen"] == max(m["gen"] for m in minds)]
    throat_th = np.mean([th[i] for i in newest]) if newest else 0.0
    root_p = F.point(0.0, throat_th + math.pi * 0.5) + np.array([0, -0.12, 0])
    root_p *= np.array([0.8, 1, 0.8])
    root = len(nodes); nodes.append(dict(id=root, kind="root", mind=-1, p=root_p))
    tip_p = np.array([0.0, p["sculpt_top"] + 0.01, 0.0])
    tip = len(nodes); nodes.append(dict(id=tip, kind="sculpture", mind=-1, p=tip_p))
    add(root, tip, "tendril")
    for i in newest:
        add(i, root, "chain")

    # lineage chains
    for f in sorted({m["flask"] for m in minds}):
        ids = sorted([i for i in range(N) if minds[i]["flask"] == f], key=lambda i: minds[i]["gen"])
        for a, b in zip(ids, ids[1:]):
            add(a, b, "chain")

    # cross-links by genome similarity among spatial neighbours
    Gn = G - G.mean(0); Gn /= np.linalg.norm(Gn, axis=1, keepdims=True) + 1e-9
    tree = cKDTree(pos)
    cand = []
    for a, b in tree.query_pairs(p["cross_dmax"]):
        if b in adj.get(a, ()): continue
        d = np.linalg.norm(pos[a] - pos[b])
        sim = float(Gn[a] @ Gn[b])
        score = 0.65 * sim + 0.35 * (1 - d / p["cross_dmax"]) + rng.normal(0, 0.05)
        cand.append((score, a, b, d))
    cand.sort(reverse=True)
    want = int(p["cross_frac"] * N)
    allp = np.stack([n["p"] for n in nodes])
    got = 0

    def ok_angles(a, b):
        v = unit(allp[b] - allp[a])
        for c in adj.get(a, ()):
            if unit(allp[c] - allp[a]) @ v > math.cos(math.radians(28)): return False
        return True

    cap = np.where(rng.random(N) < 0.25, 4, 3)
    rej = dict(cap=0, angle=0, cross=0, skewer=0)
    xdeg = np.zeros(N + 512, int)
    for pas in range(3):             # pass 0: both fresh, pass 1: one fresh, pass 2: anything within caps
        for score, a, b, d in cand:
            if got >= want: break
            if b in adj.get(a, ()): continue
            fresh = int(xdeg[a] == 0) + int(xdeg[b] == 0)
            if fresh < 2 - pas: continue
            if deg[a] >= cap[a] or deg[b] >= cap[b]: rej["cap"] += 1; continue
            if not ok_angles(a, b) or not ok_angles(b, a): rej["angle"] += 1; continue
            bad = False
            for e in edges:
                if e["a"] in (a, b) or e["b"] in (a, b): continue
                if seg_dist(allp[a], allp[b], allp[e["a"]], allp[e["b"]]) < 0.13: bad = True; break
            if bad: rej["cross"] += 1; continue
            mid = 0.5 * (allp[a] + allp[b])
            near = tree.query_ball_point(mid, 0.5 * d - 0.05)
            if any(k not in (a, b) and seg_dist(allp[a], allp[b], allp[k], allp[k]) < 0.18 for k in near):
                rej["skewer"] += 1; continue
            add(a, b, "cross"); got += 1; xdeg[a] += 1; xdeg[b] += 1
    print(f"cross-links {got}/{want} from {len(cand)} candidates, rejected {rej}")

    # blind spurs off quiet nodes: dead-end burrows, the fossil's signature
    nspur = int(p["spur_frac"] * N)
    quiet = [i for i in range(N) if deg[i] <= 3]
    rng.shuffle(quiet)
    made = 0
    for i in quiet:
        if made >= nspur: break
        n = F.normal(u[i], th[i])
        # pick a direction away from existing edges, mostly tangential with an outward lean
        best, bestv = -9, None
        for _ in range(24):
            v = rng.normal(size=3); v = unit(v - (v @ n) * n * 0.6 + n * rng.uniform(0.1, 0.5))
            sc = min((-(unit(allp[c] - allp[i]) @ v)) for c in adj.get(i, ())) if adj.get(i) else 1
            if sc > best: best, bestv = sc, v
        L = rng.uniform(*p["spur_len"])
        sp = allp[i] + bestv * L
        if sp[1] > p["ceiling"] - 0.08: sp[1] = p["ceiling"] - 0.08
        if sp[1] < 0.35: continue
        if cKDTree(allp).query(sp)[0] < 0.2: continue
        k = len(nodes); nodes.append(dict(id=k, kind="spur", mind=-1, p=sp))
        allp = np.vstack([allp, sp[None]])
        add(i, k, "spur"); made += 1
    return nodes, edges, adj


# ---------------------------------------------------------------- routing
def catmull(pts, step):
    pts = [np.asarray(q, float) for q in pts]
    if len(pts) == 2:
        L = np.linalg.norm(pts[1] - pts[0]); n = max(2, int(L / step) + 1)
        t = np.linspace(0, 1, n)[:, None]
        return pts[0] * (1 - t) + pts[1] * t
    ext = [2 * pts[0] - pts[1]] + pts + [2 * pts[-1] - pts[-2]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        t0 = 0.0
        t1 = t0 + np.linalg.norm(p1 - p0) ** 0.5 + 1e-6
        t2 = t1 + np.linalg.norm(p2 - p1) ** 0.5 + 1e-6
        t3 = t2 + np.linalg.norm(p3 - p2) ** 0.5 + 1e-6
        n = max(3, int(np.linalg.norm(p2 - p1) / (step * 0.5)))
        for t in np.linspace(t1, t2, n, endpoint=False):
            a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1
            a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2
            a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3
            b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2
            b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3
            out.append((t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2)
    out.append(pts[-1])
    return resample(np.array(out), step)


def resample(Q, step):
    seg = np.linalg.norm(np.diff(Q, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    n = max(int(math.ceil(s[-1] / step)), 1)
    si = np.linspace(0, s[-1], n + 1)
    return np.stack([np.interp(si, s, Q[:, j]) for j in range(3)], 1)


def route(nodes, edges, F, p, rng):
    allp = np.stack([n["p"] for n in nodes])
    paths = []
    for e in edges:
        a, b = allp[e["a"]], allp[e["b"]]
        L = np.linalg.norm(b - a)
        d = unit(b - a)
        ctrl = [a]
        if e["kind"] == "tendril":
            # arc: leave the root sideways-down, sweep in and land on the sculpture from above
            side = unit(np.cross(d, [0, 1, 0]))
            ctrl.append(a + d * 0.3 * L + side * 0.22 * L + np.array([0, -0.05, 0]))
            ctrl.append(a + d * 0.72 * L + side * 0.12 * L + np.array([0, 0.02, 0]))
            ctrl.append(b + np.array([0, 0.06, 0]))
        else:
            n = unit(F.normal(*F.project(0.5 * (a + b))))
            tang = unit(np.cross(n, d))
            k = 1 if L < 0.5 else 2
            for j in range(k):
                t = (j + 1) / (k + 1)
                wobble = L * rng.uniform(0.08, 0.2) * rng.choice([-1, 1])
                lift = L * rng.uniform(-0.03, 0.06)
                ctrl.append(a + d * t * L + tang * wobble + n * lift)
        ctrl.append(b)
        Q = catmull(ctrl, p["path_step"])
        paths.append(Q)
    return relax(paths, nodes, edges, p)


def relax(paths, nodes, edges, p, iters=60):
    """push non-adjacent runs apart to >= gap, keep runs off foreign nodes; averaged,
    clamped steps + Laplacian smoothing, endpoints pinned."""
    allp = np.stack([n["p"] for n in nodes])
    gap = 2 * p["r_max"] + 0.08
    ends = [{e["a"], e["b"]} for e in edges]
    ntree = cKDTree(allp)
    for it in range(iters):
        allq = np.vstack(paths)
        owner = np.concatenate([np.full(len(q), i) for i, q in enumerate(paths)])
        tree = cKDTree(allq)
        pr = np.array(list(tree.query_pairs(gap)), dtype=int)
        disp = np.zeros_like(allq); cnt = np.zeros(len(allq))
        if len(pr):
            oi, oj = owner[pr[:, 0]], owner[pr[:, 1]]
            keep = oi != oj
            # runs that share a node meet at the junction by design
            share = np.array([bool(ends[a] & ends[b]) for a, b in zip(oi, oj)])
            keep &= ~share
            pr = pr[keep]
            d = allq[pr[:, 1]] - allq[pr[:, 0]]
            L = np.linalg.norm(d, axis=1) + 1e-9
            push = ((gap - L) * 0.5 / L)[:, None] * d
            np.add.at(disp, pr[:, 0], -push); np.add.at(disp, pr[:, 1], push)
            np.add.at(cnt, pr[:, 0], 1); np.add.at(cnt, pr[:, 1], 1)
        # keep away from foreign nodes
        dn, kn = ntree.query(allq, workers=-1)
        near = dn < 0.16
        for qi in np.nonzero(near)[0]:
            if kn[qi] in ends[owner[qi]]: continue
            d = allq[qi] - allp[kn[qi]]; L = np.linalg.norm(d) + 1e-9
            disp[qi] += (0.16 - L) * d / L; cnt[qi] += 1
        disp /= np.maximum(cnt, 1)[:, None]
        mag = np.linalg.norm(disp, axis=1)
        over = mag > 0.02
        disp[over] *= (0.02 / mag[over])[:, None]
        if mag.max() < 2e-4: break
        o = 0
        for i, q in enumerate(paths):
            n = len(q)
            dq = disp[o:o + n]; o += n
            w = np.sin(np.linspace(0, math.pi, n)) ** 0.5     # endpoints pinned
            q = q + dq * w[:, None]
            q[1:-1] = 0.6 * q[1:-1] + 0.2 * (q[:-2] + q[2:])
            paths[i] = resample(q, p["path_step"])
    return paths


# ---------------------------------------------------------------- calibre (Murray's law)
def calibre(nodes, edges, paths, p):
    """unit flow injected at every mind, sunk at the sculpture; solve a resistive network
    so loops carry flow too; r grows with flow^(1/3), floored at r_min, trunk at r_max."""
    n = len(nodes)
    sink = next(k["id"] for k in nodes if k["kind"] == "sculpture")
    Lp = np.zeros((n, n))
    lens = np.array([np.linalg.norm(np.diff(q, axis=0), axis=1).sum() for q in paths])
    for e, L in zip(edges, lens):
        g = 1.0 / max(L, 0.05)
        a, b = e["a"], e["b"]
        Lp[a, a] += g; Lp[b, b] += g; Lp[a, b] -= g; Lp[b, a] -= g
    inj = np.array([1.0 if k["kind"] == "mind" else 0.0 for k in nodes])
    inj[sink] = -inj.sum()
    keep = [i for i in range(n) if i != sink]
    phi = np.zeros(n)
    phi[keep] = np.linalg.solve(Lp[np.ix_(keep, keep)], inj[keep])
    q = np.array([abs(phi[e["a"]] - phi[e["b"]]) / max(L, 0.05) for e, L in zip(edges, lens)])
    qmax = q.max()
    r = p["r_min"] + (p["r_max"] - p["r_min"]) * (q / qmax) ** (1 / 3)
    for i, e in enumerate(edges):
        if e["kind"] == "spur": r[i] = p["r_min"] * 0.95
        if e["kind"] == "tendril": r[i] = p["r_max"]
        e["flow"] = float(q[i]); e["r"] = float(r[i]); e["len"] = float(lens[i])
        # orient every run so s runs from the upstream end (higher potential, older) to the
        # downstream end (toward the sculpture); boluses travel in +s
        if phi[e["a"]] < phi[e["b"]] and e["kind"] != "spur":
            e["a"], e["b"] = e["b"], e["a"]; paths[i] = paths[i][::-1].copy()
        if e["kind"] == "spur":            # spurs: base -> blind tip
            pass
    # nodule swelling radius per node (the dome sits in it) and the dome centre
    inc = {}
    for e in edges:
        inc.setdefault(e["a"], []).append(e["r"]); inc.setdefault(e["b"], []).append(e["r"])
    for k in nodes:
        rs = inc.get(k["id"], [p["r_min"]])
        k["nod_r"] = float(max(1.25 * max(rs), 0.046)) if k["kind"] == "mind" else 0.0
        k["r_max"] = float(max(rs))
    return r


# ---------------------------------------------------------------- driver
def build(minds, G, seed, params=None):
    p = dict(P); p.update(params or {})
    rng = np.random.default_rng(seed)
    F = Funnel(p)
    pos, u, th, off = place_minds(minds, G, F, p, rng)
    nodes, edges, adj = build_graph(minds, G, pos, F, u, th, p, rng)
    paths = route(nodes, edges, F, p, rng)
    calibre(nodes, edges, paths, p)
    # dome facing: toward where the wearer stands (beside the sculpture, head height)
    eye = np.array([0.0, 1.5, 0.0])
    for k in nodes:
        k["face"] = unit(eye - k["p"]) if k["kind"] == "mind" else np.zeros(3)
        k["deg"] = int(len(adj.get(k["id"], ())))
        k["dome_r"] = 0.058 if k["kind"] == "mind" else 0.0
        # dome protrudes from the nodule: its centre sits on the nodule surface, facing the wearer
        k["dome"] = k["p"] + k["face"] * (k["nod_r"] * 0.85) if k["kind"] == "mind" else k["p"]
    return dict(seed=seed, params=p, nodes=nodes, edges=edges, paths=paths, funnel=F)


def to_json(net):
    nodes = [dict(id=k["id"], kind=k["kind"], mind=k["mind"], deg=k["deg"], nod_r=round(k["nod_r"], 4),
                  dome_r=k["dome_r"], p=[round(float(v), 4) for v in k["p"]],
                  face=[round(float(v), 4) for v in k["face"]], dome=[round(float(v), 4) for v in k["dome"]])
             for k in net["nodes"]]
    edges = [dict(a=e["a"], b=e["b"], kind=e["kind"], r=round(e["r"], 4), flow=round(e["flow"], 4),
                  len=round(e["len"], 3), path=np.round(q, 4).tolist())
             for e, q in zip(net["edges"], net["paths"])]
    return dict(seed=net["seed"], params=net["params"], nodes=nodes, edges=edges)


def plot(net, out):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axs = plt.subplots(1, 2, figsize=(16, 8))
    col = dict(chain="#333", cross="#1a7", spur="#c62", tendril="#c00")
    for ax, (i0, i1, lab) in zip(axs, [(0, 1, "side (x,y)"), (0, 2, "top (x,z)")]):
        for e, q in zip(net["edges"], net["paths"]):
            ax.plot(q[:, i0], q[:, i1], color=col[e["kind"]], lw=e["r"] * 60, alpha=0.85)
        for k in net["nodes"]:
            if k["kind"] == "mind":
                ax.plot(k["p"][i0], k["p"][i1], "o", ms=4, color="#06c" if k["mind"] % 2 else "#c06")
        ax.set_aspect("equal"); ax.set_title(lab); ax.grid(alpha=0.2)
    fig.suptitle(f"seed {net['seed']}: {len(net['nodes'])} nodes, {len(net['edges'])} edges")
    fig.tight_layout(); fig.savefig(out, dpi=90); plt.close(fig)


def main():
    args = dict(a.split("=") for a in sys.argv[1:])
    seed = int(args.get("seed", 7))
    params = {k: float(v) for k, v in args.items() if k in P}
    minds = json.loads((BUILD / "minds.json").read_text())["minds"]
    G = np.load(BUILD / "genomes.npy")
    net = build(minds, G, seed, params)
    (BUILD / f"network_{seed}.json").write_text(json.dumps(to_json(net)))
    kinds = {}
    for e in net["edges"]: kinds[e["kind"]] = kinds.get(e["kind"], 0) + 1
    degs = np.bincount([k["deg"] for k in net["nodes"] if k["kind"] == "mind"])
    L = sum(e["len"] for e in net["edges"])
    print(f"seed {seed}: {len(net['nodes'])} nodes, edges {kinds}, total length {L:.1f} m, "
          f"mind degree histogram {degs.tolist()}, r {min(e['r'] for e in net['edges']):.3f}.."
          f"{max(e['r'] for e in net['edges']):.3f}")
    if int(args.get("plot", 1)):
        plot(net, BUILD / f"network_{seed}.png")


if __name__ == "__main__":
    main()
