# Testing and verification status

Read each result's date and scope. Source tests, runtime compilation, a Unity bundle and player acceptance are distinct checks. Historical evidence is in [History](HISTORY.md); compatibility limits are in [Compatibility](COMPATIBILITY.md).

Run core checks from the repository with `dotnet run --project tests/LivingCities.Tests --configuration Release`. Compile runtime source with `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release`. `tools/Build.ps1` packages/installs only when DFU is closed; it refuses to interrupt a player's game. `tools/TownTest.ps1` runs owned-player fixtures and similarly refuses an existing session. Read switch prerequisites before selecting a probe.

Performance work must record game/build/save/mod combination and measured frame times; a lower update frequency alone is not proof of fewer stalls. Prior guard/inn survey timings and regressions remain in the [performance evidence](HISTORY.md#source-performance-md). The observed destination/door and inn-movement regressions remain open until a specific player test accepts them.

<a id="latest-acceptance"></a>

## Latest acceptance — 17 October 2026

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


<a id="fixtures"></a>

## Regression fixtures

Use the current character above. `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -DialogueProbe` runs the native UI, clock, memory, home-marker, grounded dialogue, disposition and existing service/compatibility checks. It owns its launched game process and refuses to interrupt an existing one.

`tools/TestDialogueMemory.ps1` runs saved old-detail recall and separate-person missing-memory checks against the local model without opening DFU. It requires the configured loopback llama.cpp server. The core suite independently checks save isolation, deep copy, bounded retrieval, old relevant turns, dates, future timestamps, claim provenance and epoch precision.

Older specialized saves, commands and acceptance checkpoints are preserved in [historical fixture evidence](HISTORY.md#source-test-fixtures-md). They are not current selectable saves and must not be presented as current instructions.
