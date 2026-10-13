# Gothway Garden test saves

Verified 14 October 2026 in the isolated portable player with Basic Roads and Living Cities enabled.

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
