# AR Shakespeare Worms — Burrow Network (spec v1, 2026-10-11)

A Thalassinoides-style burrow network floating in the real room (Quest passthrough AR),
rooted at the physical worm sculpture and mushrooming up into the ceiling. The nodes
of the network are worm minds. Pinch a node to open that mind.

Everything is baked offline and shipped inside the APK. No network, no SD card.

## 1. The minds (nodes)

* ~208 minds: the single best poet (highest fitness) of every generation in both flasks
  of HamletRNAWorld v6 (2 flasks x 104 generations). Stratified per generation, not a
  global top-N, so the sample spans the whole history rather than the last twenty gens.
* Each node is a glass dome, like the acrylic eyes on the sculpture, with something dark
  inside (the brain). Node = junction or blind end of the burrow network.
* Per-mind data shipped: connectome weights, poem, fitness, name, generation, flask.

## 2. Shape of the network

* Rooted at the sculpture (floor). The sculpture is the throat of a funnel / gravity
  well. Newest minds (gen 104) sit at the throat near the sculpture; older minds reach
  up and outward; gen 1 forms the flat blanket spreading across the ceiling.
* Generation -> position along the funnel profile (height + radius). Which minds get
  tunnelled together is decided by similarity (genome distance / poem embedding) among
  neighbours on the funnel, plus parent-child lineage links where they exist.
* One tendril arcs down from the throat and touches the sculpture.
* Offline generator tries many layouts (seeds, metric weights) and scores each for
  tunnel-ness: loop count, cell-size spread, junction angles, no self-crossings,
  clearance, calibre grading. Keeps the best.

## 3. Aesthetic (from the XPS worm-mount scripts: take_weave, take_flange, take_eruption)

* Constant calibre along a run; calibre steps at junctions (Murray's law from flow).
* Swollen turning chambers at junctions. Bulbous blind ends, never tapered to a point.
* Boxwork loops with open polygonal cells (ref: thalassinoides_inspo.jpg).
* Rough bioturbation skin (take_weave). Surface reads as the sculpture: wire-wrapped
  silver / aluminium. Domes: clear glass, dark interior.

## 4. Motion (no growth during the run)

* Subtle breathing: slow low-frequency radius modulation along every run.
* Peristaltic boluses: bulges travel along tunnels as if swallowing prey (anaconda).
  Boluses are born at older minds and travel down the lineage toward the sculpture.
  Scheduled from a baked table; occasional, not constant.
* Very slow sway of free tendrils.
* Implementation: static baked mesh + per-vertex spine data (arc length, run id,
  radius, spine tangent). A vertex shader does all motion on-device. No baked frames.

## 5. Interaction (revised after the first headset session, 2026-10-11)

* Launch: the wearer pinches AND HOLDS (1 s, reticle swells) at the sculpture's centre; the
  network roots there. Holding both pinches for 2 s clears it for re-placement.
* One shared TEXT WORLD: a floating volume (0.9 x 0.56 x 0.16 m) that appears at chest height
  in front of the wearer when the network is placed, draggable by pinching inside it. All
  summoned minds crawl over the SAME scrolling Hamlet (one WmScroller shared by their worlds;
  a word eaten by one is gone for all). The sim stays 2D; depth is presentation: each word
  sits at a stable random depth, each worm wanders through the thickness, so they meet in 3D.
* Summoning: a pinch that points at a dome (near, or along the visible hand ray) selects it on
  release. The dome flashes twice, dims to an empty shell, and a translucent bubble flies on a
  slow arc to a slot beside the text world. The bubble holds the live x-ray of that worm (its
  body copied from the simulation with the 300 neurons lit along it, the website's x-ray) and
  its name, generation and score. Tapping a bubble flashes it and flies it home.
* Talking: hold a pinch pointing at nothing for 0.5 s -> orb + "listening"; release to send.
  whisper transcribes on-device; the sentence is laid into the shared field as pale blue words
  with their baked taste (replayed into late-joining minds), so the worms can eat and say them.
* Eaten words gather above the worm's head into a line; after a 3 s pause (or 7 words) the
  whole line is recited in one breath in the worm's own voice, spatialised at its head, slow
  and low (speakers drawn from the 36 deepest of the 109), inside a concert-hall reverb.
  Punctuation tokens are swallowed silently. Each clip carries 1.6 s of silence so the hall
  rings out. Guardian is suppressed (BoundaryFree.cs) so the wearer can walk the whole piece.
* Kokoro (sherpa-onnx, British narrator voices incl. bm_fable) is the candidate upgrade for
  more gravitas: samples in build/voices/listen; ~10x the per-word cost, so it would need a
  pre-rendered Hamlet vocabulary bank per voice with live synthesis only for spoken words.

## 6. Pipeline

* `tools/` Python: select minds -> layout + route + score -> bake mesh + spine data +
  bolus schedule + per-mind assets -> `Assets/StreamingAssets/`.
* Per-mind brains: tools/worms/bake_all.py (via HamletWorms' bake.py, unmodified) then
  split_shared.py -> StreamingAssets/worms/shared.hwrm (1 MB, tables common to every mind)
  + mind_<id>.hwrm (~47 KB connectome each). The C# port (Assets/Scripts/WormSim) is verified
  bit-identical to the firmware's C over 1.5 M ticks (tools/wormsim_test/run.sh).
* Unity (existing Quest AR build pipeline, Meta OpenXR passthrough, XR Hands):
  loads the bake, renders with the motion shader, handles placement, selection, pop-up.
  `./build_quest.sh` -> Build/ShakespeareWorms.apk. Native speech: tools/speech/wormspeech.c
  (shim over sherpa-onnx's C API, built with Unity's NDK by build_shim.sh) ->
  Assets/Plugins/Android/libs/arm64-v8a; models staged by install_models.sh into
  StreamingAssets/speech and copied to persistentDataPath on first launch.

## 7. Kept / dropped from the previous build

* Kept: Quest AR build + passthrough setup, hand tracking + pinch, StreamingAssets JSON
  loading, editor build scripts.
* Dropped: grow_tree.py, tree.json, TreeMeshBuilder, Bark shader, the lineage-tree
  semantics, WormTree scene.
