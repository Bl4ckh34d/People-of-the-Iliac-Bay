# Gothway Garden test saves

Verified 14 October 2026 in the isolated portable player with Basic Roads and Living Cities enabled.

- Character: `LivingCitiesTest-a2dd8579` (disposable developer character, god mode).
- `SAVE3`: `Living Cities - inside Gothway Garden`, map pixel 109,159.
- `SAVE4`: `Living Cities - outside Gothway Garden`, adjacent pixel 110,159.
- Root: `../Player/PortableAppdata/Saves` relative to this repository.

Both saves contain DFU save/quest data, `mod_livingcities.txt`, and rendered screenshots. Screenshots were inspected: the inside save shows town streets and houses; the outside save shows surrounding countryside. The introductory popup is dismissed before the new fixtures are saved.

`tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-a2dd8579` starts a fresh owned player process, loads inside/outside/inside, verifies the character and map positions, confirms the outside location differs from Gothway Garden, and compares the complete serialized virtual population after each load. All three loads passed with 162 candidate households and identical state. The runner stops only its own process and refuses to interrupt an existing isolated player.

Running `tools/TownTest.ps1` without a character creates a new uniquely named disposable character and two additional save slots, then verifies those loads. It never overwrites another character's saves. `SAVE1`/`SAVE2` are earlier valid fixtures for `LivingCitiesTest-0cb7deaa`; `SAVE0` is an incomplete earlier development attempt and must not be used. These older files remain preserved.

Reproducible creation needs rendered graphics because DFU saves a screenshot using end-of-frame callbacks. Pure headless startup checks are separate. Initialize `CharacterDocument.backStory` and `biographyEffects`: a default document leaves them null, and saving the biography otherwise fails. Do not move the world clock backwards after new-character initialization; DFU's player time bookkeeping rejects that discontinuity.

This verifies real save-slot population continuity, not physical actor continuity, quest movement, deaths during gameplay, or bed allocation. Those remain acceptance work. Logs are retained in ignored `work/town-test.log`.
