# Mars First Logistics Spring Tuner — BepInEx

A BepInEx 6 IL2CPP plugin with a local browser control panel. It tunes Light/Medium/Heavy linear and torsion springs. Each type has stiffness, low/high compression damping, low/high rebound damping, and two crossover speeds. The dropdown edits one type at a time and retains unsaved edits across selections.

## Installed setup

- Loader: **BepInEx 6.0.0-be.791+b782da4, Unity IL2CPP, Windows x64**. This is the Windows game running under Proton; do not use the Linux or Mono loader builds.
- Plugin: `BepInEx/plugins/SpringTuner/MarsFirstLogistics.SpringTuner.dll`.
- Panel/settings: `spring-tuner/panel.py`, `panel.html`, `tuning.txt`.
- The previous standalone `version.dll` is backed up in `spring-tuner/legacy-native/` and is no longer loaded. Never run both implementations together.

Restart the game after installing/updating the plugin:

```sh
./spring-tuner/launch-game.sh
./spring-tuner/open-panel.sh
```

The panel is at <http://127.0.0.1:8765>. Refresh an already-open panel after upgrading. Load and unpause a vehicle, choose a spring type, and use **Apply all changes live**. Settings persist when the panel closes. **Restore vanilla for all springs** restores the original drive values; **Load selected spring defaults** only changes that type's draft controls.

For normal Steam launching, replace the old mod's `version=n,b` launch option with:

```sh
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Preserve unrelated launch options. The supplied launch script already uses this override. It does not change Steam settings. Do not launch a second instance while the game is running.

The first BepInEx launch generates `BepInEx/interop`; later launches use that cache. Check `BepInEx/LogOutput.log` for `Loading [Mars First Logistics Spring Tuner 1.0.0]` and `Spring Tuner loaded: six spring families`. The panel identifies the running backend as **BepInEx connected**. Old native feedback is explicitly labeled as old feedback.

## Installing on another copy

1. Install the [official BepInEx Unity IL2CPP Windows x64 build](https://builds.bepinex.dev/projects/bepinex_be) into the game directory. The validated loader package here is build 791. Follow the [IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).
2. Start the game once with the `winhttp` override to generate the interop assemblies, then close it.
3. Extract `dist/SpringTuner-BepInEx.zip` into the game directory. It contains the plugin and companion panel, not BepInEx or any game assemblies. Existing `tuning.txt` is not overwritten.
4. Start the game and panel as above.

Only this game's current build is accepted: `GameAssembly.dll` SHA-256 `c687618a07b873008a1b045f39d1d8157de35737efaa7138ccc586026200fdc8`, Unity 2022.3.62f2. An unknown hash is rejected before patching. The plugin needs regeneration/revalidation after a game update. Only the game's articulation physics path is supported.

## Implementation

This is a C# `BasePlugin`, not a native DLL loaded by a wrapper. Harmony postfixes on `SpringSO.Init` and `HingeSO.Init` register fully constructed springs. An injected Unity `MonoBehaviour` updates their `ArticulationBody.xDrive` each physics tick. BepInEx/Il2CppInterop supplies the typed game bindings. There are no fixed-address detours, MinHook dependency, manual virtual-table writes, or process attachment.

The plugin filters the supported part IDs, ignores ordinary hinges, removes destroyed bodies, and restores modified drive values on plugin unload. It records each drive's original stiffness, damping and force limit at creation. Disabling restores those exact values while preserving the current drive target, limits and mode. It is intended to be loaded at normal BepInEx startup, before vehicles are created; late hot-loading into an existing vehicle is not supported.

Configuration is still the version-3 `spring-tuner/tuning.txt` protocol, six rows of seven values after the version/enabled flags. Versions 1 and 2 are also accepted and expanded with stock torsion defaults. Invalid config retains the last valid values and reports the error. Config polling and telemetry run on Unity's main thread, including while physics is paused; custom physics changes apply on the next physics tick. BepInEx logs errors to `BepInEx/LogOutput.log`.

## Physics

| Type | Stock stiffness | Stock damping | Part IDs | Speed units |
| --- | ---: | ---: | --- | --- |
| Light Spring | 400 | 30 | 59, 141, 142 | game units/s |
| Medium Spring | 1000 | 60 | 58, 139, 140 | game units/s |
| Heavy Spring | 4000 | 200 | 60, 143, 144 | game units/s |
| Light Torsion Spring | 5000 | 20 | 66 | rad/s |
| Medium Torsion Spring | 20000 | 100 | 65 | rad/s |
| Heavy Torsion Spring | 50000 | 100 | 67 | rad/s |

Speed is derived from spring travel or angular displacement per simulation second, never vehicle speed. Linear shortening selects compression; extension selects rebound. For torsion springs, compression means winding away from the rest angle and rebound means unwinding toward it, in either rotational direction. Angular wraparound is handled before differentiating. New/invalid samples or time gaps over 0.25 s give zero speed for that sample.

Each direction has a crossover (default 0.5). Below 90% of crossover the low-speed coefficient applies; above 110%, the high-speed coefficient applies. Between them the coefficient blends linearly. This is a two-range coefficient model, not a digressive high-speed force-slope model. At rest the previous direction is retained, using low-speed damping.

The force/torque cap is `max(original force limit, chosen stiffness)`, allowing damping with zero stiffness. Restoring vanilla restores the original cap. Controls accept stiffness 0–100000, damping 0–20000, and crossover 0.001–100. These input bounds do not guarantee stable behavior at extreme values.

## Build and tests

Requires .NET 8 SDK and the game's generated `BepInEx/interop` assemblies. The local SDK is available at `spring-tuner/.dotnet/dotnet`; `build.sh` also accepts a system-installed SDK. Source is in `spring-tuner/bepinex/`.

```sh
./spring-tuner/build.sh
./spring-tuner/install-plugin.sh
./spring-tuner/.dotnet/dotnet run --project spring-tuner/bepinex/Tests/Tests.csproj -c Release
python3 -m unittest discover -s spring-tuner -p 'test_*.py' -v
```

Build uses local BepInEx/game assemblies as non-copying references; they are not bundled with the plugin. `GameDir` can be overridden using `dotnet build ... -p:GameDir=/absolute/game/path`.

Validation so far: official BepInEx started and generated bindings successfully under Proton; plugin builds with zero warnings/errors; managed physics/migration tests and nine panel/API/dropdown tests pass. Live plugin/vehicle validation is pending the requested restart. Earlier native implementation tests and artifact analysis remain in `evidence/`; those are not claimed as validation of this rewrite.

## Remove or disable

Use **Restore vanilla for all springs**, close the game, and remove `BepInEx/plugins/SpringTuner`. Keep BepInEx if other mods use it. The companion panel/settings directory can be retained or removed separately. Original game executables, assets and saves were not patched. Do not restore the legacy native loader while the BepInEx plugin is installed.
