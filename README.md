# AR Shakespeare Worms

A Quest 3 passthrough-AR piece: a Thalassinoides burrow network of 195 worm minds, rooted
at the physical worm sculpture and mushrooming into the ceiling. Pinch a dome to open a
mind; hold a pinch to talk to the open ones. Everything runs offline on the headset.
See `SPEC.md` for the design and `tools/burrow/README.md` for the generator.

## Build and install

```
./build_quest.sh                      # Unity batch build -> Build/ShakespeareWorms.apk (log: Logs/build.log)
adb install -r Build/ShakespeareWorms.apk
```

First launch copies the speech models (~190 MB) to the app's data folder; later launches
start straight away. The app asks for microphone permission the first time you hold a pinch.

## Regenerating the content

```
cd tools/burrow && python3 minds.py && ./run_preview.sh 7 0.015     # minds -> network -> mesh -> renders
python3 tools/burrow/bake.py build/burrow/network_7.json            # -> Assets/StreamingAssets/burrow
python3 tools/worms/bake_all.py && python3 tools/worms/split_shared.py   # per-mind brains (shared + overlays)
python3 tools/speech/vocab.py                                       # extended taste vocabulary (slow: embeds 12k words)
./tools/speech/install_models.sh                                    # stage TTS/ASR models from build/models
./tools/speech/build_shim.sh                                        # native speech shim (needs Unity's NDK)
```

## Using it

1. Put the headset on near the sculpture. The prompt says: pinch at the centre of the sculpture.
2. The funnel appears, its tendril touching where you pinched. Walk under it, look up.
3. Tap-pinch a glass dome (near, or point from a distance) to open that mind: the live worm
   crawls over Hamlet and speaks what it eats; its 300 neurons glow beside it. Up to five open.
4. Hold a pinch (either hand) and speak; let go. Your words drift into every open worm's field.
5. Hold both pinches for two seconds to re-place the network.
6. Touch a tab under the text world with a fingertip to switch a view (X / O / C on a keyboard):
   **x-ray** shows each worm's connectome through its skin, coloured by class (chemosensory cyan,
   sensory blue, motor orange); **senses** draws dashed lines from a worm's nose to the words it
   can smell; **chemo** opens a card by each bubble with the 12 chemosensory pairs and the words
   feeding each neuron. A word keeps one colour across its line and its bar segments.
