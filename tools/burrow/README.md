# tools/burrow — offline generator for the AR burrow network

Everything the headset renders is baked here; the app only loads and animates it.

```
python3 minds.py                               # pick the minds -> build/burrow/minds.json, genomes.npy
python3 network.py seed=7 [ceiling=2.6 ...]    # layout + graph + routes + calibre -> network_7.json (+ .png)
python3 field.py ../../build/burrow/network_7.json vox=0.015 faces=220000   # SDF -> mesh (.npz, .ply)
python3 bake.py  ../../build/burrow/network_7.json                          # -> Assets/StreamingAssets/burrow/
./run_preview.sh 7 0.015 wearer,outside,up     # the three above + a Blender render of the result
```

* `network.py` holds every shape parameter in `P` (metres). `well_exp`, `flare_r`, `blanket_r`
  set the gravity-well profile; `turns` the helical drift of each lineage; `cross_frac`,
  `spur_frac` the boxwork density; `r_min`/`r_max` the Murray-graded calibre.
* Runs are oriented upstream (older mind) -> downstream (toward the sculpture) so the
  bolus animation always travels in +s.
* `field.py` encodes the sculptural rules (constant calibre per run, swollen turning
  chambers, bulbous blind ends, nodule per mind, bioturbation skin) and bakes per-vertex
  spine attributes (s, r, phi, node proximity, run id, tip weight) for the Unity shader.
* Final bake: `vox=0.01 faces=320000` (a few minutes). Iterate at `vox=0.02`.
