#!/bin/zsh
# usage: ./run_preview.sh <seed> [vox] [views]
set -e
cd "$(dirname "$0")"
SEED=${1:-7}; VOX=${2:-0.02}; VIEWS=${3:-wearer,outside,up}
python3 network.py seed=$SEED
python3 field.py ../../build/burrow/network_$SEED.json vox=$VOX faces=220000
/Applications/Blender.app/Contents/MacOS/Blender -b -P preview.py -- mesh=../../build/burrow/network_${SEED}_mesh.ply net=../../build/burrow/network_$SEED.json out=../../build/burrow/preview_$SEED views=$VIEWS samples=40 pct=55 2>&1 | grep -E "rendered|Error|Traceback|error" || true
