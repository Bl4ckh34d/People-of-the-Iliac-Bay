# Incremental loading and conceptual extraction checkpoint

## Native loading repair

The previous saved-city test measured 1007.27 ms inside eager `RMBLayout.GetLocationBuildingData` and a 1014.88 ms frame. The mod now loads and enriches one native block per coroutine yield through `IncrementalLocationBlocks`, using the public block reader and native replacement path. It preserves building naming order, replacement handling and complete-only publication. Cancellation discards partial results. No engine changes or persistent replacement-sensitive cache are required. The copied algorithm carries the DFU MIT license attribution.

The final native parity probe compares all BuildingData fields and block ordering against the native loader for maps 206728581, 1291010263 and 414355624: 12 + 64 + 42 = **118 blocks**. It also verifies one yield per block, unchanged source arrays and cancellation without partial publication. The parity process is separate from performance measurement to avoid warming the measured process with the eager reference.

Installed package SHA-256: `BC3C963D1DE3371F44B6945D8AFB6CD759260D5EC80851D81A3B3FBDFD5BAE33`. Final owned native performance test exits 0 and records:

- Initial/later/approach maximum frames: **103.51 / 112.98 / 56.43 ms**, below the unchanged one-second gate.
- `LIVING_CITIES_PERFORMANCE_PASS`, `LIVING_CITIES_DAILY_ITINERARY_PASS`, `LIVING_CITIES_MURDER_WITNESS_PASS` and final `LIVING_CITIES_TOWN_TEST_PASS`.
- 75 routines, 63 itineraries, 41 multi-stop adults and 10 distinct service buildings in the daily fixture.

Two earlier fixture failures remain preserved. The first required gate assignments in an unwalled fixture with zero native gates. PerformanceProbe now checks completed gate survey and zero posts for this case; existing assignment checks remain for cities with gates. The second timed out waiting for the entire outdoor migration coroutine even though deferred routes legitimately wait for residents to return home. DailyItineraryProbe now waits for public street/entrance/world readiness and persisted itinerary version 1, retaining its existing itinerary, route, household and serialization assertions. The final log has `outdoorMigrationComplete=true`, so this run does not independently demonstrate acceptance while migration remains deferred.

Evidence: `work/incremental-blocks-20261020/final-parity-native.log`, `performance-qwen-first.log`, `performance-qwen-no-gates.log`, `performance-qwen-readiness.log`, `readiness-build.log` and `readiness-package-hashes.json`. RuntimeCompile passed with zero warnings/errors before the final build. Selected Qwen remains resident during performance measurement; D3-mini is stopped. This repairs the reproduced burst in this fixture, not every frame spike or world-scale performance. Individual native block reads still take tens of milliseconds. Concurrent Mini requests plus game performance remain a separate gate.

## Conceptual extraction v1: diagnostic only

The offline `conceptual-goal-v1` experiment separates candidate-neutral Qwen extraction from candidate binding. Extraction has seven conceptual kinds, ordered steps, verbatim source quotes, unbound target text, quantity/duration and condition/missing-information fields. Grounding cannot change extracted kinds, counts, order or numeric arguments. Unsupported or unresolved steps remain representable rather than being forced into a legal candidate. Deterministic validation cannot establish semantic completeness or target identity.

The corpus was frozen before inference: **28 roots**, SHA-256 `f9297c3aca22316665116163e82c29b55f3da2104b0e03c62f6701d82a937765`. All candidates are synthetic. There are **56 actual Qwen calls**: 28 extraction calls and 28 eligible grounding calls. The 56 evaluated arms reuse each root's extraction across original/reversed candidate orders; these are not independent extraction trials. Every call returns HTTP 200 and the parent verifies request/response hashes. No D3 route, live NPC consent, Core execution or native effects are included.

- Extraction structure accepted: **25/28**; strict typed-field/presence agreement: **10/28**. This metric does not prove full semantic agreement.
- Grounding outcome agreement: **34/56**; 20 plans, **six false plan arms**, two missed plan arms and six invalid arms.
- Plan-gold outcomes: 14/16; clarify-gold: 20/28; no-action-gold: 0/12.
- False plans in both orders: dropped four-gold constraint, unavailable cousin replaced with player, and player's own shopping intention interpreted as an NPC request.
- Warm serial extraction median/max: 1255.94/3056.40 ms; grounding: 265.30/448.01 ms. These are not gameplay frame measurements.

Evidence: `work/conceptual-goals-20261020/run1/`, independent `parent-verdict.json`, `model-runtime-hashes.json` and `source-snapshot-v1/` in the parent directory. **114 Python tests pass** on the final source. Source snapshots and failures are retained. The corpus is now development evidence and must not be represented as fresh held-out evidence in another tuned run. Results cannot be directly ranked against the different earlier 35-root Wait corpus.

**Decision:** do not promote conceptual v1 to gameplay. Next architecture experiment should constrain conceptual action selection separately from parameter extraction, preserve all conditions and targets, and measure semantic errors independently. D3 multi-question API support is unverified; test that capability before designing a protocol around it. No production parser changes are justified by an assumed API feature.

## Resume and shutdown checkpoint

The user's latest instruction requests shutdown after the bounded current work. No new package is started. All dispatched workers are terminal, the owned game has exited, and source/evidence remain in the integrated checkout without reset, broad staging or commit. The broad mod roadmap remains incomplete and is paused, not complete. The last live account reading is 57 percent used / 43 percent remaining. Stopping now follows the later shutdown instruction; the 60-percent-used quota threshold was not reached.
