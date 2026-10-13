# Living Cities for Daggerfall Unity

An in-development standard DFU mod for persistent residents, daily routines, civilian awareness, finite guards, nightlife, and optional local dialogue.

The user approved the scope and recommended architecture on 13 October 2026 and requested continuous implementation, testing, and improvement. The working name is Living Cities.

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. The simulation core has no Unity dependency. The canonical source is this repository, not the ignored copy deployed under DFU's Assets/Game/Mods directory.

See [milestones](docs/MILESTONES.md), [approved design](docs/superpowers/specs/2026-10-13-living-cities-design.md), and [foundation plan](docs/superpowers/plans/2026-10-13-population-foundation.md).

This is experimental software. Use the isolated portable player and dedicated test saves. Individual milestone completion is recorded only after its acceptance checks pass.

## Verified foundation

`dotnet run --project tests/LivingCities.Tests` passes 57 behavior tests: household identity/capacity, persistent deaths, deterministic weighted paths, analytical schedules, save round trips, atomic restore rejection, and bounded settings without regenerating existing residents.

The standard `livingcities.dfmod` builds, reopens, and initializes alongside Basic Roads in the isolated player. Unity's integration verifier exercises DFU's real FullSerializer and the mod save adapter. The runtime adapter currently displays virtual home/sleep counts with F8. Physical NPCs, live commutes, and actual save-slot continuity remain pending.

The native mod settings menu provides six working foundation controls: simulation enable, maximum initial household size (1–3, new settlements only), wake/sleep hours, refresh interval, and F8 diagnostics availability. Settings load through DFU's live callback and are validated again at runtime. More controls accompany their gameplay systems; city-overhaul and market-vendor patches follow vanilla stability. See `docs/SETTINGS-AND-COMPATIBILITY.md`.

Canonical source lives here; `tools/Deploy.ps1` copies it into the upstream project's ignored mod folder. To build the installed mod, run Unity with `-batchmode -nographics -quit -projectPath` pointing to the sibling `daggerfall-unity` checkout and `-executeMethod LivingCitiesBuild.VerifyAndBuild`. Preserve the log with `-logFile`. Run `tools/SmokeTest.ps1` for isolated headless startup checks; it stops only its owned process.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, and sibling `../work/livingcities-build.log`. Build artifacts and machine-local logs are ignored by Git. `docs/INTEGRATION.md` records known engine limitations and deferred checks.

