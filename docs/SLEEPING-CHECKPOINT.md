# Sleeping foundation checkpoint — 14 October 2026

The current installed 0.3.0 development bundle includes bed geometry and assignment code. NPC sleeping poses are not active yet. No M2/M3 completion is claimed.

## Verified behavior

`HomeBeds.Assign` derives stable logical claims from permanent household IDs and stable interior bed record indices. Couples share, additional residents use spare beds, and a five-person family may share one bed without reducing population. Dead members retain their allocation in the derivation so survivors keep their slots; only living members receive active claims. New household identities will create new assignments. Enumeration order and serialization do not change the result. Foreign residents cannot use this household API as a substitute for tavern booking.

`BedGeometry` extracts the mattress from actual original model geometry rather than assuming the furniture bounding-box top is the mattress. This matters for canopy bed 41002. Placement follows DFU's rotation, scale and prop-bottom rules.

Evidence:

- `dotnet run --project tests/LivingCities.Tests -c Release`: 94/94 passed, including seven new bed tests. The seven cases first failed against an explicit unimplemented allocator; logs are `work/bed-assignment-red.log` and `work/bed-assignment-green.log`.
- `tools/Build.ps1`: Unity 2019.4.40f1 exit 0, native serialization/settings/adapter and bundle checks passed. Original bed models 41000–41002 passed mattress bounds, canopy exclusion, rotation, upstairs offset and prop placement checks. Geometry had a prior failing native test before implementation (`work/bed-geometry-red.log`).
- `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -BedProbe`: all three models matched loaded mattress colliders within 0.005 metres: 41001 in building 4/record 13, 41000 in building 16777216/record 21, and canopy bed 41002 in building 1/record 22. Five resident claims share the family home's one bed and reproduce after loading the real family save. The same run passed existing street animation, interior continuity, death/reload and family tests. Evidence: `work/bed-runtime-pass.log`. Read-only review identified the initial one-model scene coverage gap; the expanded run closes it.
- Source survey exported 226 FLATS.CFG records, 25 mobile appearance archives and three bed meshes. `art/sleep/source-appearances.csv` is uncurated metadata: it includes nonhuman candidates and does not prove complete human coverage or accepted assets.

## Current art direction

The user chose a thick white blanket covering the body to the neck, with only the face/head visible. Reusable blanket poses reduce outfit work, while the face and adult/child proportions preserve identity. The camera usually sees a bed from the side and slightly above; directional side/angled/head/foot views are required. A horizontal overhead cutout is not the finished renderer.

`art/sleep/drafts/flat-182-1-blanket-side-v1.png` is the first side/angled study from original flat 182/1. Built-in image generation produced 2138×736 RGBA pixels with transparent corners, despite the native-scale prompt. It needs native-scale/style review, directional consistency and actual in-engine inspection. Two earlier overhead images are retained as explicitly superseded drafts. None is packed into the playable mod.

## Remaining implementation

Connect schedule sleep/wake to actual bed poses, preserve actor/service/quest identity and collider behavior, and verify morning waking, time skips, settings changes, death and save/load. Define family blanket compositions that physically fit shared beds and handle missing/unsupported geometry explicitly. Bind original home/shop NPCs without duplicates, including authoritative quest household surnames and destinations. Implement paid traveler/player room rights before tavern sleepers or occupied-bed enforcement. Expand approved sleeping faces/views across all required human appearances, including children.
