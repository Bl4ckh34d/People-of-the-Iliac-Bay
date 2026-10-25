# Native climate and time-skip profiling — 26 October 2026

This is a diagnostic checkpoint, not a production climate optimization. The
installed bundle remains the housing-cache build from `d3b8cca`.

## Method

`ProfileInEditor.ps1 -RunName climate-native-cpu -ClimateDetails` uses the same
copied SAVE82, mod order/settings, native navigation and player-equivalent
location cache as `housing-after`. Deep Profiling is disabled. Explicit scoped
Harmony hooks add timing and CPU samples without replacing native behavior.
Normal play installs none of these hooks.

The completed raw captures were analyzed using
`PeopleOfTheIliacBayEditorProfile.Analyze`. Frame 98 in `loiter-3h.raw.csv` takes
1,625.10 ms. Its largest native `DaggerfallLocation.Update` call takes
1,153.20 ms, including 1,153.10 ms in `ApplyClimateSettings`. Other loaded
locations update in the same frame. The complete frame allocates 14,171,051
managed bytes; this is not a per-texture allocation measurement.

## Initial attribution

Three-hour loiter section totals from `climate-native-cpu/sections.csv`:

| Native scope | Calls | Total | Maximum |
| --- | ---: | ---: | ---: |
| Location climate | 7 | 1,601.78 ms | 1,153.10 ms |
| Mesh climate | 97 | 4.13 ms | 0.20 ms |
| Material climate | 1,829 | 2.74 ms | 0.05 ms |
| Billboard material | 1,587 | 1,592.44 ms | 2.01 ms |
| Billboard replacement | 1,587 | 1,565.90 ms | 1.98 ms |

These timings are inclusive and overlap. They identify repeated billboard
replacement work as the dominant measured scope, not mesh material changes or
NPC route calculation. The wall-clock frame sampler reports 1,621.51 ms for
this phase; its sampling boundary differs from Unity's CPU frame boundary.

`climate-native-counters` repeats the fixture without binary capture and adds
texture-load/disk timing. It completes with the same game times and twelve
residents after the final skip. The three-hour maximum remains 1,615.57 ms:
1,599.32 ms in location climate, 1,565.07 ms in billboard replacement, 1,276.31
ms in texture load/cache/import, and only 53.23 ms across 1,624 disk-import
checks. These are overlapping totals, not independent costs. No gen-0 collection
occurs during that skip. Binary recording is not required to reproduce the
spike; disk import checks alone do not account for it.

## Release-player boundary

Native `DaggerfallLocation.ApplyClimateSettings` refreshes every non-nature
billboard on a city-light/season change. `GetStaticBillboardMaterial` attempts
replacement loading; native successful texture loads are cached, but a missing
replacement can repeat the lookup. Source inspection also identifies a material
editor/player difference: `Mod.HasAsset` scans `ModInfo.Files` for virtual mods
under `UNITY_EDITOR`; the release path uses `AssetBundle.Contains`.

That difference must be measured before treating the editor spike as a shipped
game bottleneck. Do not add a permanent negative texture cache or suppress
same-argument material refresh solely from these measurements: native APIs can
intentionally refresh state, and mod assets may change. No GPU workload has
been established.

The additional `climate-virtual-lookup` counters-only run measures 3,952
`Mod.HasAsset` calls taking 1,486.13 ms during the three-hour skip. Location
climate takes 1,596.88 ms, billboard replacement 1,562.64 ms, and disk-import
checks 53.79 ms. The largest sampled frame is 1,613.64 ms. This strongly
implicates virtual-mod manifest lookup in this editor fixture. These totals
include other calls within the phase; they are not an exact subtraction of
individual child calls from the largest frame. A packaged-player measurement
is required before claiming this cost exists in the installed game.

The original two runs and the extra lookup run use different diagnostic marker
sets; their similar durations corroborate attribution, not an optimization
benchmark. No production behavior was changed. QuestHooks and RuntimeCompile
build without warnings/errors; Core 2121/2121, source policy, tooling checks and
incompatible-mode rejection pass. The installed bundle hash remains
`8E9FEDB6437F7338AB5937D498B5AFA3E275BC15D89F1361643E88AAA02F3F76`.

All three runs report completion and no exception entries. They retain the
existing scene-teardown warning about objects not being cleaned up; this work
does not resolve it. Owned editor processes exit; Unity Hub is left running.

The reported four-to-six-second Player freezes remain open. The independently
verified housing startup repair removes one measured cause; this investigation
does not establish the cause of every remaining freeze.
