#!/bin/zsh
# Build libwormspeech.so (arm64 Android) against the bundled sherpa-onnx libs and header,
# and stage all three native libraries where Unity packages them.
set -e
cd "$(dirname "$0")/../.."
NDK=/Applications/Unity/Hub/Editor/6000.0.79f1/PlaybackEngines/AndroidPlayer/NDK
CLANG=$NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin/clang
LIBS=build/sherpa-android/jniLibs/arm64-v8a
OUT=Assets/Plugins/Android/libs/arm64-v8a
mkdir -p "$OUT"
"$CLANG" --target=aarch64-linux-android29 -fPIC -shared -O2 -fvisibility=hidden \
  -I build/sherpa-api tools/speech/wormspeech.c \
  -L "$LIBS" -lsherpa-onnx-c-api -o "$OUT/libwormspeech.so"
cp "$LIBS/libsherpa-onnx-c-api.so" "$LIBS/libonnxruntime.so" "$OUT/"
ls -la "$OUT"
"$NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin/llvm-nm" -D --defined-only "$OUT/libwormspeech.so" | grep " T ws_"
