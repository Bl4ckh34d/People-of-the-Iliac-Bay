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

Death/recovery amendment: extinct households stay vacant; corpse cleanup waits for elapsed time and an unobserved interior. New families use new identities. Dense-city recovery or bank listing takes 3–7 days; other settlements 7–30 days, with emptied isolated hamlets toward 30. Preserve ownership, deceased identities and memories. See `VACANCY-AND-RECOVERY.md`.

Indoor-crime refinement: a silent one-blow kill of the lone occupant raises no alarm until player departure. Guards travel to the house, clear the body upon arrival, and occupy it for six game hours; player return during occupation triggers attempted custody without magically identifying the unwitnessed killer. Other present occupants, including stationary NPCs, can mobilize to fight or flee and alert guards. Use actual finite responders and preserve quest identities. See `CRIME-SCENES.md`.

Public-crime refinement: direct witnesses flee to reachable safe interiors, alert nearby guards and warn at most two encountered NPCs. Recipients flee or fight but do not relay secondhand warnings. Incident IDs and persisted budgets prevent repeated or unbounded propagation. Player-caused deaths may include important quest NPCs and break their quests; preserve technical bindings and saves, with separate default protection from random background events. These amendments supersede the goal's original blanket quest-protection clause. See `CRIME-SCENES.md`.

Future extension: NPC inventories/pickpocketing, house keys and corpse-only equipped clothing/armor loot; reusable outfit diversity metadata. Full combat/casting/corpse sprite sets remain undecided. See `RELATIONSHIPS-AND-FUTURE-INVENTORIES.md`.

Future speech extension: optional English TTS, investigating the Telegram bot's MiraTTS engine; at least 52 female and 52 male base voices plus fantasy options, with persistent per-NPC voice/pitch/speed. The user delegates the 7B dialogue model choice; selection awaits benchmarks. See `FUTURE-DIALOGUE-AND-VOICE.md`.

## Current work

Population foundation implemented and reviewed. Full milestones remain incomplete.

The active goal's original text still mentions three residents; the user's later explicit five-person family limit above supersedes it. The goal tool permits status updates only, so this file and the amended design are the authoritative evolving scope. Current code still enforces three until the family/bed-capacity implementation and save migration land.

- 66/66 behavior tests pass with `dotnet run --project tests/LivingCities.Tests`, including fractional-second travel, bounded visits, schema-1 migration and persisted route validation.
- Stable residents and maximum-three households, deterministic bounded weighted routes, analytical work/travel/home/sleep schedules, persistent deaths, and transactional save validation are implemented in the Unity-independent core.
- Unity compiled the deployed sources. `LivingCitiesBuild.VerifyAndBuild` passed real DFU FullSerializer round-trip, adapter restore-failure isolation, API checks, and bundle reopen checks; exit code 0.
- The standard mod is installed in the isolated portable player. `tools/SmokeTest.ps1` verified its runtime source compilation and initialization alongside Basic Roads, then GameManager readiness. The owned process was stopped after the check.
- Read-only Luna/high reviews covered the foundation, street actors and routine/interior lifecycle. A House1 destination bug was found, reproduced by a failing Unity check, and fixed by validating actual opening windows. No further confirmed critical or important blocker was found in the reviewed changes; review does not replace runtime acceptance.
- Temporary Harmony probe intercepted both guard spawn entry points, including immediate/deferred batch calls and single-call return value, then removed its patches. Probe package removed after testing. This proves feasibility, not finite-guard gameplay.
- Seven native settings controls include the physical-population preview toggle. Unity verified the actual DFU settings deserializer; the player logged settings loaded. Existing residents/deaths survive capacity changes. Visual menu and live in-game adjustment checks remain pending.
- Real-town geometry adapter maps 156 building entrance anchors in Gothway Garden, conservatively rejecting 17 door candidates. Eleven sampled entrance-to-entrance A* routes pass in the actual player and after fixture reload. Door access checks include static collision sweeps, initial overlap rejection and a ground-floor height limit. Whole-town route connectivity, navigation around all props and performance profiling remain pending; this is not a guarantee that every route is traversable by an NPC.

Version 0.2.0 now connects physical adult actors to real doors in Gothway Garden only. The fixture has 324 resident schedules; 285 received visit routes in the initial survey and the rest remain home. Nearby exterior actors use 140/165-metre activation/removal radii and an 80-actor cap. Household surnames and individual appearances are deterministic. The runtime test observed 13 nearby actors and four distinct walking animation frames. Changing animation state only when needed fixes the reported gliding bug.

Following a sampled resident into building 13 finds the same name, appearance and ID. Exit/re-entry produces one active actor, a living-resident interior save reload preserves presence, and the actual entity death callback plus another save/reload keeps that resident dead. Schema 2 persists routes to avoid rebuilding against inactive exterior geometry; schema 1 migrates without regenerating residents. Legacy indoor saves without routes use home presence until their first exterior survey. Indoor positions currently use checked reachable standing anchors, not beds or activities. The preview leaves vanilla stationary quest/service actors untouched; their proper household/workplace mapping is still pending.

Survey: 175 buildings, 162 candidate homes, 215 doors, and 33,143 walkable cells in a 256 by 192 grid. Candidates still need filtering for ownership, quest occupancy and residence suitability. Family allocation, bed placement, complete offscreen/adjacent-chunk continuity, origin shifts, melee/death/crime integration, guard gameplay and LLM dialogue remain open. The current route cache is about 9.6 MB for this test town before compression; optimize it and profile large-city loading before broader release. The user prioritized functional milestones over curved turns and appearance polish.

Evidence: `work/unity-build-result.json`, `work/town-test.log`, historical `work/player-smoke.log`, sibling `../work/livingcities-build.log`. `tools/Build.ps1` checks build completion before reporting success. See `TEST-FIXTURES.md`, `INTEGRATION.md` and the separate `probes/guard` experiment.

