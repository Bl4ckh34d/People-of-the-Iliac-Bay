# Profiling in the Unity editor

Run `tools/ProfileInEditor.ps1 -RunName baseline` with the game and project editor
closed. It opens the native startup scene in play mode, uses a copied morning
fixture, and copies the player's mod order/settings into the isolated **EditorData**
directory. The virtual mod manifest must include the already baked navigation
assets; missing navigation aborts measurement rather than benchmarking fallback
movement. It does not install a package or save the character.

Focused modes use the same isolated launcher: `-ValidateArrival` checks native
outdoor arrival and shared doors; `-ValidateWardrobe` checks native outfit slots;
`-ValidateCrowd` reconstructs six overlapping bodies and checks a sign-obstructed
flight corridor. `-FixtureFolder SAVE1 -ValidateShopping` enters the nearest
native shop and checks nonbuyer and buyer paths, saved progress, counter payment
and retained inventory. `-ValidatePlaytest` adds inventory ownership, personal
trade and incremental block parity after the normal loiter measurements.
`-ShoppingBuyerOnly` with `-ValidateShopping` repeats just the buyer when a
nonbuyer has already passed. Shop restore checks retain their spatial tolerance
while accounting for the native clock's one-second observation interval.
Add `-ValidateSpeech` with `-ValidatePlaytest` to reopen the trade conversation
and pass a controlled returning greeting through real local TTS. This requires
the configured voice service and checks completed nonzero mixer output; it does
not validate a language model's spontaneous wording.
Do not interpret a fixture setup failure or timeout as gameplay acceptance.

`-ValidateComplaint` starts an exterior direct-threat complaint from a controlled
issued speech receipt, then observes actual travel, saved body position, a
mid-route snapshot round trip and finite guard dispatch. The incident position
must pass physical clearance and guard-response routing before the test begins.
After verifying dispatch, it clears the controlled native crime and observes ten
seconds of routine movement without jumps, restarted complaints or extra guards.
This isolates resumption from the guard confrontation; it does not test arrest
or escape resolution.
It does not exercise free-text model classification or native disk save/load.
Add `-ValidateComplaintWait` to fault-inject an unreachable response location,
verify five seconds of standing/facing without a charge, then restore the real
incident and verify delivery. This option requires `-ValidateComplaint`; it does
not claim a naturally blocked door or path was reproduced.
See the [physical complaint audit](../../docs/audits/2026-10-26-physical-dialogue-complaint.md).

`-ValidateMurderReport -ValidateMurderReportChat` uses the real local model and
native Talk handler instead of controlled acceptance, holds the accepted request
in Talk for 25 seconds, then requires physical report delivery. Start the owned
speech coordinator against that run's isolated `data` directory during editor
loading. Exact opt-in classifier traces are written below its `work` directory.
See [setup and verification boundaries](../../docs/audits/2026-10-26-murder-report-dialogue.md).

Each 20-second observation phase and native 1/3-hour loiter records its own CPU
`.raw` under `work/playtest-2026-10-26/editor/<RunName>`. Deep Profiling stays off.
`-CountersOnly` disables binary profiling for a second comparison without capture
I/O overhead. Captures are stopped at approximately 256 MiB per phase; counters
continue, and a single large frame can overshoot that limit. `-Overnight` adds
12 hours as native-limit-sized chunks; native encounters/popups can interrupt it.
An interrupted run is not a successful overnight test.

`-MeasureStreetDensity -CountersOnly` keeps the observer at the copied save's
location and samples 09:10, 10:15, 12:00 and 15:00. Each phase records an immediate
sample and another after 30 real seconds. `STREET_DENSITY` separates scheduled
outdoor/indoor activities, home/visit counts, nearby eligible residents, actual
active bodies, standing bodies, family/action ownership and deferred spawns.
`activeBodies` means loaded managed residents, not camera-visible people or
native guards. Initial spawn deferrals after a jump are expected; compare the
settled samples. This is a clock-jump population probe, not a native loiter or
Player performance benchmark. See the [density audit](../../docs/audits/2026-10-26-street-density.md).

`tools/SummarizeEditorProfile.ps1 -RunDirectory <directory>` exports phase and
section counters as JSON/CSV, preserving the completion/error status. To inspect
existing raw captures without running the game, set `POTIB_EDITOR_PROFILE_ROOT`
to their directory and launch this project with
`-executeMethod PeopleOfTheIliacBayEditorProfile.Analyze`. This exports native
CPU hierarchy nodes of at least 10 ms in frames of at least 100 ms.

DFU 1.1.1's editor effect enumeration needs one guard when mod source shares
`Assembly-CSharp`: in `Assets/Scripts/Game/MagicAndEffects/EntityEffectBroker.cs`,
`ReflectiveEnumerator.GetEnumerableOfType<T>` must exclude
`myType.ContainsGenericParameters` alongside abstract classes. An open generic
`ResidentDisease<T>` is not constructible. Released bundles use a separate
assembly and do not need this host-source guard. The local Unity project has this
guard; it does not change the installed game executable.

DFU also disables `RMBLayout`'s 12-location block cache in the editor. This made
an interior survey reread an entire city, allocating about 500 MB and taking
five seconds in the initial diagnostic. Those measurements are **not evidence
of the same cost in a release player**. The local host source now declares
`maxLocationCacheSize` in both builds and admits the existing cache insertion
in an editor only when `-peopleoftheiliacbay-editor-profile` is present. Native
cache eviction and variant invalidation remain unchanged. The launcher checks
for this opt-in guard. These two host-source changes live in the adjacent DFU
checkout, not in the shipped mod.

Treat editor frame timings as diagnostic, not release-player performance.
Keep the same fixture, mod list, camera, phase durations and profiler settings
between comparisons. Preserve failed runs separately and exclude them from
performance conclusions.

`-ClimateDetails` adds scoped Harmony timing and native CPU samples to the normal
loiter profile. It observes location/mesh/ground climate updates, billboard
materials, replacement textures, disk imports and mod asset lookup. Original
results and exceptions remain unchanged; the hooks are removed after the probe.
Focused fixtures that bypass loiter reject this option. `-ValidatePlaytest` can
still follow the normal profile. Compile QuestHooks before launch: deployment
copies its existing DLL, while a package build compiles it automatically.

Durations are inclusive and nested; do not sum parents and children. Compare
binary CPU capture with `-CountersOnly`, and keep instrumentation identical for
optimization comparisons. Neither variant proves release-player performance:
DFU's editor-only virtual-mod `HasAsset` scans the manifest, whereas a packaged
mod uses `AssetBundle.Contains`. Investigate that difference before changing
production material refresh or caching behavior. See the
[native climate investigation](../../docs/audits/2026-10-26-native-climate-profile.md).
