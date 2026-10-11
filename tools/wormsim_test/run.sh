#!/bin/sh
# Compile the WormSim port + this harness with Unity's bundled Mono compiler
# and run it. No UnityEngine reference: the port is plain .NET.
#
#   tools/wormsim_test/run.sh <asset.hwrm> <n_ticks> [state_every] [embed]
#
# Set WORMSIM_NOBUILD=1 to skip recompiling.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
MONO_BIN=${MONO_BIN:-/Applications/Unity/Hub/Editor/6000.0.79f1/Unity.app/Contents/MonoBleedingEdge/bin}
OUT="$HERE/bin"
EXE="$OUT/wormsim_test.exe"

if [ -z "$WORMSIM_NOBUILD" ]; then
    mkdir -p "$OUT"
    # -optimize+ so the loop code resembles a release build; mcs never fuses
    # multiply-adds either way, which is what keeps the arithmetic bit-exact.
    "$MONO_BIN/mcs" -nologo -optimize+ -debug- -warn:4 -target:exe -out:"$EXE" \
        "$ROOT"/Assets/Scripts/WormSim/*.cs "$HERE/Program.cs"
fi

exec "$MONO_BIN/mono" "$EXE" "$@"
