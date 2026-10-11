#!/usr/bin/env python3
"""Measure every VCTK speaker in the piper voice: median pitch (F0), loudness, and how much of
the clip is voiced, so the worms can be given the voices with weight. Writes build/voices/speakers.json
and a contact sheet of WAVs for the top candidates.

    python3 tools/speech/rank_speakers.py [--text "..."] [--speed 0.8]
"""
import argparse, json, os, wave
import numpy as np
import sherpa_onnx

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
D = os.path.join(ROOT, "build/models/vits-piper-en_GB-vctk-medium/vits-piper-en_GB-vctk-medium")
OUT = os.path.join(ROOT, "build/voices")


def f0_median(x, sr):
    """crude but adequate: autocorrelation pitch on 40 ms frames, voiced frames only."""
    frame, hop = int(0.04 * sr), int(0.02 * sr)
    lo, hi = int(sr / 400), int(sr / 60)
    f0s = []
    for s in range(0, len(x) - frame, hop):
        w = x[s:s + frame]
        if np.sqrt((w ** 2).mean()) < 0.02: continue
        w = w - w.mean()
        ac = np.correlate(w, w, "full")[frame - 1:]
        ac /= ac[0] + 1e-9
        k = lo + np.argmax(ac[lo:hi])
        if ac[k] > 0.45: f0s.append(sr / k)
    return (float(np.median(f0s)) if f0s else 0.0), len(f0s)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--text", default="To be, or not to be: that is the question.")
    ap.add_argument("--speed", type=float, default=0.85)
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    tts = sherpa_onnx.OfflineTts(sherpa_onnx.OfflineTtsConfig(
        model=sherpa_onnx.OfflineTtsModelConfig(
            vits=sherpa_onnx.OfflineTtsVitsModelConfig(model=f"{D}/en_GB-vctk-medium.onnx", lexicon="", tokens=f"{D}/tokens.txt", data_dir=f"{D}/espeak-ng-data"),
            num_threads=4, provider="cpu"), max_num_sentences=1))
    rows = []
    for sid in range(tts.num_speakers):
        au = tts.generate(a.text, sid=sid, speed=a.speed)
        x = np.asarray(au.samples, np.float32)
        f0, voiced = f0_median(x, au.sample_rate)
        rms = float(np.sqrt((x ** 2).mean()))
        rows.append(dict(sid=sid, f0=round(f0, 1), rms=round(rms, 4), voiced_frames=voiced, seconds=round(len(x) / au.sample_rate, 2)))
        print(f"sid {sid:3d}  f0 {f0:6.1f} Hz  rms {rms:.3f}  {len(x)/au.sample_rate:.2f}s")
    rows.sort(key=lambda r: r["f0"])
    json.dump(dict(text=a.text, speed=a.speed, speakers=rows), open(f"{OUT}/speakers.json", "w"), indent=1)
    # keep a WAV of the 12 lowest and 4 highest for listening
    picks = [r["sid"] for r in rows if r["f0"] > 0][:12] + [r["sid"] for r in rows if r["f0"] > 0][-4:]
    for sid in picks:
        au = tts.generate(a.text, sid=sid, speed=a.speed)
        x = np.asarray(au.samples, np.float32)
        with wave.open(f"{OUT}/piper_sid{sid:03d}.wav", "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(au.sample_rate); w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())
    print("lowest-pitched speakers:", [r["sid"] for r in rows if r["f0"] > 0][:20])


if __name__ == "__main__":
    main()
