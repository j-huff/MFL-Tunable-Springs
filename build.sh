#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
if command -v dotnet >/dev/null; then sdk=dotnet
elif [[ -x .dotnet/dotnet ]]; then sdk="$PWD/.dotnet/dotnet"
else echo 'Install the .NET 8 SDK to build this plugin.' >&2; exit 1; fi
"$sdk" build bepinex/SpringTuner.csproj -c Release --nologo
