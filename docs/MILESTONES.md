# Living Cities milestones

Approved on 13 October 2026. The active Codex goal covers all milestones; this file records concrete acceptance evidence.

- [ ] M1 Baseline and integration probes. Official assets and patched player verified; Unity compilation, Basic Roads and Living Cities builds/startup passed. Temporary standard-mod guard interception passed in the shipped Mono player. Remaining: quest identity probe and in-world save/load test harness.
- [ ] M2 One-town persistent population. Stable households capped at three, deterministic routes and work/travel/sleep, nearby physical actors, no duplicates, save/load and leave/return continuity.
- [ ] M3 Civilian awareness and activities. Sleep poses/anchors, closed shops, burglary and wake-up detection, meetings, visiting, and tavern groups; quest bindings preserved.
- [ ] M4 Finite guards. Settlement-specific rosters, no garrison in tiny hamlets, small-town contingents, patrol pairs with torches, reachable wall posts, physical response, last-seen search/escape, arrest compatibility, defense, persistent deaths and bounded recruitment.
- [ ] M5 Night events and haunting. Bounded incursions, night crowd changes, reachable refuges, and actual main-quest state controlling haunting behavior.
- [ ] M6 Optional local dialogue. A benchmarked local 7B model, asynchronous loopback service, free-text UI plus topics, factual grounding, timeout/cancellation and vanilla fallback.
- [ ] M7 Broad validation and city-mod patches. Multiple settlement layouts, world-origin shifts, fast travel/rest/load, other-mod conflicts, versioned saves, performance budgets, packaging and gameplay checks. After vanilla stability, add explicit adapters for city overhauls, market stalls and currently decorative vendor NPCs.

Cross-cutting requirement: a mod settings menu with bounded controls, useful presets, subsystem toggles, clear save/reload semantics, and defaults. Expose working features as they are implemented. See `SETTINGS-AND-COMPATIBILITY.md`.

## Current work

Population foundation implemented and reviewed. Full milestones remain incomplete.

- 57/57 behavior tests pass with `dotnet run --project tests/LivingCities.Tests`.
- Stable residents and maximum-three households, deterministic bounded weighted routes, analytical work/travel/home/sleep schedules, persistent deaths, and transactional save validation are implemented in the Unity-independent core.
- Unity compiled the deployed sources. `LivingCitiesBuild.VerifyAndBuild` passed real DFU FullSerializer round-trip, adapter restore-failure isolation, API checks, and bundle reopen checks; exit code 0.
- The standard mod is installed in the isolated portable player. `tools/SmokeTest.ps1` verified its runtime source compilation and initialization alongside Basic Roads, then GameManager readiness. The owned process was stopped after the check.
- A read-only Luna/high review found no critical or important issues in the foundation. Its remaining test caveat is actual in-game save-slot and leave/return continuity.
- Temporary Harmony probe intercepted both guard spawn entry points, including immediate/deferred batch calls and single-call return value, then removed its patches. Probe package removed after testing. This proves feasibility, not finite-guard gameplay.
- Six native settings controls are bundled and wired to the live callback. Unity verified the actual DFU settings deserializer; the player logged settings loaded. Existing residents/deaths survive capacity changes. Visual menu and live in-game adjustment checks remain pending.

The live adapter currently shows virtual household/home/sleep diagnostics with F8. Core commutes are not yet connected to real doors or physical actors. No actual playthrough, bed placement, quest movement, finite guard gameplay, or LLM dialogue has passed testing yet.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, sibling `../work/livingcities-build.log`. See `INTEGRATION.md` for engine constraints and the separate `probes/guard` experiment.

