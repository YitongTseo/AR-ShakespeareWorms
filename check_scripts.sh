#!/bin/zsh
# Fast type-check of all runtime scripts with Unity's own Roslyn (seconds, vs. minutes for a build).
cd "$(dirname "$0")"
U=/Applications/Unity/Hub/Editor/6000.0.79f1/Unity.app/Contents
RSP=/tmp/burrow_check.rsp
{ ls $U/Managed/UnityEngine/UnityEngine*.dll | sed 's/^/-r:/'
  echo "-r:$U/NetStandard/ref/2.1.0/netstandard.dll"
  echo "-r:Library/ScriptAssemblies/Unity.XR.Hands.dll"
  echo "-r:Library/ScriptAssemblies/Unity.XR.Management.dll"
  echo "-r:Library/ScriptAssemblies/Unity.XR.OpenXR.dll"
  echo "-r:Library/ScriptAssemblies/Unity.XR.MetaOpenXR.dll"
  ls Assets/Scripts/*.cs Assets/Scripts/*/*.cs; } > $RSP
$U/NetCoreRuntime/dotnet $U/DotNetSdkRoslyn/csc.dll -nologo -noconfig -nostdlib+ -target:library -langversion:9 \
  -out:/tmp/burrow_check.dll -nowarn:0168,0219,0414,0649,0109,1701,1702 -define:UNITY_ANDROID @$RSP 2>&1 | grep -E "error|warning CS0" > /tmp/burrow_check.txt
if [ -s /tmp/burrow_check.txt ]; then cat /tmp/burrow_check.txt; echo "check FAILED"; exit 1; fi
echo "check ok"
