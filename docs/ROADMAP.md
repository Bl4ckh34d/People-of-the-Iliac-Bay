# Roadmap — TL;DR

Consolidated 17 October 2026. The broader roadmap remains planned work. Subsequently authorized work: **2× game clock**, **truthful home context and trusted home map markers**, and **persistent dated NPC interaction memory**. These focused changes do not authorize the deferred companion, recruitment, quest-mobility or housing expansion. Acceptance status belongs in Testing.

## Direction

Make one persistent person exist across home, work, streets, travel, conversation and combat. Preserve native quest/service ownership. Complete reliable physical movement and identity before adding more behaviors that depend on them.

The [current inventory](NPC-SYSTEMS.md#inventory) describes implemented coverage. The [future design and backlog](ROADMAP.md#detailed-design) records the latest requirements. The [delivery plan](ROADMAP.md#delivery-plan) gives ordered, testable work packages. Old milestone paragraphs remain historical evidence, not a competing current plan.

## Important answers

- Most side quests retain native quest control. They are **not automatically broken** because only one delivery quest has household binding. General quest integration remains unverified, and unrelated movement/combat/guard changes still need compatibility tests.
- Main-story people stay under native placement and quest control for now. Their authored personality, public story and disclosure permissions must override random personality generation. Mood can vary within those boundaries.
- Same-building bed movement can preserve identity, but staying in one building alone does not guarantee quest safety. Original native binding, activation, visibility, timing, service access and return position must also survive.
- Two authored NPCs in a one-bed inn room do not share that bed today. Excess occupants are logged as lacking a bed. The requested shared-bed exception is future work.
- Outdoor stall owners do not currently receive homes before generated families. This becomes a required allocation order.
- GitHub has source/build notes and a local-model launcher, not a complete end-user installer or a published release bundle. See [installation status and manual setup](INSTALLATION.md).

## Proposed sequence

| Order | Work package | Exit condition |
| --- | --- | --- |
| 0 | Stabilize observed movement, itinerary/dialogue agreement, door transitions and frame stalls | A tracked person reaches the stated destination, enters/exits physically, and remains the same person after reload; repeatable frame-time evidence |
| 1 | Incremental actor census and unified ownership | Every surveyed actor has stable provenance, identity, role, appearance, owner and a visible adoption/exclusion reason; no duplicate representation |
| 2 | Housing and bed allocation before generated population | Saved people and authored tenants keep capacity; local workers obtain homes before remaining families are generated; migration never evicts silently |
| 3 | Native locals and visitors become mobile by role | Stall workers commute; shopkeepers lock/unlock and commute when needed; shop tenants use keys; inn lodgers have origins, visits and checkout/return journeys |
| 4 | Bedside staff behavior and appearance coverage | Ordinary institutional staff retain services and use real reserved beds; movement/sleep art matches each actual vanilla or replacement appearance |
| 5 | Story-aware dialogue and persistent memory | Main NPCs keep authored character; stage-filtered facts do not leak secrets; encounters and shared activities persist with provenance and game dates |
| 6 | Broader vanilla quest adapters, then Quest Pack 1 | Per-quest/per-role capability records and native end-to-end tests; unsupported stages remain native rather than being moved speculatively |
| 7 | Modular packaging and public installation | No-LLM installation works without model/CUDA downloads; optional setup is reproducible on a clean machine with measured hardware requirements |
| Later | Invitations, durable human combat state, companions and action framework | Independent milestones after movement, ownership, memory and inventory foundations; explicitly shelved now |

Some work can be scoped independently: installation documentation and spoiler-protection design do not require full mobility. The order is a dependency guide, not permission to start a large combined implementation. Quest Pack 1 mobility implementation remains deferred despite its installation and source audit.

## Latest behavioral decisions

- Reserve housing for authored local stall owners, staff needing off-site homes and shop tenants before filling remaining homes. Keep traveler hometown reservations intact.
- Stall workers have slightly varied opening/closing commutes. Markets may stay open longer on weekends while demand remains, within finite closing and safety limits. Local residents can browse multiple stalls as well as indoor shops.
- Ordinary authored inn-bedroom occupants become visiting travelers with stable origins and a reason for visiting. They leave for daytime activities, return to lodging, and eventually check out and travel home. Quest actors and staff are classified separately.
- Allow two authored inn-room occupants to share one bed through an explicit capacity/claim exception and a proposed shared blanket with appearance-specific heads. Do not double all household capacity or infer a romance from co-occupancy.
- Ordinary guild/temple/castle staff keep their workplace duties; off-site daily routines are not required now. Reserve reachable beds if present and preserve each native service role.
- Upstairs shop-bedroom occupants are residential tenants. Their beds are unavailable to the shopkeeper. Tenants carry valid access keys and may return after closing; the same persistent key can be looted after death.
- Main and side quest actors keep original identity and script control. Only reviewed capabilities may be enabled per role/stage. Recruitment excludes main-quest and side-quest actors.

## Existing obligations retained

These additions do not replace earlier work: finite guards, crime/fleeing/reporting and pursuit, guard quarters, household recovery/house sales, corpse loot/property keys, human stats, skill-sensitive awareness, adult itineraries and sparse inter-settlement travel, spacing/facing/idle/running art, commerce and service disposition, rank-sensitive guild expulsion, noble insult consequences, optional quest tone, modular settings, context/memory/listener awareness, performance and versioned saves. See the design's backlog crosswalk and [milestones](HISTORY.md#source-milestones-md).

Current blockers and uncertain evidence remain visible: physical inn movement is not broadly accepted; the prior current/next destination and saved-door fixes still need targeted native checks; game time remains 12× and a safe 2× migration is pending. No percentage-complete estimate should hide these distinctions.

## Next decision

After the separately requested clock fix, recommended next selectable slice: finish the existing destination/door/movement regression, then implement the read-only actor census. Alternative: prioritize story-safe context if plot leakage is the immediate concern. Choose one bounded slice before code changes; companions and exhaustive Quest Pack adaptation are not that slice.

<a id="detailed-design"></a>

# NPC lifecycle, story-aware dialogue and future companions

Date: 17 October 2026. Status: **user requirements captured; architecture proposed for review; implementation deferred**.

This consolidates the user's latest decisions with [current NPC evidence](NPC-SYSTEMS.md#inventory), [additional quest evidence](COMPATIBILITY.md#quests), [relationships](HISTORY.md#source-relationships-and-future-inventories-md), and [dialogue/memory](HISTORY.md#source-future-dialogue-and-voice-md). The short navigation entry is [Roadmap](ROADMAP.md). Nothing below establishes that the feature already works.

## 1. Alternatives and recommendation

1. **Replace every NPC with a generic mobile citizen.** Fast apparent coverage, but loses quest/service identity, authored appearance and native visibility ownership. Reject.
2. **Keep every authored NPC static indefinitely.** Safest immediate quest behavior, but cannot deliver worker commutes, active lodgers or consistent housing. Use only as the fallback for unsupported roles/stages.
3. **One persistent identity with role-specific capabilities and native ownership adapters.** More foundation work, but allows ordinary citizens to move while quest/service controls remain authoritative. Recommended. Enable capabilities incrementally after tests; do not require every quest to be rewritten first.

The invariant is one person, one persistent life state, one physical representation when loaded. The same name or sprite is not sufficient proof of identity. Scene absence, unloaded simulation, quest hiding, sleep and death are distinct states.

## 2. Identity, roles and allocation

The census records effective mod/world-data provider, map/block/building/record identity, native hash/nameSeed, faction/service role, quest UID/symbol/individual identity, appearance provider/archive/record, workplace, known home, room/bed, activity and ownership. Include inactive authored objects and replacements; distinguish decorations from people. Never use Unity instance IDs for persistence.

Roles can change. A citizen can become quest-owned after accepting a quest; a quest can finish while the citizen is sleeping. Ownership is re-evaluated at lifecycle boundaries. Existing saved names, deaths, relationships, keys, possessions and traveler reservations survive adoption.

Allocation order for a new or completing settlement:

1. Preserve existing saved identities, occupied homes, player property, active rentals and quest placement constraints.
2. Survey authored bedroom tenants/lodgers and real beds; reserve explicit room occupancy before keeper allocation.
3. Reserve on-site staff/keeper beds where actually free and suitable.
4. Assign local workers who need off-site homes to remaining residential capacity, before generated families fill it.
5. Preserve reserved traveler hometown households and merge them by stable identity rather than generating another copy.
6. Generate remaining households only in remaining capacity.

Existing towns require a versioned migration, not a destructive re-roll. Lack of space is an explicit unresolved housing assignment; it must not evict a saved family or invent a bed. Proposed fallback is to retain native placement until a legitimate vacancy or reviewed migration policy provides housing. Never label a local worker a foreign visitor merely to avoid the capacity problem.

### Market workers and destinations — requirements H01–H03

Stall owners are locals with a home in the stall's settlement. Separate home/work keys and retain the authored service. They walk to work before opening and home after closing, with stable small variations by person/day. Use loaded geometry to locate market stalls and browsing/transaction positions; detection is capability-based for active city/village improvement mods, not a hardcoded town list.

Residents' multi-stop schedules may select multiple outdoor stalls, browse, buy and socialize around markets while retaining indoor shop visits. Crowd generation must use existing people, not duplicate visitors. Demand and navigable capacity influence destination choice.

Weekend market extensions depend on the in-game calendar, customers still present, weather/danger and worker availability. Use a bounded latest closing time and a sustained-demand window to prevent a single late NPC from keeping the market open forever. Exact calendar-day mapping and balance values remain explicit design choices for that implementation slice; do not silently import a real-world Saturday/Sunday convention. Dialogue must report actual opening/closing state.

### Shopkeepers and upstairs tenants — H04–H06

Classify authored upstairs bedroom occupants as tenants unless quest/staff evidence overrides that classification. Reserve their actual beds first. Tenants receive household membership, full identity, daily activities and a key granting access through the shop after closing. They are not automatically the keeper's relatives or employees.

A keeper with a legitimate unoccupied on-site bed can live at the shop. Otherwise assign an off-site household. Closing behavior walks to the exit, locks the appropriate public entrance, returns home, sleeps, returns before opening, unlocks and resumes the native service position. No teleporting when the schedule deadline passes.

Tenant access must not reopen public commerce or unlock all internal rooms. Key permissions distinguish public business entrance, tenant residence and private areas where applicable. A killed tenant's genuine key becomes corpse loot once, retains building scope across saves, and permits the player's nighttime entry without granting ownership of merchandise. Native crime/quest door overrides retain priority. No permanent dead-person duplicate may deliver another key.

### Authored inn occupants — H07–H09

Ordinary non-quest bedroom occupants become travelers staying in that inn, not permanent immobile decorations. Assign a stable home outside the destination settlement and reserve that person in the origin population before it is fully generated. Prefer actual supported settlements; a proposed origin beyond the simulated map must be an explicit abstract origin, not a fabricated visitable house.

Give each traveler a reason for visiting: official business, services, pilgrimage, trade, or visiting established relatives. A matching surname may support a family visit but does not prove a relationship; create or validate an explicit relationship. Preserve authored/fixed names. Travel parties can share a booking without being spouses.

Plan local daytime visits, evening return, room use, morning checkout when the visit ends, and physical travel home. Multi-day stays are allowed. Staff/innkeepers and quest actors are not converted into transient tourists by this rule. Common-room NPCs require classification rather than blanket bedroom treatment.

**Shared-bed amendment:** when an authored inn room contains two ordinary occupants but one suitable bed, support one bed with two explicit occupant claims. This narrowly supersedes the earlier one-NPC-per-bed rule for this case. Normal generated households and other beds remain one-per-bed unless a later explicit policy changes them. More than two occupants are a capacity conflict, not permission for unlimited sharing.

Proposed art uses one shared blanket/pillow composition with two independently identified head layers, avoiding every possible paired sprite sheet. Preserve individual faces, bed-axis alignment, pixel proportions, wake/death state, hit selection and room booking. One occupant waking or dying must not remove or kill the other. Shared-bed occupancy does not create a relationship. This is an art/renderer proposal, not an accepted generated asset.

### Guild, temple and castle staff — H10

Ordinary staff keep their work inside the institution; new external daily routines are not required now. Native service/faction purpose supplies background. Add a stable personality and context-sensitive mood consistent with that role. If reachable beds exist, reserve them and implement bedside sleep/return without removing service identity. Do not assume every guild, temple or castle has beds. Quest-owned staff follow section 3 first.

## 3. Quest mobility and dialogue — Q01–Q07

### Preservation, not universal household binding

The current narrow household pilot does not mean all other side quests break. They mostly retain native quest actors and script behavior. Broad mod compatibility is unverified; deliberate player killing can still make quests impossible under the user's established policy.

Main-story NPCs remain in their native locations for now. A bed in the same building may be reserved without moving the actor. Actual bed travel/sleep requires preserving native identity fields, components, activation collider, quest visibility/mute/hostility, timers and services. An inaccessible bed or a quest-required active scene disables that capability. Return to the original authored anchor, not a newly inferred spot.

Maintain a capability record per role and, where necessary, quest stage: native placement only; local movement; sleep; cross-building movement; reactive fleeing/combat; service interaction; generated social chat; quest tone rewriting. Capability denial has a documented reason. Main/side labels alone do not determine safety. Quest Pack 1 implementation is deferred and uses the same framework later.

### Character and spoiler boundaries

Named story NPCs use authored character profiles: public role, voice, outward temperament, obligations, declared story and relationship posture. Do not randomly turn a calculating noble into an indiscreet cheerful stranger. Weather, injury and disposition can influence delivery within the authored profile; they cannot change motives, reveal gates or canonical facts. Ordinary citizens keep mixed generated traits.

Do **not** put entire quest scripts, future stages, hidden identities or true intentions into the generation prompt and rely on “do not spoil” instructions. Store full quest data outside the LLM context. Build a per-speaker, per-stage projection with:

- Public/current scene facts the speaker can observe.
- Facts the speaker knows and is permitted to tell this player at this stage, including newly available native leads when their conditions are satisfied.
- The speaker's authored public account or cover story; the model need not know the secret it conceals.
- Explicit native topic/action identifiers that are currently available, with outcomes still executed by native handlers.
- Relevant relationship/memory entries that pass the same disclosure gates.

Player knowledge and NPC knowledge are separate. A player's guess is not confirmation; overheard rumors retain source/uncertainty. Another NPC's private memory never enters this speaker's context automatically. High disposition or a compliment must not unlock a plot reveal unless native quest state authorizes it.

A model may know Daggerfall lore from training. Context filtering alone therefore cannot guarantee secrecy. For sensitive quest questions, use approved native-stage facts or authored responses and validate any allowed tone rewrite. Unsupported quest questions remain under native dialogue; spoiler-neutral social chat may continue. Do not replace all roleplay with generic rejection templates. Generated text cannot start, finish, reward, fail or relocate a quest on its own.

Retrieval applies authorization **before** ranking/context assembly. Summaries, persisted memories, caches and conversation history retain speaker/save/quest-stage provenance and cannot reintroduce blocked secrets. Invalidate stage-sensitive caches after native quest transitions. A spoiler typed by the player remains a player claim, not model authority. Native text already disclosed in this save can be recalled where the speaker legitimately knows it.

### Representative stage analysis

This is a planning sample, not an individual review of all 302 pack quests:

| Case | Native ownership requirements | Dialogue projection |
| --- | --- | --- |
| `JHDB121a`, named Gortwog/Br'itsa and alias/vampire sequence (326–331, 431–446) | Keep named/alias/foe resources distinct; script controls placement/hiding | Public persona before reveal; hidden identity and future encounter absent; expose only native-authorized information afterward |
| `JHDB109`, child/parent rescue and ambush (95–154) | Captive does not independently commute home; preserve parent and child hide/placement | Rescue-stage observations; no parent ambush details before disclosure; memory records actual shared rescue afterward |
| `S0000007`, Cyndassa and separate werewolf resources (334–355, 406–501) | Preserve Person/Foe, face, restraint and kill events | Authored character and current story; hidden monster information gated by actual stage, not friendliness |
| `JHCG003`, chemist giver/placement/hide/reward (84–106) | Original service/quest binding remains; routine cannot undo hide | Normal role-based social context; native offer/hand-in/reward handled separately |
| `JHKN119`, priest/ritual and timed foes (115–149, 181–213) | Script controls priest availability and event clocks; no independent bedtime catch-up | Current ritual instructions when available; future waves and concealed outcomes excluded |

The individual quest backlog record must include source version/hash, speaker identities, native stages/actions, public/known-private/disclosable facts, capability gates, reveal conditions and representative before/after/save-load tests. Generated analysis can propose records; manual source review and native tests establish acceptance. Keep unreviewed scripts on conservative native ownership. Candidate pack/extension issues already identified in the audit remain separate upstream compatibility tasks.

## 4. Memory, invitations and companions — M01–M03, F01–F06

These are explicitly future features, shelved while foundations stabilize.

**Memory foundation:** each persistent NPC remembers dated encounters, claims, promises, conflicts, activities and places genuinely shared with the player. Store save/NPC identity, game date/time/location, participants, source, certainty, event outcome and salience. Retrieve compact relevant memories plus recent exact turns; do not load every NPC's entire transcript. Keep current authoritative context present. Distinguish a promised trip from a completed one and a player claim from witnessed fact. Memory retention/summary work is bounded and asynchronous. Same-room listeners only remember what they actually perceive.

**Social invitations:** the player may invite an NPC to a specified inn/tavern for drinks and a few hours together. NPC chooses using familiarity, disposition, memory, personality/mood, current duties, danger, travel time and availability. Strangers usually decline; friendship creates opportunity, not guaranteed compliance. Acceptance reserves time, follows the player physically to that named venue, creates a joint activity, then releases the temporary schedule override and resumes a feasible plan. Cancellation, player abandonment, closing, full venue, combat, death and reload must not strand or duplicate the NPC. Record the actual experience in memory. Quest actors cannot be recruited through this route.

**Human combat state:** ordinary NPCs should not be guaranteed one-punch deaths. Assign persistent, modestly varied human attributes, skills, level, health, magicka, fatigue, resistances and abilities. Store the initial roll once; do not reroll on streaming. Some know spells, can protect/heal themselves or the player, and attack enemies. Use native combat/effect semantics where possible; sustain damage, diseases/status effects, cooldowns and resource use across transitions. Stats should fit role and experience rather than making every civilian a combat expert. Exact balance remains a later design task.

**Companion recruitment:** non-quest NPCs may join for money or earned trust/help. Preserve their identity, home, family, duties, possessions and ability to refuse/leave. Re-check quest eligibility at recruitment and subsequent quest ownership changes. Main and side quest NPCs are excluded, including fixed story identities and currently bound actors. Hiring cost/payment is authoritative; dialogue cannot duplicate money or bypass consent.

**Inventory and progression:** companions own persistent equipment, items, gold and keys; support carrying, trading and giving/receiving items with capacity and ownership checks. Used skills gain experience and improve; level progression follows a deliberate DFU-compatible skill model, not an unreviewed generic kill-XP system. Persist growth without streaming duplicates or time-skip training exploits. Visible equipment and sprite compatibility remain constraints from the earlier inventory plan.

**Action framework:** interpret chat as a proposed task, resolve real targets, check capability/consent/resources/law/quest ownership, then execute through bounded native adapters. Initial examples: a thief attempts a real lock using lockpicking skill; a fighter attempts native door bashing; neither guarantees success or bypasses a quest-sealed door. Further actions include melee, stealing, climbing, bow/spell cover, beneficial spells, inventory carrying and exchange. Every action has approach, start, progress, success/failure, interruption and cancellation states; report actual results back to dialogue and memory. The model never directly edits save state, grants rewards, removes locks or invents success.

Crimes committed on request need a deliberate attribution policy for actor/player, witnesses, reputation and guard response. Do not silently make companion theft consequence-free or assume player liability without that decision. Pathfinding, climbing, friendly fire, line of sight, ammunition, spell cost, limits, equipment art and unloaded execution all require distinct tested adapters.

## 5. Modular delivery and installation — P01–P04

Retain the existing request for independent chapters: LLM/free-text/server/model setup, finite guards, patrols/posts, household recovery/house sale, disposition pricing/services and other compatible subsystems. Dependencies must be explicit; toggles cannot produce duplicate guards, lost identities or corrupt saves. Simulation and NPC action logic should remain usable without an LLM; future non-chat commands can provide equivalent access.

Current launcher uses prebuilt Windows llama.cpp and Qwen GGUF, so Python is not required for player use. Recommended optional installer direction is a PowerShell bootstrap or packaged launcher consistent with current tooling, rather than adding Python solely to download files. A Python installer is an alternative only if it provides a concrete benefit; it would require a pinned tested interpreter/dependency environment documented before release.

Public setup must document mod installation, external mod order, tested DFU/runtime/model revisions, official downloads and checksums, required DLL placement, LLM opt-out, model consent/download size, local-only endpoint, startup/prewarm distinction, health checks, error recovery, removal/update behavior and minimum/recommended hardware based on measurement. Tested Windows/NVIDIA/CUDA configuration is not evidence for AMD/Intel/CPU/macOS/Linux support. Do not invent minimum VRAM from GGUF file size.

## 6. Backlog crosswalk and acceptance

All rows remain open unless a linked evidence document explicitly proves a narrower existing behavior. “Recorded” is not “implemented.”

| IDs | Requirement | Existing track / dependency |
| --- | --- | --- |
| B01–B03 | Movement/door continuity, factual next destination and safe 2× time migration | M2/M3; current itinerary and observed-visit probes; complete before more schedule overrides |
| B04 | Guard/inn survey stalls and bounded background work | M7; PERFORMANCE.md |
| I01–I03 | Census, identity/ownership, versioned adoption/migration | M2/M7; NPC-INTEGRATION-AUDIT.md; prerequisite for new roles |
| H01–H03 | Housing-first local market workers, varied commute, weekend/demand market visits | M2/M3/M7; identity and navigation |
| H04–H06 | Tenant bed priority, keeper commute/locks, tenant keys and after-hours access/loot | M2/M3; housing, native services, corpse keys |
| H07–H09 | Inn traveler origins/kin visits, daytime itinerary/checkout/home return, explicit shared beds | M2/M3; travel reservations, room inventory, art and persistence |
| H10 | Institutional roles/personality and actual bed reservations; no new external workday routine | M2/M3; service and quest capability checks |
| A01–A02 | Actual-provider walking/running/idle/sleep coverage; shared-bed composition | M3/M7; DIRECTIONAL-CIVILIAN-ANIMATION.md; inspect before generation |
| Q01–Q03 | Native quest preservation, authored story personality, capability-limited bed movement | M1/M6/M7; original identities and native lifecycle |
| Q04–Q07 | Speaker/stage knowledge projection, spoiler tests, vanilla adapter matrix, deferred pack review | M6/M7; quest audit and no whole-script prompt |
| M01–M03 | Dated persistent memory, relevant retrieval, shared activities/listeners and disclosure filtering | M3/M6; FUTURE-DIALOGUE-AND-VOICE.md |
| F01–F06 | Invitations; human stats/effects; recruitment; inventories; progression; world-action framework | Later M3/M6 extension; shelved; identity/memory/combat/action dependencies |
| P01–P04 | Player install guide, optional bootstrap, measured support matrix, modular packaging | M6/M7; INSTALLATION.md; no mandatory LLM dependencies |
| E01 | Existing crime/guard/haunting, household recovery, visits/travel and visual behavior | M2–M5/M7; existing domain documents remain binding |
| E02 | All paid services/pricing/refusal; rank-sensitive expulsion and castle-noble consequences | M3/M6; existing MILESTONES.md requests retained |
| E03 | Native quest dialogue with disposition-aware tone without semantic/action changes | M6/M7; disclosure/ownership policy above |
| E04 | English voices and broader NPC appearance work | Later M6/M7; preserve existing voice backlog, do not prioritize over broken movement |

Acceptance is per slice: core behavior, native bindings, save/load, player-observed result and performance evidence where applicable. Publishing plans or passing core tests does not certify all quests or layouts. No “everything active everywhere” per-frame simulation is required; unloaded positions/actions must be determinable consistently with bounded cost.


<a id="delivery-plan"></a>

# NPC lifecycle and quest compatibility delivery plan

> **For agentic workers:** Use the `executing-plans` skill for a selected implementation slice. This is a planning backlog, not authorization to execute all phases. Delegation requires user authorization and the configured Luna/high ceiling.

**Goal:** Turn the NPC inventory and accumulated requests into independently reviewable deliveries without losing native quests, identity or physical continuity.

**Architecture:** Stable identity and native ownership underpin role-specific mobility. Housing reservations precede generated population; quest capabilities and information disclosure are explicit. Unloaded simulation remains bounded and deterministic where possible.

**Tech stack:** DFU 1.1.1, Unity 2019.4.40f1, C# 7.3 runtime adapters; .NET 8 core tests; existing PowerShell build and owned-player probes; optional local llama.cpp server.

**Spec:** [NPC lifecycle and quest-dialogue design](ROADMAP.md#detailed-design). Read alongside the [TL;DR roadmap](ROADMAP.md) and [current inventory](NPC-SYSTEMS.md#inventory).

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
