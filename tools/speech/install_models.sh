#!/bin/zsh
# Stage the speech models into StreamingAssets/speech with a file manifest, so the app can
# copy them to the device filesystem on first launch (native code needs real paths, not APK entries).
set -e
cd "$(dirname "$0")/../.."
SRC=build/models
DST=Assets/StreamingAssets/speech
rm -rf "$DST/tts" "$DST/asr"; mkdir -p "$DST/tts" "$DST/asr"
T=$SRC/vits-piper-en_GB-vctk-medium/vits-piper-en_GB-vctk-medium
cp "$T/en_GB-vctk-medium.onnx" "$DST/tts/model.onnx"
cp "$T/tokens.txt" "$DST/tts/tokens.txt"
cp -R "$T/espeak-ng-data" "$DST/tts/espeak-ng-data"
W=$SRC/sherpa-onnx-whisper-tiny.en/sherpa-onnx-whisper-tiny.en
cp "$W/tiny.en-encoder.int8.onnx" "$DST/asr/encoder.onnx"
cp "$W/tiny.en-decoder.int8.onnx" "$DST/asr/decoder.onnx"
cp "$W/tiny.en-tokens.txt" "$DST/asr/tokens.txt"
# espeak-ng-data has hundreds of small files; Unity on Android cannot list StreamingAssets, so ship an index
(cd "$DST" && find tts asr -type f ! -name "*.meta" | sort > files.txt)
echo "$(wc -l < "$DST/files.txt") files, $(du -sh "$DST" | cut -f1)"
