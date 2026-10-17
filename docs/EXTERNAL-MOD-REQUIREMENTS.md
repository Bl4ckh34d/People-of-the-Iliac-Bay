# External mod requirements

Latest user selection supersedes the original list below: Lootable Villagers and Families of Daggerfall are removed from both installations and the enabled-mod list. Original archives are retained. Living Cities will implement corpse inventory/keys itself and already owns households/relationships. Rosy's Residences is disabled in favor of the tavern/city/village improvements; Rosy's Resources remains enabled because those improvements require it. See EXTERNAL-MOD-AUDIT.md for the installed order and compatibility evidence.

## Installation checkpoint, 16 October

Later verification: native DFU `NameHelper` generated 400 nonempty names across all eight humanoid banks and both genders using the installed editor override. `DFU/work/external-mods/names-validation.log` records `EXTERNAL_NAMES_VERIFIED` and a clean Unity exit. This verifies generation, not saved-identity migration or visual gameplay.

Property-key integration is in development: custom item and exact-property access, per-corpse persisted claims, and post-transfer inventory observation. `NativeItemTransferHooks` compiles against the player assemblies and observes single/bulk non-stacking transfers only after the item leaves the source and enters the destination. It is not yet attached to corpse inventories; do not claim loot is functional.

The user is downloading archives into `DFU/downloads`. Names of Tamriel Extended 1.1 is installed separately into both player and editor `StreamingAssets/Text/NameGen.txt`. The original RAR remains unchanged. Its JSON omitted a comma between Monster3 sets 2 and 3; the staged copy adds that comma and passes JSON parsing. Both deployed files match SHA-256 `921B560087396021AE55030D5961C2D6010D4AF0B4721CEEEA1D11219857693D`. Runtime name-generation acceptance is pending. Existing saved identities must not be regenerated. This supersedes the initial login request below.

Daggerfall Expanded Textures 1.3.0 and RMB Resource Pack 0.3.0 are installed separately in player/editor Mods folders. Unity successfully loaded both bundles and extracted their actual metadata (`DFU/work/external-mods/inspection.log`, `EXTERNAL_MOD_INSPECTION_SUCCESS`). Player Mods.json enables both before BasicRoads and Living Cities, preserving the existing pair's relative order; its original copy is `DFU/work/external-mods/Mods.before-dependencies.json`. This validates archive loading, not in-game compatibility. Taverns Redone, Lootable Villagers and Detailed City Walls are not yet installed.

Detailed City Walls requires [Daggerfall Expanded Textures](https://www.nexusmods.com/daggerfallunity/mods/307) and [RMB Resource Pack](https://www.nexusmods.com/daggerfallunity/mods/528).

User-approved 16 October 2026. These mods are separately installed dependencies, not assets bundled inside Living Cities.

- Names of Tamriel — https://www.nexusmods.com/daggerfallunity/mods/1310 . Use the installed name generator for newly generated residents while preserving saved identities.
- Taverns Redone — https://www.nexusmods.com/daggerfallunity/mods/43 . Lodging census, room allocation and navigation must use the modified inn geometry.
- Lootable Villagers — https://www.nexusmods.com/daggerfallunity/mods/362 . Managed NPC corpses must offer persistent loot including their home/workplace key. Keys identify a settlement and building, survive inventory/save transitions and open the matching locked house, shop or inn. Prevent duplicate bodies/loot when both mods handle a death. Reuse a suitable native key inventory image if available; otherwise generate one.
- Detailed City Walls — https://www.nexusmods.com/daggerfallunity/mods/221 . Requires Daggerfall Expanded Textures and RMB Resource Pack per its author. Off-duty guards in walled settlements walk into the wall interiors and use unoccupied beds; daytime posts sleep at night and night patrols sleep during the day. Exterior gate guards retire before gates close. This supersedes the earlier off-duty wall-patrol placeholder. Unwalled settlements do not gain guard sleep/rest behavior from this feature.

Installation is pending: Nexus showed a logged-out download gate in the Codex browser. Requested login; do not claim these files are installed until their archives and deployed content are verified. Integration must inspect each mod's actual contents, interfaces and load ordering. Saved vanilla-layout room/route caches need invalidation or migration when replacement geometry changes.
