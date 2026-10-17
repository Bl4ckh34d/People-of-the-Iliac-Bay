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
