#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
if [[ ! -f ../BepInEx/core/BepInEx.Unity.IL2CPP.dll ]]; then
  echo 'Install BepInEx 6 Unity IL2CPP (Windows x64) in the game folder first.' >&2; exit 1
fi
if [[ -f ../version.dll ]]; then
  echo 'Remove the old Spring Tuner version.dll loader before installing. Do not run both backends.' >&2; exit 1
fi
plugin=bepinex/bin/Release/net6.0/MarsFirstLogistics.SpringTuner.dll
[[ -f "$plugin" ]] || { echo 'Run ./spring-tuner/build.sh first.' >&2; exit 1; }
mkdir -p ../BepInEx/plugins/SpringTuner
cp "$plugin" ../BepInEx/plugins/SpringTuner/MarsFirstLogistics.SpringTuner.dll.new
mv ../BepInEx/plugins/SpringTuner/MarsFirstLogistics.SpringTuner.dll.new ../BepInEx/plugins/SpringTuner/MarsFirstLogistics.SpringTuner.dll
printf 'Plugin installed. Restart the game to load it.\n'
