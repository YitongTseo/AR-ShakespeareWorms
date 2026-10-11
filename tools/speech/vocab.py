#!/usr/bin/env python3
"""Extended vocabulary for the conversation feature: the words a wearer is likely to say.

For every word (Hamlet's 4919 + the N most frequent English words) this bakes what the worm's
chemosensory path needs when that word is dropped into its world at runtime:
  * E(word) = ReLU(W_E . nomic512(word) + b_E), 11 floats, under the SAME embedder params the
    per-mind .hwrm assets were baked with (HamletWorms/tools/bake.py: random_init(seed=0)
    unless an evolved genome is supplied)
  * its universal POS tag index (same 12-tag order as v7's embedding.POS_TAGS)
Unknown spoken words fall back at runtime to the nearest known word by character trigrams.

    python3 tools/speech/vocab.py [--top 12000] [--embedder-seed 0] [--embedder-genome path]
-> Assets/StreamingAssets/speech/vocab_ext.json  {words, etable(flat f32 x11), pos(u8)}
"""
import argparse
import json
import os
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
V7 = Path("/Users/yitong/Documents/GitHub/HamletRNAWorld/v7")
HW = Path("/Users/yitong/Documents/GitHub/HamletWorms")
sys.path.insert(0, str(V7))
os.environ.setdefault("NLTK_DATA", str(HW / "tools" / "nltk_data"))
from server import embedding, pos_scorers  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--top", type=int, default=12000)
    ap.add_argument("--embedder-seed", type=int, default=0)
    ap.add_argument("--embedder-genome", type=Path, default=None)
    ap.add_argument("--out", type=Path, default=ROOT / "Assets" / "StreamingAssets" / "speech" / "vocab_ext.json")
    args = ap.parse_args()

    from wordfreq import top_n_list
    nomic = json.loads((V7 / "cache" / "corpus_nomic512.json").read_text())
    hamlet = {w: np.asarray(v, np.float64) for w, v in zip(nomic["words"], nomic["nomic512"])}
    common = [w for w in top_n_list("en", args.top) if w.isalpha() and w not in hamlet]
    print(f"hamlet {len(hamlet)} words + {len(common)} common words to embed")

    cache = ROOT / "build" / "vocab" / "common_nomic512.npz"
    if cache.exists() and set(np.load(cache, allow_pickle=True)["words"]) >= set(common):
        z = np.load(cache, allow_pickle=True)
        idx = {w: i for i, w in enumerate(z["words"])}
        vecs = np.stack([z["vecs"][idx[w]] for w in common])
        print("using cached nomic vectors")
    else:
        from sentence_transformers import SentenceTransformer
        model = SentenceTransformer(nomic["model"], trust_remote_code=True, device="cpu")
        t0 = time.time()
        vecs = model.encode([f"{nomic['prefix']} {w}" for w in common], batch_size=64,
                            convert_to_numpy=True, normalize_embeddings=True, show_progress_bar=False)
        vecs = np.asarray(vecs, np.float64)
        print(f"embedded in {time.time()-t0:.0f}s, dims {vecs.shape[1]}")
        if vecs.shape[1] != 512:   # matryoshka: the corpus cache was truncated to 512 then stored
            vecs = vecs[:, :512]
            vecs /= np.linalg.norm(vecs, axis=1, keepdims=True)
        cache.parent.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(cache, words=np.array(common, dtype=object), vecs=vecs.astype(np.float32))

    if args.embedder_genome:
        raw = args.embedder_genome
        vec = np.load(raw) if raw.suffix == ".npy" else np.asarray(json.loads(raw.read_text()))
        params = embedding.EmbeddingParams.from_flat(vec)
    else:
        params = embedding.EmbeddingParams.random_init(args.embedder_seed)

    words = list(hamlet.keys()) + common
    V = np.vstack([np.stack(list(hamlet.values())), vecs])
    E = np.maximum(0.0, V @ params.W_E + params.b_E)
    tags = [pos_scorers.tag_word(w) for w in words]
    tidx = {t: i for i, t in enumerate(embedding.POS_TAGS)}
    pos = [tidx.get(t, tidx["X"]) for t in tags]
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(dict(
        n=len(words), dim=int(E.shape[1]), embedder=("genome:" + str(args.embedder_genome)) if args.embedder_genome
        else f"random_init(seed={args.embedder_seed})", hamlet=len(hamlet),
        words=words, etable=[round(float(x), 5) for x in E.ravel()], pos=pos), separators=(",", ":")))
    print(f"wrote {args.out} ({args.out.stat().st_size/1e6:.1f} MB): {len(words)} words, E mean {E.mean():.3f}")


if __name__ == "__main__":
    main()
