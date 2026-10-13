# Sleeping foundation checkpoint — 14 October 2026

Version 0.3.1 adds a limited sleeping prototype: generated adult men using mobile archive 385 can sleep in unshared home beds. Other appearances and shared beds still use standing presence. No M2/M3 completion is claimed.

## Runtime prototype

The renderer keeps the same ResidentActor and MobilePersonNPC, hides its standing asset, places a directional blanket/face visual at the native mattress, and changes the existing interaction capsule to a horizontal body. Morning and the live `DailyRoutine/SleepingPoses` toggle restore the original asset and capsule. Five atlas views are selected by camera direction, with mirrored opposite views. The first atlas is oversized generated prototype art, not completed classic-resolution production coverage; its provenance and exact prompt are in `art/sleep/README.md`.

Core tests pass 97/97, including three direction-selection cases. The Unity build checks the five separated atlas views, transparent borders against the native shader cutoff, end/side proportions, eight settings controls and the reopened bundle.

`tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -SleepProbe` passed in the actual player. Edwore Coppercroft (`s:206728581:h:263:r:0`, building 263) sleeps at 23:00 and midnight, wakes with the same native person/name and restored upright capsule at 07:00, and responds to the sleep toggle. Camera raycasts reach the sleeping interaction target. Actual SAVE17 reload restores the living pose; entity death removes the actor, SAVE18 reload retains death, and reloading SAVE17 restores the living preview. This tests the death callback, not melee damage, crime attribution or sleeping dialogue. Existing street/interior/family checks passed in the same run. Evidence: `work/sleep-runtime-pass.log`; earlier failing checks are retained under `work/sleep-runtime-red.log` and camera diagnostic logs. Read-only Luna/high review found no confirmed important production-code issue.

Actual side and foot-end screenshots were inspected: blanket/head are visible on the mattress with transparent surroundings. The waking screenshot points toward the downstairs standing destination but is occluded; the visible standing pose is not established by that screenshot. Waking identity, original asset activation and capsule restoration are established by runtime assertions. Physical stair/bed approach remains unimplemented.

The screenshot harness needed two corrections: native `SetFacing` changes fields but its Update may skip applying transforms in the unattended process; the probe now explicitly applies the native body-yaw/camera-pitch split and checks centered projection. Player positions are grounded by interior floor raycasts near the upstairs bed, excluding the player's own collider, with movement cleared before teleporting. The general standing-anchor survey deliberately does not traverse stairs and cannot supply this upstairs camera position. The corrected preview survives actual reload without the previous below-floor camera placement.

Remaining limits: no shared-bed blanket compositions, child or other adult appearance coverage, original stationary NPC binding, tavern bookings, stealth wake reactions, or walking to/from the bed. Entering/leaving the sleep pose currently snaps between the bed and the existing standing anchor. The native NPC/quest integration is still separate, and broad quest compatibility remains pending.

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

Extend the limited runtime prototype across supported appearances and shared beds. Define family blanket compositions that physically fit shared beds and handle missing/unsupported geometry explicitly. Bind original home/shop NPCs without duplicates, including authoritative quest household surnames and destinations. Implement paid traveler/player room rights before tavern sleepers or occupied-bed enforcement. Expand approved sleeping faces/views across all required human appearances, including children. Verify sleep-specific exit/re-entry, native service/quest activation, waking reactions and physical bed approach before claiming the complete feature.
