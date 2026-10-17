# Testing and verification status

Read each result's date and scope. Source tests, runtime compilation, a Unity bundle and player acceptance are distinct checks. Historical evidence is in [History](HISTORY.md); compatibility limits are in [Compatibility](COMPATIBILITY.md).

Run core checks from the repository with `dotnet run --project tests/LivingCities.Tests --configuration Release`. Compile runtime source with `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release`. `tools/Build.ps1` packages/installs only when DFU is closed; it refuses to interrupt a player's game. `tools/TownTest.ps1` runs owned-player fixtures and similarly refuses an existing session. Read switch prerequisites before selecting a probe.

Performance work must record game/build/save/mod combination and measured frame times; a lower update frequency alone is not proof of fewer stalls. Prior guard/inn survey timings and regressions remain in the [performance evidence](HISTORY.md#source-performance-md). The observed destination/door and inn-movement regressions remain open until a specific player test accepts them.

<a id="latest-acceptance"></a>

## Latest acceptance — inn-bedroom household reservations, 17 October 2026

Installed bundle SHA-256: `E331D6B9404968A9592CE105AD3A48B97F68AB5FD73ECDB536C9483455A2111B`, matching `dist/livingcities.dfmod`.

- **693/693 core tests passed**. New regressions first reproduced missing permanent homes, sole-lodger vacancy recovery and admission into a pending replacement household. Runtime compilation has zero warnings/errors; Unity build/install passed through the existing normal-editor license fallback.
- Core coverage includes guest/staff/hall separation, excess bedroom occupants, capacity reservation before family generation, known full homes without eviction, unavailable and bank-sale property exclusions, partial-town completion, schema-23 migration, corrupt/duplicate records, bed conflicts, death, historical generations and save restore after recovery rejection.
- The owned-player probe loaded `People - Shop entrance`, entered actual inns and allocated homes for native bedroom occupants. For example, Luck Sheffield (`npc:206728581:131078:-105`) reserved hometown `1380150225`, home `65538`, slot 0. Nine entrance-connected main-hall NPCs received no foreign reservations.
- The probe completed that actual hometown's population from surveyed profiles, then used native `SaveLoadManager` serialization/deserialization and store restore. The same person, home and slot remained; no duplicate generated home resident existed, the absence query remained true, and conversation home lookup resolved the reservation. Dedicated sale-building and temporary player-deed checks rejected residential availability through the production survey helper.
- Repeat: `./tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -InnLodgerHomeProbe`. Evidence: `work/inn-lodger-home-red.log`, `inn-lodger-recovery-red.log`, `inn-lodger-recovery-race-red.log`, `inn-lodger-home-core.log`, `inn-lodger-home-build.log`, `inn-lodger-home-native.log`, and `inn-lodger-home-native-player.log`.

No player save was overwritten. This verifies data identity, hometown completion and native serialization, not a rendered visit to the hometown or physical checkout/return travel. Shared temporary beds, universal main-hall housing and every modded inn layout remain outside acceptance. Previously allocated property is not retroactively evicted.

## Prior acceptance — keeper identity, tenancy and keys, 17 October 2026

Installed bundle SHA-256: `32A9B56F837CAF2826D5BC8A66C92344F622F09FAC4CD8A68FEBDA0332982ACD`, matching `dist/livingcities.dfmod`.

- **680/680 core tests passed**. The role-prompt, temporary-inn-key exclusion and legacy-key availability regressions failed before their corrections; a bed-assigned shop tenant retains the home key without workplace confirmation. Runtime compilation has zero warnings/errors; Unity build/install passed.
- A native shop fixture first reproduced “Bed-owning tenant was classified as shopkeeper.” The corrected player probe rejects tenant ownership/business context, preserves a bed-backed tenant key, filters a legacy non-keeper workplace claim without resetting its ledger, and recognizes the actual selling NPC as keeper.
- A static adult ignores a stale mobile child; inverse mobile identity and memory ignore a stale static NPC. Five local inn doors reject key eligibility through the production filter.
- Repeat: `./tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -BusinessIdentityProbe`. Evidence: `work/business-role-red.log`, `business-keys-red.log`, `business-legacy-red.log`, `business-role-red-player.log`, `business-role-core.log`, `business-role-runtime.log`, `business-role-build.log`, `business-role-native-player.log`.
- No player save was overwritten and no foreign home was invented for an authored inn occupant. Native inn staff classification and universal permanent-home adoption remain planned. This probe does not certify every service transaction, LLM reply, physical door-key interaction or modded inn layout. Previously transferred non-inn keys lack issuer provenance and are not retroactively revoked.

## Prior acceptance — occupied interior approaches, 17 October 2026

Installed bundle SHA-256: `EECF1FACE0DA42A6B6B53E17E4010FD7ED0FD971C553CD72C27CF1451B8156E9`, matching `dist/livingcities.dfmod`.

- **677/677 core tests passed**, including two new regressions that failed before the correction. Runtime compilation and Unity build/install passed.
- The owned-player fixture loaded `People - Shop entrance`, entered an actual general store and occupied a generated shelf goal. The approaching NPC reached a free nearby position at the same shelf; its captured visit validated with connected remaining legs.
- A blocked approach held position, heading and idle pose for 90 rendered frames, then resumed after the blocker left. Existing-overlap recovery and wall rejection passed core tests.
- Repeat: `./tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -OccupiedApproachProbe`. Evidence: `work/occupied-approach-red.log`, `occupied-approach-core.log`, `occupied-approach-runtime.log`, `occupied-approach-build.log`, and `occupied-approach-native-player.log`. No player save was overwritten. Other route-stall diagnostics still appear; this does not establish universal crowd deadlock resolution.

## Prior acceptance — stable personal walking variation, 17 October 2026

Installed bundle SHA-256: `E29B7DAF3ED1A257C2237726775969D213E41A47C743163D132196C9FCDACAC8`, matching `dist/livingcities.dfmod`.

- **675/675 core tests passed**; runtime compilation has zero warnings/errors; Unity build/install passed. New tests first reproduced absent walking variation and missing personal speed in legacy daily caches.
- Across 10,000 appearance seeds, walking stays within 1.105–1.495 m/s, includes slower/faster people, has over 100 distinct values, remains stable per seed and averages close to 1.3 m/s. Existing fleeing values exactly match the previous formula across 1,000 seeds.
- The native old Daggerfall fixture checked **778 daily route legs** against each owner's personal speed after migration. The rendered test resident measured **1.3888 m/s** against its assigned **1.3779 m/s** walking pace. Running measured **3.3473 m/s** against its unchanged **3.3605 m/s** pace. Simultaneous game/real clock ratios were **2.0225** and **1.9988**.
- Repeat using the same `-MovementClockProbe` command below. Evidence: `work/movement-variation-red.log`, `movement-variation-red-cache.log`, `movement-variation-core.log`, `movement-variation-runtime.log`, `movement-variation-build.log`, and `movement-variation-native-player.log`. The fixture probe did not overwrite player saves.

Personal walking pace applies to mobile citizens, their local schedules and indoor movement, and newly planned travel. Existing fleeing variation stays intact. Coordinated guard formation speeds and the previously documented migration limit for old paid inter-settlement trips are unchanged.

## Prior acceptance — physical movement with 2× time, 17 October 2026

Installed bundle SHA-256: `F43956C9225C2FF5EA99F41AAEA70FA43AA2BB233456721B761855F1B384A0B4`, matching `dist/livingcities.dfmod`.

- **672/672 core tests passed**. Both new tests failed before the corrections: cached daily routes moved only one sixth of the intended speed, and short fallback visits lost speed through whole-minute commute rounding. Runtime compilation has zero warnings/errors; Unity build/install passed through the existing normal-editor license fallback.
- Loaded the original schema-21 `People - Daggerfall day` fixture. All **778 cached daily walking legs** evaluated at 1.3 m/s with world time at 2×.
- A rendered native resident using production `ResidentActor`/`CrowdMotion` movement measured **1.3066 m/s** walking against 1.3 expected, and **3.3486 m/s** running against 3.3605 expected for that appearance seed. Simultaneous game/real clock ratios were **2.0167** and **1.9996** over four real seconds each. The movement measurement uses a clear elevated test floor to isolate physical speed from crowd congestion.
- Core checks cover schema 21/22/23 caches, old and already-correct clock encodings, source snapshot preservation, idempotent restore, unchanged dwell durations, and exact physical fallback walking at both 12× and 2×.

Run `./tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -MovementClockProbe` with DFU closed. Evidence: `work/movement-clock-red.log`, `movement-clock-core.log`, `movement-clock-runtime.log`, `movement-clock-build.log`, and `movement-clock-native-player.log`. No player saves were overwritten by this probe.

Scope: repairs local daily caches and fallback travel. New inter-settlement plans already use the active clock. Older inter-settlement trips containing paid stays or linked admission histories retain their existing timing; an atomic trip/booking migration remains open. This speed check does not establish that every crowded route reaches every appointment.

## Prior acceptance — shared memory archive, 17 October 2026

Installed bundle SHA-256: `E4AD0FD0CFDAC0525DBCCC6DF5A93589C41325EE9A18B48030D1AE8F58D76FD0` (matches `dist/livingcities.dfmod`).

- Core: **670/670 passed**; runtime compilation: zero warnings/errors; Unity build and integration checks passed. The existing normal-editor fallback handled a batch-mode license rejection.
- Native disk save/load: created a base save and two divergent futures, then restarted the game completely and loaded all three again. The base recalled neither future; each branch recalled only its own additions, despite identical conversation dates. Separate NPC isolation passed.
- Each native serialized archive checkpoint measured **157 characters**. A core test added 1,000 exchanges and verified unchanged total serialized snapshot length for otherwise identical world data; committed transcripts are absent from saves. Other world-state fields can still grow normally.
- Legacy inline migration, repeated-import deduplication, unchanged saves, unsaved-memory rollback, pending entries after failed writes, missing/corrupt ancestry and transactional restore passed.
- Portable export/import passed against a real native save: identical mod payload, exact ancestry, existing-slot refusal, path-traversal rejection and corrupt-ancestry rejection.
- Native Talk reopened with an empty transcript and the live model recalled an earlier archived personal detail. Current clock, home marking, grounded dialogue, disposition, room pricing/refusal, indoor guard dispatch and final compatibility transitions passed in the completed owned-player run.

Evidence: `work/archive-core.log`, `work/archive-runtime.log`, `work/archive-build.log`, `work/archive-native-write-player.log`, `work/archive-native-read-player.log`, `work/archive-transfer-test.log`, and `work/archive-dialogue.log`. The initial dialogue run exposed an unconfigured archive in an old test-only clone; runtime probe clones now use the active archive root. Core storage and the native branch probes were unchanged by that helper correction.

Back up the entire Saves directory, including `PeopleMemory`, or use [portable export/import](INSTALLATION.md#memory-backups). History consumes shared disk space once; automatic pruning and multi-year load/recall performance are not implemented or established by these tests.

## Prior memory/home/clock acceptance — 17 October 2026

Installed bundle SHA-256: `09172B40E7C6D4C53A6D6813190149CFAB787DBC736DE757ACAA3BF222D47897`.

- Core: **662/662 passed**. Runtime compilation: zero warnings and errors. Unity build and native FullSerializer memory round-trip passed.
- Loaded-game clock: **8 game seconds / 4.006598 real seconds**. Supported active journeys preserve progress and remaining real duration during migration; historical itinerary calendar bookings are not all retimed.
- Native Talk: recorded production free-text and native-option exchanges, actually closed and reopened the conversation, restored through DFU's serializer, and received a live model reply recalling an earlier personal detail with an empty current transcript. Separate-person isolation passed. This probe uses native serialization, not a complete game restart/load cycle.
- Home map request: an outdoor resident's actual saved home key `197128` resolved to an unnamed `House2`. Low trust and disabled map requests prevented discovery; disposition 20 allowed an exact home marker, labelled `Home of Ennia Etienne`. Existing player and quest labels are preserved. Indoor home marking was not separately live-tested.
- Existing grounded dialogue, clock questions, disposition, room pricing/refusal and final compatibility transitions passed in the same owned-player run.
- Standalone local-model memory checks passed for old-detail recall after 80 intervening turns and another NPC's missing-memory response. These are targeted results, not a guarantee that the model never invents or misinterprets anything.

Evidence: `work/memory-core-final.log`, `work/memory-build-final.log`, `work/memory-native-final.log`, and the matching native probe markers in the player's log. Memory starts with recorded interactions after this update; earlier unrecorded conversations cannot be recovered. Unidentified/unadopted actors and future companion activity memories remain outside current coverage; see [NPC systems](NPC-SYSTEMS.md).

<a id="saves"></a>

# Current test saves — 17 October 2026

Character: **LivingCitiesTest-9a5f197b**. These five saves were generated from a new character with the current external city, village, wall and tavern mods. Each save was immediately loaded and checked for the expected character, settlement, exterior scene and no active crime.

| Save | Purpose |
| --- | --- |
| People - Gothway Garden | Daytime street population and conversations |
| People - Shop entrance | Enter the current general store layout for shopping/keeper tests |
| People - Inn entrance | Enter the current inn layout for patrons, rooms and keeper tests |
| People - Daggerfall day | Large-city population, performance and guards |
| People - Daggerfall night | Haunting, curfew and guard behavior |

All saves are outdoors to avoid persisting an obsolete interior scene. They contain the standard new-game quests, not injected crime/welfare/quest-debug scenarios. The test launcher enables GodMode during generation; use the console if a test specifically requires ordinary player damage.

The former disposable test characters and their obsolete saves were removed from the game's Saves directory. A recovery archive is outside the game menu at `DFU/backups/obsolete-test-saves-20261017`, with a manifest. It also contains four rejected preview slots from an interrupted first generation attempt; those are not valid fresh fixtures.

Regenerate with `./tools/TownTest.ps1 -FreshPreviews`. This creates a new test character and never deletes previous saves automatically. The generator verifies all five reloads and reports `LIVING_CITIES_TOWN_TEST_PASS: fresh previews`. Evidence: `work/FreshPreviews-pass.log`.

The other older regression commands may still expect their named, purpose-built fixtures. Do not silently substitute a clean save for a scripted murder or quest fixture; rebuild the intended scenario first.

The shared-archive probe also creates three diagnostic slots on this disposable character: `People - Memory base`, `People - Memory future`, and `People - Memory branch`. They contain synthetic same-date conversations used to prove old-save and branch isolation. They are not additional ordinary starting fixtures.


<a id="fixtures"></a>

## Regression fixtures

Use the current character above. `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -DialogueProbe` runs the native UI, clock, memory, home-marker, grounded dialogue, disposition and existing service/compatibility checks. It owns its launched game process and refuses to interrupt an existing one.

`tools/TestDialogueMemory.ps1` runs saved old-detail recall and separate-person missing-memory checks against the local model without opening DFU. It requires the configured loopback llama.cpp server. The core suite independently checks save isolation, deep copy, bounded retrieval, old relevant turns, dates, future timestamps, claim provenance and epoch precision.

Shared-archive checks, in order, with DFU closed:

```powershell
./tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -MemoryArchiveProbe
./tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -MemoryArchiveProbe -MemoryArchiveRead
./tools/TestMemoryArchiveTransfer.ps1 -SaveFolder 'C:/path/to/Saves/SAVE12'
```

The second command starts a separate game process and loads all three disk saves again. Use the actual folder for `People - Memory base` in the transfer test. That test exports/imports a real save into an isolated temporary directory, checks the payload and archive, and rejects overwriting, path traversal and corrupt ancestry. Runtime probe clones must use `LivingCitiesMod.ConversationArchiveRoot()` when restoring a checkpoint; an unconfigured core store intentionally rejects nonempty archive heads.

Older specialized saves, commands and acceptance checkpoints are preserved in [historical fixture evidence](HISTORY.md#source-test-fixtures-md). They are not current selectable saves and must not be presented as current instructions.
