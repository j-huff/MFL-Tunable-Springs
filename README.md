# Mars First Logistics Spring Tuner — in-game edition

BepInEx 6 IL2CPP plugin adding **per-part suspension tuning** to the game's **Configure controls** menu. Supports Light/Medium/Heavy Springs and Torsion Springs, including compressed/extended linear variants.

The previous web-controlled version was committed before this rewrite: **`0df54b4`** in this `spring-tuner` Git repository. The legacy web panel and global tuning files have been removed from the current tree; they remain available in that commit.

## Use

1. Restart the game using `./spring-tuner/launch-game.sh`.
2. Enter the vehicle editor and select **Configure controls**.
3. Hover over a spring and use the normal **Configure controls** action shown by the game. The mod uses your existing keyboard/controller binding.
4. Adjust the sliders in the existing part-controls popup:
   - Stiffness
   - Low-speed compression damping
   - High-speed compression damping
   - Low-speed rebound damping
   - High-speed rebound damping
   - Compression speed crossover
   - Rebound speed crossover
5. Close the popup normally. Changes belong to **that spring instance**, not every spring of its type. Slider edits in the popup are grouped into an undo step.

**Apply All** copies the displayed settings to all matching springs on the current vehicle. It matches the spring family: Light, Medium and Heavy remain separate, torsion and linear remain separate, and compressed/extended variants count with their corresponding linear family. Other vehicles are unaffected. The bulk action is independently undoable. Clicking it plays the same sound as opening the controls menu, including when the matching springs already have those settings.

**Reset spring** clears only the selected spring's override. Untuned parts retain stock behavior. There is no global enable or Apply button. Editing in the vehicle editor affects the rebuilt vehicle when returning to driving; if the same live node is present, tuning applies at its next physics tick.

The sliders reuse the game's native settings-slider widgets and focus/input handling. The stock menu remains responsible for closing the popup and for all ordinary part controls. No browser, external panel, or separate in-game overlay is used.

## Persistence

Per-part values are attached to the native vehicle node using weak handles, avoiding native-pointer reuse and unintended lifetime retention. Tuning follows Saved designs → Save to slot / Load vehicle, base64 game-save paths, copies and duplicates. Slot saves include tuning in the design PNG; loading tolerates its extra pixel padding. Renaming a saved design preserves that design’s tuning. Undo/redo uses additional snapshots because the game serializes undo history differently.

The save extension is a bounded, versioned JSON trailer with checksum and a topology signature. Original vehicle bytes, names and control bindings stay unchanged. Settings are matched by tree position, attachment slot and part ID, never by transient memory addresses. Invalid or mismatched extensions are ignored without preventing the base vehicle from loading.

Saving a tuned vehicle **without the mod** loses the tuning extension; the base game ignores the extra data. Keep the mod enabled when editing/saving vehicles whose tuning you want to retain. Multiplayer synchronization has not been implemented or validated.

## Install and launch

Installed plugin: `BepInEx/plugins/SpringTuner/MarsFirstLogistics.SpringTuner.dll` (version 2.1.1).

Loader used here: **BepInEx 6.0.0-be.791+b782da4, Unity IL2CPP, Windows x64**. This is a Windows game running under Proton, so use the Windows IL2CPP loader. [Official loader builds](https://builds.bepinex.dev/projects/bepinex_be) and [installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).

```sh
./spring-tuner/launch-game.sh
```

For launching through Steam instead of the script:

```sh
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Do not restore the old standalone `version.dll`; running both backends is unsupported. Existing loader/source backups remain in `legacy-native/`.

`dist/SpringTuner-BepInEx.zip` contains the plugin, launcher and this README. Extract into the game directory after installing BepInEx. It includes no game assemblies, saved vehicles, web server, or user tuning files. The first BepInEx launch generates its interop bindings.

## Physics

Speed means suspension travel or rotation per simulation second, never vehicle speed. Linear compression is shortening; rebound is extension. Torsion compression is winding farther from the rest angle, rebound is unwinding toward it, in either direction. Torsion crossovers use **rad/s**; linear crossovers use game distance units/s.

Below 90% of the selected crossover the low-speed coefficient applies. Above 110%, the high-speed coefficient applies. Between them it blends linearly. This is a two-range damping-coefficient model, not a digressive force-slope model. New samples, long simulation gaps and invalid timing yield zero shaft speed; angular wraparound is handled. At rest the previous direction is retained.

The force/torque cap is `max(original force limit, chosen stiffness)`. Zero stiffness therefore still permits damping. Reset and plugin unload restore the original drive stiffness, damping and force cap while preserving its target, limits and mode.

| Type | Stock stiffness | Stock damping |
| --- | ---: | ---: |
| Light Spring | 400 | 30 |
| Medium Spring | 1000 | 60 |
| Heavy Spring | 4000 | 200 |
| Light Torsion Spring | 5000 | 20 |
| Medium Torsion Spring | 20000 | 100 |
| Heavy Torsion Spring | 50000 | 100 |

Input ranges: stiffness 0–100000; damping 0–20000; crossover 0.001–100. These bounds do not guarantee physical stability at extremes.

## Compatibility and testing

Supported `GameAssembly.dll` SHA-256:

```
c687618a07b873008a1b045f39d1d8157de35737efaa7138ccc586026200fdc8
```

This corresponds to the installed Unity 2022.3.62f2 build. Unknown builds are rejected before patching. Only articulation physics is supported. The plugin must load at normal BepInEx startup, before vehicles are created.

Build: zero warnings/errors. Managed tests cover physics, separate same-ID part values, value-copy isolation, save-trailer round trips, untouched native bytes/names, legacy vehicles, topology checks, truncation and corrupt data. The user confirmed the native Configure controls popup opens successfully. A live slider-maximum rounding error was found and fixed; tests now validate every slider at 10,001 points including both endpoints. Apply All has vehicle-scope/type/variant selection tests. The user confirmed Apply All works. Version 2.1.1 fixes the Saved designs save path and padded PNG decoding; live slot save/load, the new sound, and undo/redo still require verification after restart. Investigation details and static evidence are in `evidence/`.

An opt-in developer integration check is available: create `spring-tuner/.run-integration-checks` before launching. Once a spring is loaded, checks run on detached node copies and log their result. They do not alter live nodes or disk saves. The request file is consumed once. These cover native same-ID isolation, copy, clone, named saves and base64 saves; they do not replace visual/menu or undo/redo checks.

## Build

Requires .NET 8 SDK and this game's generated BepInEx interop assemblies. A local SDK is available in `.dotnet`; `build.sh` also finds a system SDK.

```sh
./spring-tuner/build.sh
./spring-tuner/.dotnet/dotnet run --project spring-tuner/bepinex/Tests/Tests.csproj -c Release
./spring-tuner/install-plugin.sh
```

Source is in `bepinex/`. Inspect changes relative to the old mod using `git -C spring-tuner diff 0df54b4`. Build references game/loader assemblies without copying them into the plugin package.

## Remove

Close the game and remove `BepInEx/plugins/SpringTuner`. Keep BepInEx if other mods use it. Original executables/assets and saves are not patched on disk. Removing the mod leaves vanilla spring physics; preserve tuned saves separately if you intend to reinstall later.
