#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
game_dir="$PWD"
steam_dir="$(cd ../.. && pwd)"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="$(cd "$steam_dir/.." && pwd)"
export STEAM_COMPAT_DATA_PATH="$steam_dir/compatdata/1532200"
export SteamAppId=1532200 SteamGameId=1532200
export WINEDLLOVERRIDES="winhttp=n,b${WINEDLLOVERRIDES:+;$WINEDLLOVERRIDES}"
exec "$steam_dir/common/SteamLinuxRuntime_4/_v2-entry-point" --verb=waitforexitandrun -- "$steam_dir/common/Proton - Experimental/proton" waitforexitandrun "$game_dir/Mars First Logistics.exe"
