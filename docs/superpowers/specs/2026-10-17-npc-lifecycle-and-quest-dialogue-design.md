# NPC lifecycle, story-aware dialogue and future companions

Date: 17 October 2026. Status: **user requirements captured; architecture proposed for review; implementation deferred**.

This consolidates the user's latest decisions with [current NPC evidence](../../NPC-INTEGRATION-AUDIT.md), [additional quest evidence](../../QUEST-PACK-COMPATIBILITY.md), [relationships](../../RELATIONSHIPS-AND-FUTURE-INVENTORIES.md), and [dialogue/memory](../../FUTURE-DIALOGUE-AND-VOICE.md). The short navigation entry is [Roadmap](../../ROADMAP.md). Nothing below establishes that the feature already works.

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
