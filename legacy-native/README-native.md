> Historical documentation for the retired native mod. The web panel and its launcher have been removed; use the BepInEx mod described in the main README.

# Mars First Logistics — live spring tuner

A native mod and local browser control panel for Light, Medium and Heavy Springs and Torsion Springs. Each family has stiffness, low-speed compression, high-speed compression, low-speed rebound, and high-speed rebound damping, plus separate compression/rebound shaft-speed crossovers. Normal, compressed and extended variants share their family's settings. Select one of the six types using the dropdown; edits stay in place when switching types.

## Use

The mod is installed as `version.dll` beside the game executable. No original game binaries or save files are patched.

1. Launch the game with `./spring-tuner/launch-game.sh` from the game directory. This uses your existing Steam Proton Experimental installation and existing save/profile.
2. Start the controls with `./spring-tuner/open-panel.sh`, or `python3 spring-tuner/panel.py`. Open <http://127.0.0.1:8765> if the browser does not open automatically.
3. Load a vehicle with springs and unpause. Choose a spring type, enter values and click **Apply live**. Apply saves all edited types; the dropdown displays one type at a time. Existing and newly constructed springs update without rebuilding the vehicle. Updates are read within about 250 ms of active spring physics; feedback refreshes once per second.
4. **Restore vanilla physics** removes overrides on the next physics update. **Load selected defaults** only resets the selected type’s controls; Apply is still required. Restoring vanilla affects all six types.

To launch normally through Steam instead of the supplied script, set this game's Properties → Launch Options to:

```sh
WINEDLLOVERRIDES="version=n,b" %command%
```

Preserve any existing launch options/overrides when combining them. The supplied launcher does not change Steam settings. Do not launch a second game instance while one is running.

Settings are saved in `tuning.txt` and persist after the panel closes. Closing the panel does not disable the mod. Pause, a stopped game, or a vehicle without springs prevents fresh physics telemetry; the panel reports that changes may be pending. The panel uses only Python's standard library and binds to localhost.

## Physics behavior

| Family | Stock stiffness | Stock damping, both directions | Part IDs |
| --- | ---: | ---: | --- |
| Light | 400 | 30 | 59, 141, 142 |
| Medium | 1000 | 60 | 58, 139, 140 |
| Heavy | 4000 | 200 | 60, 143, 144 |
| Light Torsion | 5000 | 20 | 66 |
| Medium Torsion | 20000 | 100 | 65 |
| Heavy Torsion | 50000 | 100 | 67 |

Stock values and part IDs come from the shipped assets.

The mod hooks `Spring.FixedUpdate` and computes shaft velocity from the change in the game's normalized spring distance divided by elapsed Unity physics time (`Time.fixedTimeAsDouble`). This measures spring compression/extension, not vehicle movement. Speed is in game distance units per simulation second; physical meter calibration has not been established.

Negative velocity selects compression, positive selects rebound. Each direction has its own low/high coefficients and crossover (default 0.5 units/s). Below 90% of the crossover, the low-speed coefficient applies; above 110%, the high-speed coefficient applies. Between those speeds the coefficient blends linearly, avoiding a force discontinuity. This is a two-range damping coefficient model, not a digressive high-speed force-slope model. Force remains subject to the articulation drive's cap.

At near-zero speed the previous direction is retained, using low-speed damping. New springs start in low-speed rebound. Missing/invalid samples or elapsed simulation gaps over 0.25 s yield zero speed for that sample. Wall-clock time is only used for file polling and stale-object cleanup, never as the shaft-velocity denominator. The game's distance function handles reversed installations.

Torsion springs use `HingeArticulation` rather than `Spring`. The mod installs a validated per-class FixedUpdate virtual slot when the first torsion spring is constructed; ordinary hinges retain their original behavior. Rotational X-drive position is sampled in radians, with wraparound handled before differentiating over physics time. Crossover units are **rad/s** (0.5 rad/s ≈ 28.6°/s). Compression means winding farther from the target angle; rebound means unwinding toward it, for either rotational direction. Only the articulation physics path is supported.

Version 3 config stores seven values for each of six families: stiffness, low compression, high compression, low rebound, high rebound, compression crossover, rebound crossover. The panel migrates v1/v2 settings without changing linear tuning and adds stock torsion settings. The installed v2 settings are backed up as `tuning-v2.backup.txt`. Refresh the browser panel and restart the game once after upgrading the DLL. Older loaded DLLs are explicitly identified by the panel and will not read v3 settings.

Only the articulation X-drive stiffness, damping, and force limit change. Limits, targets, drive mode, vehicle geometry, and SO assets remain intact. Force limit is `max(stock stiffness, chosen stiffness)`: it grows with higher stiffness while retaining enough capacity for damping when stiffness is reduced to zero. Restoring vanilla restores stock stiffness, damping and force cap.

Inputs allow stiffness 0–100000, damping 0–20000, and crossovers 0.001–100 units/s. Those are input bounds, not a guarantee of stable vehicle behavior at extremes. The game still imposes articulation force limits.

## Compatibility and evidence

Built for the installed Windows x64 IL2CPP game (Unity 2022.3.62f2, asset version `v202610030813`) running under Linux/Proton. Startup checks the complete `GameAssembly.dll` SHA-256 and hook prologue, refusing to patch an unknown build:

```
c687618a07b873008a1b045f39d1d8157de35737efaa7138ccc586026200fdc8
```

A game update may require updating and revalidating the mod. `status.json` reports a rejected build. The DLL forwards Windows version APIs and only instruments `Mars First Logistics.exe`, not UnityCrashHandler.

`evidence/` contains metadata and asset extraction scripts, native assembly excerpts, the finding ledger, and runtime validation. REA successfully identified the native target; Hopper analysis timed out, so detailed findings were obtained with local metadata parsing, objdump, and UnityPy. They are local artifact evidence, not fabricated REA decompiler results.

Verified live on four Light Springs: stiffness 500, compression 40, rebound 70; observed both damping branches and successful restore to 400/30. Medium/Heavy and their variants are identified from shipped assets and use the same hook, but have not individually been exercised live. Version 3 native tests cover linear/angular velocity, angle wraparound, winding direction, all four damping regions, crossover blending and invalid time samples; nine panel tests cover validation, API writes, v1/v2 migration, dropdown switching and preservation of edits. Live torsion validation is pending a restart.

During development, Frida attachment caused the initial Proton process to exit. That approach was discarded; the delivered mod does not use Frida. The native loader was subsequently tested with the running game.

## Build and test

Source: `src/mod.c`, `src/version.def`. Hook library: [MinHook v1.3.4](https://github.com/TsudaKageyu/minhook/tree/v1.3.4), vendored at commit `c3fcafdc10146beb5919319d0683e44e3c30d537` with its license. Unity's [ArticulationDrive API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ArticulationDrive.html) describes the solver fields.

The existing `.venv` contains Zig's Windows cross compiler. To recreate it:

```sh
uv venv --python 3.12 spring-tuner/.venv
uv pip install --python spring-tuner/.venv/bin/python ziglang==0.17.0
./spring-tuner/build.sh
python3 -m unittest discover -s spring-tuner -p 'test_*.py' -v
gcc -Wall -Wextra -Werror spring-tuner/src/test_damping.c -lm -o /tmp/mfl-test-damping
/tmp/mfl-test-damping
```

The build writes `spring-tuner/version.dll`; install it as the root `version.dll` while the game is closed. Do not overwrite a different mod loader without integrating the loaders first. Only this mod's DLL was present when installed here.

## Remove

Restore vanilla in the panel, exit the game, then remove the root `version.dll` installed by this mod and remove the Steam `version=n,b` override if you added it. The `spring-tuner` directory can then be deleted. Keep it if you want the source, settings or evidence. Original game files and saves require no restoration.

Physics time reference: [Unity Time.fixedTime](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Time-fixedTime.html).

Angular-position units: [Unity ArticulationBody.jointPosition](https://docs.unity3d.com/ja/2022.3/ScriptReference/ArticulationBody-jointPosition.html).
