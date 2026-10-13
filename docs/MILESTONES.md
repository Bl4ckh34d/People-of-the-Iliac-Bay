# Living Cities milestones

Approved on 13 October 2026. The active Codex goal covers all milestones; this file records concrete acceptance evidence.

- [ ] M1 Baseline and integration probes. Official assets and patched player verified; Unity compilation, Basic Roads and Living Cities builds/startup passed. Temporary standard-mod guard interception and actual inside/outside Gothway Garden saves/reloads passed in the shipped player. Remaining: quest identity probe.
- [ ] M2 One-town persistent population. Stable households with shared surnames; mostly smaller households, up to five for two adults and three children (14 October amendment supersedes maximum three). Beds and house size are soft preferences; couples and children can share beds. Explicit family roles, child-local play/home schedules, shopkeepers commuting home when their shop has no living quarters, deterministic adult work/travel/sleep, nearby physical actors, no duplicates, save/load and leave/return continuity.
- [ ] M3 Civilian awareness, relationships and activities. Sleep poses/anchors, closed shops, burglary/waking, friendships/rivalries, meetings, visiting and tavern groups; persistent bereavement with fading salience and witnessed-killing hostility; quest bindings preserved.
- [ ] M4 Finite guards. Settlement-specific rosters, no garrison in tiny hamlets, small-town contingents, patrol pairs with torches, reachable wall posts, physical response, last-seen search/escape, arrest compatibility, defense, persistent deaths and bounded recruitment.
- [ ] M5 Night events and haunting. Bounded incursions, night crowd changes, reachable refuges, and actual main-quest state controlling haunting behavior.
- [ ] M6 Optional local dialogue. A benchmarked local 7B model, asynchronous loopback service, free-text UI plus topics, factual grounding including relationships and bereavement, timeout/cancellation and vanilla fallback.
- [ ] M7 Broad validation and city-mod patches. Multiple settlement layouts, world-origin shifts, fast travel/rest/load, other-mod conflicts, versioned saves, performance budgets, packaging and gameplay checks. After vanilla stability, add explicit adapters for city overhauls, market stalls and currently decorative vendor NPCs.

Cross-cutting requirement: a mod settings menu with bounded controls, useful presets, subsystem toggles, clear save/reload semantics, and defaults. Expose working features as they are implemented. See `SETTINGS-AND-COMPATIBILITY.md`.

Tavern amendment: nonlocal travelers rent for one or several nights, visit town, then depart. Paid reservations retain rooms while guests are out; first come, first served, no guaranteed player vacancy. Reserved rooms reject player sleep; full taverns report the earliest room release. Preserve existing paid player bookings. See `TAVERN-LODGING.md` for persistence and acceptance requirements.

Future extension: NPC inventories/pickpocketing, house keys and corpse-only equipped clothing/armor loot; reusable outfit diversity metadata. Full combat/casting/corpse sprite sets remain undecided. See `RELATIONSHIPS-AND-FUTURE-INVENTORIES.md`.

Future speech extension: optional English TTS, investigating the Telegram bot's MiraTTS engine; at least 52 female and 52 male base voices plus fantasy options, with persistent per-NPC voice/pitch/speed. The user delegates the 7B dialogue model choice; selection awaits benchmarks. See `FUTURE-DIALOGUE-AND-VOICE.md`.

## Current work

Population foundation implemented and reviewed. Full milestones remain incomplete.

The active goal's original text still mentions three residents; the user's later explicit five-person family limit above supersedes it. The goal tool permits status updates only, so this file and the amended design are the authoritative evolving scope. Current code still enforces three until the family/bed-capacity implementation and save migration land.

- 60/60 behavior tests pass with `dotnet run --project tests/LivingCities.Tests`, including smooth fractional-second travel and exact arrival/midnight boundaries.
- Stable residents and maximum-three households, deterministic bounded weighted routes, analytical work/travel/home/sleep schedules, persistent deaths, and transactional save validation are implemented in the Unity-independent core.
- Unity compiled the deployed sources. `LivingCitiesBuild.VerifyAndBuild` passed real DFU FullSerializer round-trip, adapter restore-failure isolation, API checks, and bundle reopen checks; exit code 0.
- The standard mod is installed in the isolated portable player. `tools/SmokeTest.ps1` verified its runtime source compilation and initialization alongside Basic Roads, then GameManager readiness. The owned process was stopped after the check.
- A read-only Luna/high review found no critical or important issues in the foundation. Its remaining test caveat is actual in-game save-slot and leave/return continuity.
- Temporary Harmony probe intercepted both guard spawn entry points, including immediate/deferred batch calls and single-call return value, then removed its patches. Probe package removed after testing. This proves feasibility, not finite-guard gameplay.
- Six native settings controls are bundled and wired to the live callback. Unity verified the actual DFU settings deserializer; the player logged settings loaded. Existing residents/deaths survive capacity changes. Visual menu and live in-game adjustment checks remain pending.
- Real-town geometry adapter maps 156 building entrance anchors in Gothway Garden, conservatively rejecting 17 door candidates. Eleven sampled entrance-to-entrance A* routes pass in the actual player and after fixture reload. Door access checks include static collision sweeps, initial overlap rejection and a ground-floor height limit. Whole-town route connectivity, navigation around all props, physical actors and performance profiling remain pending; this is not a guarantee that every route is traversable by an NPC.

The test harness now creates actual inside/outside Gothway Garden saves and verifies repeated loads and unchanged serialized population in the real player. A separate process reload also passed. Survey: 175 buildings, 162 candidate homes, 215 doors, and 33,143 walkable cells in a 256 by 192 grid. These candidate homes are not yet filtered for beds, quest occupancy or sale/player ownership. The live adapter still shows virtual household/home/sleep diagnostics with F8. Core commutes are not yet connected to real doors or physical actors. Bed placement, quest movement, finite guard gameplay, and LLM dialogue remain untested.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, sibling `../work/livingcities-build.log`. See `INTEGRATION.md` for engine constraints and the separate `probes/guard` experiment.

