#!/usr/bin/env python3
"""Bake a .hwrm worm asset for an arbitrary mind, given the path to its
weights.json.

HamletWorms/tools/bake.py bakes exactly one worm, located by flask/gen/name
inside the v7 data tree, and reads its seed from seed.txt. The burrow's minds
come from v6 (same 300-neuron / 3689-synapse topology, different weights and no
v7 directory), so this script reuses bake.py's machinery -- its section writer,
container format, v7 import setup and NLTK_DATA wiring -- and re-implements only
the bake body with the weights and the seed taken from the command line.
bake.py itself is imported, never modified.

The on-disk format is bit-for-bit the one bake.py produces (verified by baking
Liam through both paths and comparing every section but META), so the firmware
parser and the C# port read it unchanged.

Usage:
    python3 tools/worms/bake_mind.py --weights <path/to/weights.json> --out <file.hwrm> [--seed N]

Everything else (corpus passage, embedder genome / seed) defaults to what
bake.py uses. The embedder is the PLACEHOLDER random_init(seed=0) unless
--embedder-genome is given, exactly as in bake.py.

Shared tables (vocab, E-table, nets, corpus) are computed once per process, so
bake_all.py can bake ~200 minds in a single run.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent          # <repo>/tools/worms
REPO = HERE.parent.parent                       # <repo>
HAMLETWORMS = Path(os.environ.get("HAMLETWORMS_DIR", REPO.parent / "HamletWorms"))
if not (HAMLETWORMS / "tools" / "bake.py").exists():
    sys.exit(f"cannot find HamletWorms/tools/bake.py under {HAMLETWORMS} (set HAMLETWORMS_DIR)")
sys.path.insert(0, str(HAMLETWORMS / "tools"))
# The HamletWorms and HamletRNAWorld trees are read-only from here: don't
# leave __pycache__ droppings in them.
sys.dont_write_bytecode = True

# Importing bake.py puts the v7 tree on sys.path, points NLTK_DATA at the
# vendored tagger data, and gives us Blob/pack. Its main() only runs under
# __main__, so the import has no other side effects.
import bake  # noqa: E402
from bake import Blob, pack  # noqa: E402

import numpy as np  # noqa: E402
from corpus.hamlet import get_sentences_with_flags  # noqa: E402
from server import embedding, pos_scorers  # noqa: E402
from sim import connectome as conn  # noqa: E402
from sim.chemosensory_mapping import PC_NEURON_PAIRS  # noqa: E402

V7 = bake.V7

# Sections whose bytes depend only on the corpus + embedder, never on the mind.
SHARED_TAGS = ("VOCAB", "ETABLE", "NET", "CORPUS")


class Shared:
    """Everything that is identical across minds: the embedder, the vocabulary
    and its E-table, the tokenised/tagged corpus, and the pre-serialised
    sections built from them."""

    def __init__(self, embedder_seed: int = 0, embedder_genome: Path | None = None,
                 passage: str = "full", quiet: bool = False):
        self.passage = passage
        log = (lambda *a: None) if quiet else print

        # -- embedder params (same choice bake.py makes) -----------------------
        if embedder_genome:
            raw = Path(embedder_genome)
            vec = np.load(raw) if raw.suffix == ".npy" else np.asarray(json.loads(raw.read_text()))
            self.params = embedding.EmbeddingParams.from_flat(vec)
            self.emb_src = str(raw)
        else:
            self.params = embedding.EmbeddingParams.random_init(embedder_seed)
            self.emb_src = f"PLACEHOLDER random_init(seed={embedder_seed})"
        model = embedding.EmbeddingModel(self.params)
        log(f"embedder {self.emb_src}")

        # -- vocab + E-table ----------------------------------------------------
        self.vocab = model._words
        self.etable = np.asarray(model._E_table, dtype=np.float64)
        assert self.etable.shape == (len(self.vocab), embedding.D_EMB), self.etable.shape
        log(f"vocab   {len(self.vocab)} words, E-table {self.etable.shape} = {self.etable.nbytes/1024:.0f} KB fp64")

        # -- corpus -------------------------------------------------------------
        sentences, edible = get_sentences_with_flags(passage)
        widx = model._widx
        tok_vocab, tok_pos, tok_text, sent_start = [], [], [], [0]
        for s in sentences:
            for t in s:
                tok_vocab.append(widx.get(embedding._norm(t), 0xFFFF))
                tok_pos.append(embedding._POS_INDEX.get(pos_scorers.tag_word(t), embedding._POS_INDEX["X"]))
                tok_text.append(t)
            sent_start.append(len(tok_vocab))
        longest = max(len(x) for x in sentences)
        if longest > 40:
            sys.exit(f"longest sentence is {longest} tokens; raise WM_MAX_LINE_WORDS")
        oov = sum(1 for v in tok_vocab if v == 0xFFFF)
        log(f"corpus  {len(sentences)} sentences, {len(tok_vocab)} tokens, "
            f"{oov} OOV ({100*oov/len(tok_vocab):.1f}%), {sum(edible)} edible sentences")
        self.n_sentences, self.n_tokens = len(sentences), len(tok_vocab)

        # -- pre-serialised shared sections (byte-identical to bake.py's) --------
        b = Blob(); b.strtab(self.vocab); vocab_sec = bytes(b.buf)

        b = Blob(); b.u32(len(self.vocab)); b.u32(embedding.D_EMB); b.arr_f64(self.etable)
        etable_sec = bytes(b.buf)

        b = Blob()
        for name in ("W_Hh", "b_Hh", "W_Hf", "b_Hf", "W_P1", "b_P1", "W_P2", "b_P2"):
            b.arr_f64(getattr(self.params, name))
        net_sec = bytes(b.buf)

        b = Blob()
        b.u32(len(sentences)); b.u32(len(tok_vocab))
        b.arr_u32(sent_start)
        b.arr_u16(tok_vocab)
        b.arr_u8(tok_pos)
        b.arr_u8([len(t) for t in tok_text])      # code points, not bytes
        b.arr_u8([1 if e else 0 for e in edible])
        b.align(8)
        b.strtab(tok_text)
        corpus_sec = bytes(b.buf)

        self.sections = {"VOCAB": vocab_sec, "ETABLE": etable_sec, "NET": net_sec, "CORPUS": corpus_sec}

        # x-ray anchors, keyed by neuron name
        coords_path = V7 / "cache" / "neuron_body_coords.json"
        self.coords = json.loads(coords_path.read_text())["neurons"] if coords_path.exists() else {}


def bake_mind(weights_path: Path, seed: int, shared: Shared,
              meta_extra: dict | None = None, quiet: bool = False):
    """Returns (asset_bytes, [(tag, payload), ...]) for one mind."""
    log = (lambda *a: None) if quiet else print
    weights = json.loads(Path(weights_path).read_text())  # insertion order preserved

    # -- connectome: mirror Connectome.__init__ exactly ------------------------
    names = set(weights.keys())
    for tgt in weights.values():
        names.update(tgt.keys())
    names.update(conn.MUSCLE_LIST)
    neurons = sorted(names)
    nidx = {n: i for i, n in enumerate(neurons)}

    row_start, cols, ws = [0], [], []
    for n in neurons:
        # dendrite_accumulate walks `self.weights[pre].items()` -- JSON insertion
        # order. Preserve it verbatim: float addition is not associative.
        for post, w in weights.get(n, {}).items():
            cols.append(nidx[post])
            ws.append(w)
        row_start.append(len(cols))
    log(f"brain   {len(neurons)} neurons, {len(cols)} edges")

    # motorcontrol() walks MUSCLE_LIST = M_LEFT + M_RIGHT *with its duplicates*
    # (MDL21/MVL21 appear in M_RIGHT -- the inherited GoPiGo typo). Preserve it.
    m_left, m_right = set(conn.M_LEFT), set(conn.M_RIGHT)
    mus_idx, mus_side = [], []
    for m in conn.MUSCLE_LIST:
        mus_idx.append(nidx[m])
        mus_side.append(0 if m in m_left else (1 if m in m_right else 2))

    is_muscle = [1 if n[:3] in conn.MUSCLE_PREFIXES else 0 for n in neurons]

    # rand_excite does choice(list(self.weights.keys())) -- JSON key order.
    presyn = [nidx[n] for n in weights.keys()]

    # -- sections --------------------------------------------------------------
    S: list[tuple[str, bytes]] = []

    meta = {
        "flask": None, "worm": None, "gen": None, "seed": seed,
        "fitness": None, "embedder": shared.emb_src, "passage": shared.passage,
        "n_vocab": len(shared.vocab), "n_neurons": len(neurons), "n_edges": len(cols),
        "n_sentences": shared.n_sentences, "n_tokens": shared.n_tokens,
        "weights": str(weights_path),
    }
    if meta_extra:
        meta.update(meta_extra)
    b = Blob(); b.strtab([json.dumps(meta, indent=2)]); S.append(("META", bytes(b.buf)))

    S.append(("VOCAB", shared.sections["VOCAB"]))
    S.append(("ETABLE", shared.sections["ETABLE"]))
    S.append(("NET", shared.sections["NET"]))

    b = Blob(); b.strtab(neurons); b.arr_u8(is_muscle); b.align(4)
    S.append(("NEURONS", bytes(b.buf)))

    b = Blob()
    b.u32(len(neurons)); b.u32(len(cols))
    b.arr_u32(row_start); b.arr_u16(cols); b.align(8); b.arr_f64(ws)
    S.append(("SYNAPSE", bytes(b.buf)))

    b = Blob()
    b.u32(len(mus_idx)); b.arr_u16(mus_idx); b.arr_u8(mus_side); b.align(4)
    S.append(("MUSCLES", bytes(b.buf)))

    b = Blob(); b.u32(len(presyn)); b.arr_u16(presyn); b.align(4)
    S.append(("PRESYN", bytes(b.buf)))

    axial, lateral = [], []
    for n in neurons:
        c_ = shared.coords.get(n)
        axial.append(c_["axial"] if c_ else -1.0)
        lateral.append(c_["lateral"] if c_ else 0.0)
    b = Blob(); b.arr_f32(axial); b.arr_f32(lateral)
    S.append(("NEURONXY", bytes(b.buf)))

    b = Blob()
    for L, R in PC_NEURON_PAIRS:
        b.u16(nidx[L]); b.u16(nidx[R])
    S.append(("CHEMOMAP", bytes(b.buf)))

    b = Blob()
    for group in (conn.HUNGER_NEURONS, conn.NOSE_TOUCH_NEURONS, conn.FOOD_SENSE_NEURONS):
        b.u32(len(group)); b.arr_u16([nidx[n] for n in group])
    b.align(4)
    S.append(("STIMSETS", bytes(b.buf)))

    S.append(("CORPUS", shared.sections["CORPUS"]))

    b = Blob(); b.u32(seed); S.append(("SEED", bytes(b.buf)))

    return pack(S), S


def default_seed_for(weights_path: Path) -> int:
    """seed.txt next to weights.json when there is one (every v6/v7 worm
    directory has it), else 0."""
    st = Path(weights_path).parent / "seed.txt"
    if st.exists():
        try:
            return int(st.read_text().strip())
        except ValueError:
            pass
    return 0


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--weights", type=Path, required=True, help="path to the mind's weights.json")
    ap.add_argument("--out", type=Path, required=True, help="output .hwrm path")
    ap.add_argument("--seed", type=int, default=None,
                    help="world seed (CPython random.seed); default: seed.txt beside weights.json, else 0")
    ap.add_argument("--passage", default="full")
    ap.add_argument("--embedder-seed", type=int, default=0, help="as bake.py: PLACEHOLDER random_init seed")
    ap.add_argument("--embedder-genome", type=Path, default=None, help="as bake.py: flat .npy/.json genome")
    ap.add_argument("--meta", default=None, help="JSON object merged into the META section")
    args = ap.parse_args()

    if not args.weights.exists():
        sys.exit(f"no such weights file: {args.weights}")
    seed = args.seed if args.seed is not None else default_seed_for(args.weights)
    if not (0 <= seed < 2**32):
        sys.exit("seed must fit in 32 bits (the firmware RNG takes a single key word)")
    extra = json.loads(args.meta) if args.meta else None

    print(f"mind    {args.weights}  seed={seed}")
    shared = Shared(args.embedder_seed, args.embedder_genome, args.passage)
    out, S = bake_mind(args.weights, seed, shared, extra)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes(out)
    print()
    for tag, data in S:
        print(f"  {tag:<9} {len(data):>9,} B{'  (shared)' if tag in SHARED_TAGS else ''}")
    print(f"  {'TOTAL':<9} {len(out):>9,} B  ({len(out)/1024/1024:.2f} MB) -> {args.out}")


if __name__ == "__main__":
    main()
