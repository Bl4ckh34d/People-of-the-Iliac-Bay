# Stationary and mobile voice identity evidence catalog

This evidence supplement extends [`stationary_voice_age_catalog.json`](stationary_voice_age_catalog.json) without changing its 209 reviewed native-flat age judgments. It records native flat sex codes, static race resolution, mod replacement rules and art, mobile sprite families, and new E&B resource art. It keeps race, personal origin, and current location separate.

## Coverage

- **209 visually reviewed stationary flats** from the same 13 archives as the age catalog. Current `../GameFiles/arena2/FLATS.CFG` contains matching records for 189 of these assets: 102 male, 86 female, and one unknown (`176/1`, raw `-1`). Twenty catalog assets have no matching record in the current file, so their sex stays unknown. The older `work/sleep-source/appearances.csv` has 226 flat rows and conflicts with current `FLATS.CFG`; do not use its sex codes for those 20 assets. Raw `?2` means female with the classic ChildGard censorship marker. Static NPC race is not a flat property: DFU resolves `StaticNPC.Data.race` from the NPC faction, or falls back to current-region race. That fallback describes runtime appearance, not hometown.
- **Two active direct E&B appearance replacements** are in the enabled `Elves & Beastfolk - Replaces` bundle at load priority 17: static art `texture-flat:182:33`, and CIFRCI portrait face record `451`. Current `FLATS.CFG` assigns faceIndex `451` to flat `182:33`, with raw gender `?2`; `TalkManager` uses the selected static flat's faceIndex for its portrait. This CIFRCI ID is not the separate mobile billboard archive `451`. The static replacement is image-only; it leaves runtime faction/region race and native sex behavior intact. Its art supports only a low-confidence adult hint. Neither replacement carries individual origin.
- **106 image assets** from `Elves & Beastfolk - Resources` were extracted and visually reviewed: 43 base flat-texture resources across archives 1300–1308, 20 additional image frames, and 43 CIFRCI portrait images. Its manifest calls the group elf and beast-race flat-NPC resources, but has no per-file NPC selection table. Individual race, sex, age, and origin therefore remain unknown. The local provider catalog lists 40 of the 43 base IDs for sleep-art image/cell lookup; it omits 1306:0–2, and its provider-specific lookup has no repository callers. It supplies no identity metadata and does not prove that current NPC appearance or voice selection uses those assets. A filename-encoded portrait/flat index is not proof of a person mapping.
- **25 mobile sprite assets** are catalogued. DFU source maps 24 face-record families to Breton, Nord, or Redguard and male/female variants. Archive `399` is not in those race/sex mappings and remains unknown. No individual mobile-sprite age is assigned. A mobile sprite family is not an NPC's birthplace or home.

The active package snapshot records all 22 entries from `Player/PortableAppdata/Mods/GameData/Mods.json`; disabled packages are marked. The extracted mod texture inventory contains 472 rows from seven providers. Only E&B Replaces overlaps a native NPC flat archive, at `182:33`; its bundle lists one flat PNG, despite two duplicate inventory rows with conflicting hashes. Flat Replacer is active and can override `Race` or `Gender` when a replacement rule specifies those fields. Its deployed `LivelyCitiesFlatReplacements.json` has seven rules targeting only building/architecture textures `1200:{14,15,17,19,53,54}` and `10015:0`; it has no NPC flat targets and no `Race` or `Gender` overrides. The conclusion is limited to these installed bundles, rules, and decoded assets.

## Evidence and use

Native flat gender and portrait indices come from current `../GameFiles/arena2/FLATS.CFG`, parsed with DFU `FlatsFile` block semantics. The old `work/sleep-source/appearances.csv` does not match this file and must not be treated as current evidence. `StaticNPC.cs` carries race and sex on `NPCData`; static NPC race comes from faction race or current region, while sex comes from the flat gender flag. `TalkManager.cs` resolves static portraits from flat `faceIndex`. `MobilePersonNPC.cs` selects face/billboard families for Breton, Redguard, and Nord by runtime race and sex; other race values display the Breton billboard family. Mobile billboard archive `451` belongs to a different ID namespace from static CIFRCI face record `451`.

For actual characters, use explicit identity-bearing game/mod state when available. `NativePerson.Sex` and `NativePerson.Race` are authoritative for registered residents; static NPCs expose `NPCData.gender` and `.race`. A settlement assignment or current-region race fallback is not a birthplace. Traveler home is usable only when the actual `HomeSettlement` field exists for that traveler. Do not infer origin from names, facial features, art style, current inn/city, travel position, or race. If the game state has no authoritative origin, keep it unknown.

`stationary_voice_age_catalog.json` remains the source for native-art age hints. For a mod replacement, use the replacement's separate, explicitly low-confidence visual age field rather than reusing an estimate from the covered native image. Sex is a registry assignment aid; it does not imply pitch or override a directly heard/verified voice identity.

## Source files

- `../GameFiles/arena2/FLATS.CFG` — current native sex codes and portrait face indices; SHA-256 recorded in the JSON catalog. The older CSV export is retained only as a stale artifact and has a documented mismatch.
- `work/sleep-mod-source/inventory.json` and `work/sleep-mod-source/eb-full/` — decoded mod image inventory, full E&B contact sheets, and extracted asset hashes.
- `../Player/DaggerfallUnity_Data/StreamingAssets/FlatReplacements/LivelyCitiesFlatReplacements.json` — active Flat Replacer rule configuration.
- `work/external-decompiled/flat-replacer/FlatReplacer/FlatReplacer.cs` and `FlatReplacementRecord.cs` — local decompilation of the installed runtime fields/selection path.
- `../daggerfall-unity/Assets/Scripts/API/FlatsFile.cs` — native flat metadata definition.
- `../daggerfall-unity/Assets/Scripts/Game/MobilePersonNPC.cs` — mobile race/sex mapping and runtime behavior.
- `../daggerfall-unity/Assets/Scripts/Game/StaticNPC.cs` — static NPC faction/region race and flat-flag sex resolution.
- `../daggerfall-unity/Assets/Scripts/Game/TalkManager.cs` — static NPC portrait resolution from selected flat faceIndex.
- `../src/PeopleOfTheIliacBay.Core/NativePeople.cs` — explicit native resident fields.
- `../src/PeopleOfTheIliacBay.Core/TravelerState.cs` — traveler home field where present.
- `../src/PeopleOfTheIliacBay.Core/SleepArtProviderCatalog.cs` and `SleepArtCatalog.cs` — provider-specific image/cell lookup; source search found no provider-lookup call site.
- `../Player/PortableAppdata/Mods/GameData/Mods.json` — active mod order snapshot, with source hash recorded in the JSON catalog.
