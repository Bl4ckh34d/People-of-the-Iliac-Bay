# Testing and verification status

Read each result's date and scope. Source tests, runtime compilation, a Unity bundle and player acceptance are distinct checks. Historical evidence is in [History](HISTORY.md); compatibility limits are in [Compatibility](COMPATIBILITY.md).

Run core checks from the repository with `dotnet run --project tests/LivingCities.Tests --configuration Release`. Compile runtime source with `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release`. `tools/Build.ps1` packages/installs only when DFU is closed; it refuses to interrupt a player's game. `tools/TownTest.ps1` runs owned-player fixtures and similarly refuses an existing session. Read switch prerequisites before selecting a probe.

Performance work must record game/build/save/mod combination and measured frame times; a lower update frequency alone is not proof of fewer stalls. Prior guard/inn survey timings and regressions remain in the [performance evidence](HISTORY.md#source-performance-md). The observed destination/door and inn-movement regressions remain open until a specific player test accepts them.

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


<a id="fixtures"></a>

# Gothway Garden test saves

## Indoor guard fixture

SAVE31, Living Cities - shop guards, belongs to LivingCitiesTest-f238d317. It stores a wounded responder inside surveyed shop 131342. Enable General / ExperimentalExteriorGuards before loading; this legacy setting name now includes surveyed interiors. The GuardBuildingProbe command overwrites only this dedicated fixture, drives a native indoor guard alarm, accelerates to the travel deadline, verifies doorway placement/native combat and wounded direct-interior save/load, then checks eventual post return.

## Guard response fixture, 14 October 2026

Character LivingCitiesTest-f238d317 now has SAVE30, named Living Cities - guard response. This disposable slot contains ten roster guards, an active response and a wounded guard. Enable General / ExperimentalExteriorGuards for the exterior controller. Guards / MaximumResponders and ActorRadius tune response size and actor visibility. Indoor guards still use vanilla behavior.

Reproduce with tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -GuardControllerProbe. This overwrites only the dedicated guard-response fixture. SAVE24 remains the ordinary town exploration starting point.

Verified 14 October 2026 in the isolated portable player with Basic Roads and Living Cities enabled.

## Current bed-compliant fixture, version 0.3.3

Use `LivingCitiesTest-f238d317`. New population: 162 candidate homes, 181 residents, eight children. Household occupancy never exceeds actual bed count; one NPC per bed. Older characters below remain preserved historical fixtures and should not be overwritten by current tests.

- SAVE24: **Living Cities - inside Gothway Garden**.
- SAVE25: **Living Cities - outside Gothway Garden**.
- SAVE26: **Living Cities - interior continuity**.
- SAVE27: **Living Cities - resident death**, disposable regression case.
- SAVE28: **Living Cities - family home**, four-person home, building 131584.
- SAVE29: **Living Cities - civilian response**, disposable post-crime fight/flight and save/load case.

Start from SAVE24 or SAVE28 to explore ordinary behavior. SAVE29 includes a killing and native guard response. Native guards are still unlimited in this checkpoint; finite roster replacement is the next gameplay slice.

## Current family preview, version 0.3.0

Use character `LivingCitiesTest-126494eb` for the current family preview:

- `SAVE11`: `Living Cities - inside Gothway Garden`, map pixel 109,159.
- `SAVE12`: `Living Cities - outside Gothway Garden`, adjacent pixel 110,159.
- `SAVE13`: `Living Cities - interior continuity`, sampled living adult.
- `SAVE14`: `Living Cities - resident death`, disposable death-persistence case.
- `SAVE15`: `Living Cities - family home`, a five-person household with child Agryn Ashfield visible inside.

Run `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb` to verify family and existing adult behavior. The actual player passed with 162 households, 402 residents and 118 children. It checked housing profiles from actual game assets, no adult visits for children over a sampled day, shared surnames and saved family fields, child appearance and name after indoor reload, and four adult walking frames with physical displacement. The selected child is `s:206728581:h:4:r:2` in building 4. Children currently stay home using original boy/girl flats; this is not animated outdoor play or sleeping.

The UI check opens the native mobile conversation boundary and verifies the brief child greeting. It then simulates a static conversation with the stale mobile target retained and checks that the normal talk window remains. This is a focused UI regression check, not broad quest/service dialogue acceptance. The family screenshot was inspected after an actual camera visibility/line-of-sight check. Evidence: `work/family-runtime-pass.log`, `work/legacy-family-migration-pass.log`, and `LivingCities-family.png` in the player's normal LocalLow screenshot directory.

Legacy fixture validation also passed on schema 3 without creating children or rerolling its 324 adults. SAVE7–SAVE10 belong to an earlier disposable allocation regression run and are not the recommended family preview. All earlier saves remain preserved.

Optional `-QuestProbe` adds native quest identity checks after the normal tests. It creates/updates SAVE16, **Living Cities - quest identity probe**, on the same disposable character. This save contains a synthetic active quest bound to original tavern NPC Vannolda Woodston in building 65542. Movement, native activation, hide/show and reconstructed save/load passed. Use SAVE15 for the normal family preview. `QUEST-IDENTITY.md` records the exact scope and evidence. Scripted entries now perform native building discovery and activation setup; live input is disabled only in the automated process, preventing accidental door activation during assertions.

## Sleeping preview, version 0.3.1

Version 0.3.2 extends `-SleepProbe` to male and female appearance cases. SAVE17/SAVE18 remain the male pair. SAVE21, **Living Cities - sleeping preview - female**, belongs to `LivingCitiesTest-126494eb` and opens beside Elyzyna Wickford in building 66049 at 23:00. SAVE22, **Living Cities - sleeping death - female**, is the disposable dead-resident check. Use SAVE21 for observation. Both supported appearances now use green blankets and white pillows; the actual-player suite passes waking, toggles, targeting, save/reload and death persistence for each. Side and foot-end views were inspected. All other appearances and shared beds remain unsupported. Evidence: `work/sleep-green-runtime-pass.log`; see `SLEEPING-CHECKPOINT.md` for exact limits.

Additional developer fixture: SAVE19, **Living Cities - quest binding ledger**, same character, contains a synthetic active quest and matching schema-4 mod contract. Its recipient/UID can change on each run. `Build.ps1 -QuestBindingProbe` followed by `TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestBindingProbe` creates/updates this fixture and checks native quest plus mod sidecar across real save/load. The expanded test now exercises native guild offer creation and message-button handlers for refusal, stale-recipient rejection and acceptance, then recipient placement/name/activation and reload. It does not establish actual item hand-in, reward or broad quest compatibility. Use SAVE17 for ordinary sleep observation. See `QUEST-IDENTITY.md`.

SAVE20, **Living Cities - vanilla delivery**, same character, is saved inside the recipient's house with an active unchanged A0C00Y14 delivery and its native package in player inventory. Latest verified recipient: Elyzyssa Buckingham, building 260, UID 33554466; regeneration can select a different recipient/package/reward. `Build.ps1` then `TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -DeliveryProbe` exercises automatic binding and native social offer acceptance, saves/reloads this slot, checks journal naming, hands in through the native NPC click handler, and collects native reward loot through the inventory transfer handler (40 gold in the latest passing sample). Completion happens after the saved checkpoint, so reloading SAVE20 restores the active delivery. This does not test random offer selection, mouse raycasting or every delivery branch. The delivery-only flag now runs focused fixture-load and delivery checks; combine `-QuestBindingProbe -DeliveryProbe` after `Build.ps1 -QuestBindingProbe` to include the full street/interior/family and synthetic quest regression suite. The latest combined run passed; evidence is `work/quest-auto-runtime-pass.log`.

Character `LivingCitiesTest-126494eb` now has SAVE17, **Living Cities - sleeping preview**, at 23:00 beside Edwore Coppercroft's upstairs bed in building 263. SAVE18, **Living Cities - sleeping death**, is the disposable dead-resident regression save; use SAVE17 for observation. Both names and character ownership were verified from actual save metadata.

Run `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -SleepProbe` for the complete current check. It passes schedule transitions, preserved native actor/name, horizontal interaction capsule and raycast targeting, immediate sleep toggle, side/foot viewpoints, actual living save/load, death persistence through another reload, and restoration of the living preview. The same run passes the existing family and street checks. Sleeping is limited to one adult appearance using unshared home beds. No tavern booking, shared-family pose, child sleeping, native questor sleeping or stealth waking is included. See `SLEEPING-CHECKPOINT.md` for image and camera-test limitations.

## Historical adult fixture, version 0.2.0

- Character: `LivingCitiesTest-a2dd8579` (disposable developer character, god mode).
- `SAVE3`: `Living Cities - inside Gothway Garden`, map pixel 109,159.
- `SAVE4`: `Living Cities - outside Gothway Garden`, adjacent pixel 110,159.
- Root: `../Player/PortableAppdata/Saves` relative to this repository.

Both saves contain DFU save/quest data, `mod_livingcities.txt`, and rendered screenshots. Screenshots were inspected: the inside save shows town streets and houses; the outside save shows surrounding countryside. The introductory popup is dismissed before the new fixtures are saved.

`tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-a2dd8579` starts a fresh owned player process, loads inside/outside/inside, verifies the character and map positions, confirms the outside location differs from Gothway Garden, and compares the complete serialized virtual population after each load. All three loads passed with 162 candidate households and identical state. The runner stops only its own process and refuses to interrupt an existing isolated player.

Running `tools/TownTest.ps1` without a character creates a new uniquely named disposable character and two additional save slots, then verifies those loads. It never overwrites another character's saves. `SAVE1`/`SAVE2` are earlier valid fixtures for `LivingCitiesTest-0cb7deaa`; `SAVE0` is an incomplete earlier development attempt and must not be used. These older files remain preserved.

Reproducible creation needs rendered graphics because DFU saves a screenshot using end-of-frame callbacks. Pure headless startup checks are separate. Initialize `CharacterDocument.backStory` and `biographyEffects`: a default document leaves them null, and saving the biography otherwise fails. Do not move the world clock backwards after new-character initialization; DFU's player time bookkeeping rejects that discontinuity.

## Physical continuity checkpoint, 14 October

The expanded test returns to the HUD after loading (matching DFU's load button) and enables background updates only in the opt-in test process. It finds a scheduled traveller, advances time forward if needed, observes actual displacement and at least three walking animation frames, then follows the resident's real destination door. The current fixture observes four animation frames, 324 schedules and 13 nearby active actors; those counts describe the sampled situation, not a performance benchmark.

Identity/name/appearance remain unchanged inside. Exit/re-entry leaves exactly one active actor for the sampled ID. `SAVE5`, named `Living Cities - interior continuity`, stores the living resident in building 13. The harness reloads that interior save and verifies the living actor again. It then calls the actual entity's death method, saves `SAVE6` as `Living Cities - resident death`, reloads and confirms the dead record with no actor respawn. The original inside/outside SAVE3/SAVE4 remain unchanged. These tests exercise the death callback, not melee hit detection, murder attribution or guard response.

Schema 2 saves include route plans; the actual FullSerializer round trip and indoor reload pass. Schema 1 fixtures migrate without changing resident identities. New cache completion can vary between loads, so the fixture comparison covers every population field but excludes the newly derived route cache; dedicated core and interior-load checks cover route persistence.

Screenshots of street and interior actors were inspected. Interior placement currently uses reachable standing anchors, not beds or furniture activities. Quest movement, full leave/return simulation across map boundaries, origin shifts, melee combat and bed allocation remain acceptance work. Logs are retained in ignored `work/town-test.log`.

## Public witness probe (0.3.8)

`tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -GuardWitnessProbe` uses disposable actors after loading the normal town fixture. It creates no save slot. It checks actual native weapon interception, absence of automatic distant guard response, direct witness reporting near a post, immutable crime destination, bounded repeat reporting and native serialization. The witness is repositioned deliberately; autonomous flight-to-guard is not asserted. Run focused probe switches separately: the player harness exits after the selected scenario.

## Indoor escape (0.3.9)

`tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -InteriorEscapeProbe` creates/replaces **SAVE32 — Living Cities - escaping witness** in house 1. It checks blocked-start rejection, a real weapon kill, delayed notification, physical escape movement, actual save/load, identity and movement outside, and finite reporting to the original house. Both guard and civilian-reaction options must be enabled when inspecting this fixture. The probe advances time to later travel deadlines; it does not benchmark all routes or interior layouts. Older character fixtures remain separate.

## Residential family fixture and tavern audit, 14 October 2026

Use **SAVE34 � Living Cities - residential family home**, character **LivingCitiesTest-f238d317**, for family observation. Building 196864 is House2 in residential block RESIAM10.RMB, model 320, with three beds and three residents (two adults, one child). The actual-player HousingProbe verifies all three actors and creates this separate save. SAVE28 and all older saves remain unchanged.

The reported SAVE28 is building 131584. Both raw assets and the loaded native BuildingDirectory classify it as House2, not Tavern. It uses model 249 in TVRNAM00.RMB; actual Tavern 131590 reuses that model. The resemblance is real, but the tested allocation is residential. All three actual taverns (65542, 66061, 131590) are absent from both HousingSurvey profiles and the saved household list. Do not infer building use from exterior model or block-name prefix alone.

Reproduce: `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -HousingProbe`. Evidence: `work/HousingProbe-pass.log`, markers LIVING_CITIES_HOUSING_LIVE_PASS and LIVING_CITIES_RESIDENTIAL_FIXTURE_PASS. Tavern staff homes and paid traveler reservations remain separate unfinished systems; this audit does not claim they are implemented.
