# NPC lifecycle and quest compatibility delivery plan

> **For agentic workers:** Use the `executing-plans` skill for a selected implementation slice. This is a planning backlog, not authorization to execute all phases. Delegation requires user authorization and the configured Luna/high ceiling.

**Goal:** Turn the NPC inventory and accumulated requests into independently reviewable deliveries without losing native quests, identity or physical continuity.

**Architecture:** Stable identity and native ownership underpin role-specific mobility. Housing reservations precede generated population; quest capabilities and information disclosure are explicit. Unloaded simulation remains bounded and deterministic where possible.

**Tech stack:** DFU 1.1.1, Unity 2019.4.40f1, C# 7.3 runtime adapters; .NET 8 core tests; existing PowerShell build and owned-player probes; optional local llama.cpp server.

**Spec:** [NPC lifecycle and quest-dialogue design](../specs/2026-10-17-npc-lifecycle-and-quest-dialogue-design.md). Read alongside the [TL;DR roadmap](../../ROADMAP.md) and [current inventory](../../NPC-INTEGRATION-AUDIT.md).

## Scope and execution gates

17 October 2026: all new role, companion and quest designs remain future work. The user subsequently authorized **only the 2× game-clock fix and rebuild** as immediate code work. That bounded exception does not authorize the other phases.

This document selects deliverables and acceptance criteria. Before implementing a phase, write its narrow technical plan against then-current code, including concrete tests and interfaces. Do not invent a universal actor API now or turn the whole backlog into one patch.

For each selected slice:

- [ ] Record current behavior, a failing regression or reproducible native case, and the exact save/mod combination.
- [ ] Implement only that slice, preserving original identifiers and native quest/service ownership.
- [ ] Run relevant core checks and runtime compilation, then an owned-player probe where physical/native behavior is involved.
- [ ] Compare save/load continuity and frame-time cost where relevant; state what was not tested.
- [ ] Update evidence/status documents and make a reviewable commit. Separate planning acceptance from gameplay acceptance.

## 0. Stabilization before expanding actor coverage — B01–B04

**Existing touchpoints:** `mod/Scripts/StreetPopulation.DailyItinerary.cs`, `mod/Scripts/InteriorRoomFollower.cs`, `mod/Scripts/LivingCitiesMod.DialogueLife.cs`, `mod/Scripts/RuntimeTiming.cs`, `src/LivingCities.Core/AdultDailyItinerary.cs`, `mod/Scripts/DailyItineraryProbe.cs`, `tests/LivingCities.Tests`.

- [ ] Deliver the separately authorized 2× clock: normal elapsed game time advances two seconds per unpaused real second, including new/load game lifecycle. Preserve pause, rest/wait and fast-travel intentional time advances. Audit schedule conversion constants and saved timers; validate native elapsed time, not just a configuration value.
- [ ] Trace one resident across home → shop → inn → home with stable ID, actual location, current activity and next destination. Confirm dialogue uses actual progress rather than a nominal schedule that already moved ahead.
- [ ] Complete the existing door/arrival regression: navigate physically to a valid entrance, transition once, leave via the real exit, resume outside without teleport or duplicated actor after reload.
- [ ] Reproduce and correct blocked inn room routes and unintended shelf/staff-area destinations. Verify standing pose while idle, stable facing and no door oscillation.
- [ ] Measure guard updates and inn surveys separately; bound per-frame work and stagger independent work without changing deadlines or freezing identities. Save before/after frame-time samples.

**Acceptance:** same tracked person, right named destination, real doorway arrival, accurate dialogue, no observed freeze/disappearance; explicit timing evidence. Passing a clock probe alone does not accept all movement work.

## 1. Read-only actor census — I01

**Existing touchpoints:** `mod/Scripts/HouseholdOccupants.cs`, `mod/Scripts/TavernRoomSurvey.cs`, `mod/Scripts/QuestOfferBindings.cs`, `mod/Scripts/KnownQuestBindings.cs`, `src/LivingCities.Core/QuestBindings.cs`; evidence in `docs/NPC-INTEGRATION-AUDIT.md`.

- [ ] Survey vanilla and installed-mod exterior/interior actors incrementally after providers finish creating them.
- [ ] Produce a diagnostic inventory containing stable source/identity, appearance provider, role, building, native quest binding, service role, current bed/home claim and adoption/exclusion reason.
- [ ] Compare Daggerfall market/castle/guild/temple/shop/inn examples with a village and a walled/non-walled location. Include Quest Pack actors when active.
- [ ] Re-enter/reload/re-survey and prove inventory entries remain stable with no duplicate identities or gameplay mutation.

**Acceptance:** inspectable actor census and explicit unknown cases. No movement, replacement, mortality or housing assignment in this slice. Its report determines adapter work instead of assuming all flats are interchangeable.

## 2. Reservation and identity foundations — I02–I03, H01, H04

**Existing touchpoints:** `src/LivingCities.Core/HouseholdGeneration.cs`, `src/LivingCities.Core/HouseholdRecoveryReservation.cs`, `mod/Scripts/HouseholdOccupants.cs`, `mod/Scripts/TavernRoomSurvey.cs` and existing traveler/save models located through the census.

- [ ] Define a versioned claim ledger keyed by persistent person and actual dwelling/bed identity. Preserve saved residents and traveler hometown claims before assigning new capacity.
- [ ] Reserve authored upstairs tenants and their real bedrooms; reserve local stall workers and off-site shopkeepers before generating remaining households.
- [ ] Handle already-full saved locations explicitly: retain people and their homes, record unmet capacity, and use a reviewed relocation/expansion policy. Never silently evict or regenerate saved families.
- [ ] Test migration, provider load order, duplicate appearances with different identities, absent/dead owners, changed interior layouts and repeat surveys.

**Acceptance:** one identity across representations; one valid claim per slot; every unassigned person has a recorded reason; generation respects existing claims. No forced home invented where capacity is absent.

## 3. Separate mobility deliveries by role — H02–H08

**Existing touchpoints:** `src/LivingCities.Core/AdultDailyItinerary.cs`, `mod/Scripts/StreetPopulation.DailyItinerary.cs`, `mod/Scripts/HouseholdDoors.cs`, `mod/Scripts/HouseholdPropertyKeyItem.cs`, `mod/Scripts/NativeTavernRentals.cs`, `src/LivingCities.Core/TravelerRoomWalk.cs`; native service hooks in `src/LivingCities.QuestHooks`.

Deliver each row separately, with save/load and player-observed route tests:

| Slice | Deliverable | Required native acceptance |
| --- | --- | --- |
| Local stall workers | Assigned home, slightly varied work commute and native stall service | Home → stall → home; no service loss, clone, or independent duplicate at home |
| Market demand | Residents visit real active stalls and still visit indoor shops; bounded late weekend opening | Multiple stalls attract natural spread; finite closing despite lingering shoppers; correct game-day calendar |
| Shop tenants | Upstairs residents retain beds, leave/return independently and carry scoped keys | Return after closing; key permits correct door; looted key persists; no access to unrelated shop |
| Shopkeepers | Free shop bed or assigned off-site home; closing/locking and opening/unlocking | Return before service hours, usual shop post, tenant claims untouched; native services/quest hooks still work |
| Inn visitors | Authored nonquest bedroom occupants gain stable outside origin/home, reason, itinerary and return journey | Day outing, evening room return, checkout and physical departure; no simultaneous home/inn clone |

**Acceptance:** observed physical travel and actual schedule state agree. Do not convert quest actors through generic role inference. Recheck quest ownership before a journey starts.

## 4. Bed capacity, staff and appearance — H09–H10, A01–A02

**Existing touchpoints:** `mod/Scripts/TavernRoomSurvey.cs`, `mod/Scripts/HouseholdOccupants.cs`, `docs/DIRECTIONAL-CIVILIAN-ANIMATION.md`, `mod/Textures`, `art`.

- [ ] Keep one-per-bed as the general rule. Add explicit two-slot capacity only for suitable beds serving two authored ordinary inn-room occupants; retain both identities and claims.
- [ ] Inspect source/replacement sprites first. Prototype a shared blanket with two appearance-specific heads and validate bed alignment, one occupant waking/leaving/dying and correct selection/collision.
- [ ] Inventory actual idle/walk/run/sleep coverage by provider before generating missing art. Never substitute an unrelated face or imply all variants already exist.
- [ ] Assign ordinary institutional staff reachable beds where available; return to native work posts. No new off-site staff routine is required. Quest-owned staff use the later capability gate even within one building.

**Acceptance:** no doubled global housing capacity, no stacked single-person sleepers, no loss of services, no invisible/incorrect animation. More than two occupants or unsuitable beds remain explicit capacity exceptions requiring another decision.

## 5. Story-aware context and persistent memory — Q02, Q04–Q05, M01–M03

**Existing touchpoints:** `mod/Scripts/LivingCitiesMod.Dialogue.cs`, `mod/Scripts/LivingCitiesMod.DialogueLife.cs`, `src/LivingCities.Core/LocalDialogue.cs`, `src/LivingCities.Core/DialogueHistoryWindow.cs`, `mod/Scripts/LocalDialogueClient.cs`, native conversation hooks.

- [ ] Separate authored story persona from randomized ordinary personality. Mood changes delivery without changing hidden intent, allegiance or reveal permissions.
- [ ] Create a speaker/stage disclosure projection using native quest events and permitted leads. Never submit complete raw quest scripts, hidden identity facts or future outcomes to the social model.
- [ ] Test questions before/after a reveal, direct guesses, indirect requests, another speaker's knowledge, save rollback and remembered false claims. Sensitive quest answers must come from native/approved stage content or a validated rewrite because models may already know the plot.
- [ ] Add per-person dated memory with participants, locations, actual actions/results, certainty and source. Retrieve relevant memories after disclosure filtering; retain recent exact conversation and current authoritative context.
- [ ] Verify promised versus completed trips, past-season recollections, social history and perceived same-room events. Avoid global omniscience and broad canned refusals that destroy ordinary roleplay.

**Acceptance:** new legitimate leads remain available when native gates allow them; unrevealed facts stay protected; ordinary chat remains free-form and in character. Memory must not resurrect withheld facts through summaries, caches or other speakers.

## 6. Native quest capabilities; Quest Pack implementation deferred — Q01, Q03, Q06–Q07

**Existing touchpoints:** `src/LivingCities.Core/QuestBindings.cs`, `mod/Scripts/QuestOfferBindings.cs`, `mod/Scripts/KnownQuestBindings.cs`, `src/LivingCities.QuestHooks/NativeQuestOfferHooks.cs`, `tools/QuestSurvey.ps1`.

- [ ] Build one review record per quest-role-stage: source version/hash, original person/foe identity, placement/hide/kill/service/timer actions, permitted capabilities, bed/return anchor, public cover story, allowed new leads, withheld facts, reveal gates and acceptance saves.
- [ ] Start with an ordinary vanilla quest giver and recipient. Prove native accept → progress → reward/failure behavior remains authoritative before enabling optional same-building sleep.
- [ ] Cover dynamic hostility/transformation, aliases/captives, repeated timed placement and multi-actor scenes. Same-building movement alone is not sufficient evidence.
- [ ] Expand vanilla coverage incrementally. Unsupported actors keep native placement/actions; restrict sensitive plot discussion without disabling all harmless social chat.
- [ ] Keep Quest Pack 1 mobility in the backlog. Use `JHDB121a`, `JHDB109`, `JHCG003` and `JHKN119` as initial representative review records, then classify the remaining scripts individually. Source sampling is not certification of all 302 quests.

**Acceptance:** quest success, failure, rewards, staged hostility and reload match native semantics. Each adapter lists precisely enabled capabilities and tested versions. User-requested mortality policy stays distinct from accidental quest breakage.

## 7. Player installation and modular packaging — P01–P04

**Existing touchpoints:** `README.md`, `docs/INSTALLATION.md`, `tools/StartLocalDialogue.ps1`, `mod/Scripts/LivingCitiesMod.Dialogue.cs`, mod settings and existing feature switches.

- [ ] Publish a separately verified playable bundle with exact tested DFU/dependency versions and file hashes; source checkout alone is not an installer.
- [ ] Make no-LLM installation complete without model/runtime/CUDA downloads or background launch. Verify settings and save compatibility across module toggles.
- [ ] Add an optional prebuilt llama.cpp bootstrap with explicit download consent, pinned official assets/checksums, partial-download recovery, health checks and useful errors. Prefer PowerShell/native packaging over an unnecessary Python dependency.
- [ ] Measure combined game/model VRAM and RAM on supported configurations. Publish a tested minimum only after measurement; CPU/AMD/other platforms remain untested until verified.

**Acceptance:** clean-machine installation, optional dialogue works, no-LLM path works, update/removal does not damage saves or unrelated files. Broader guard/recovery/commerce modules each require their own toggle lifecycle tests.

## Shelved extension: invitations and companions — F01–F06

After foundations, split into: social invitation/temporary schedule override; durable human stats/effects; inventory/equipment/gold; nonquest recruitment and payment; used-skill progression; validated world-action adapters. Each gets a separate technical plan and acceptance boundary.

Initial action examples remain thief lockpicking and fighter door bashing, followed by combat/buff support, stealing, climbing, ranged cover and item carrying/exchange. Commands must resolve a real target, check consent/resources/quest restrictions, execute physically, and record actual results. Quest NPCs remain excluded. Shared activity and travel memories are prerequisites for convincing long-term companions.

No companion, sprite-generation or exhaustive quest implementation is authorized by this planning document.
