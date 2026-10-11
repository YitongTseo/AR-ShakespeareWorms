#!/bin/zsh
# Batch-build the Quest APK: ./build_quest.sh   (log in Logs/build.log)
cd "$(dirname "$0")"
mkdir -p Logs
# AR Foundation's XR Simulation build processor moves two settings assets to Assets/XR/Temp
# before a build and back after; a failed build leaves them there. Put things straight first.
if [ -d Assets/XR/Temp ]; then
  mkdir -p Assets/XR/UserSimulationSettings/Resources Assets/XR/Resources
  [ -f Assets/XR/Temp/XRSimulationPreferences.asset ] && mv Assets/XR/Temp/XRSimulationPreferences.asset* Assets/XR/UserSimulationSettings/Resources/
  [ -f Assets/XR/Temp/XRSimulationRuntimeSettings.asset ] && mv Assets/XR/Temp/XRSimulationRuntimeSettings.asset* Assets/XR/Resources/
  rm -rf Assets/XR/Temp Assets/XR/Temp.meta
fi
rm -rf Library/PlayerDataCache
/Applications/Unity/Hub/Editor/6000.0.79f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -projectPath "$PWD" -executeMethod ARBuild.Build -logFile Logs/build.log
code=$?
echo "unity exit $code"
grep -E "error CS|Shader error|\[ARBuild\] Result|Exception|BuildFailed" Logs/build.log | head -40
exit $code
