# Source classification review

On 28 October 2026 the parent inspected all five `source-contact-*.png` sheets (133 DET/RMB source references) and all seven `remaining-source-contact-*.png` sheets (207 additional references from enabled installed packages). These are source-classification contacts, not animation acceptance.

The first group contains 77 DET stationary appearance references from archives 10011–10020 and 56 RMB NPC references from archive 1200. DET 10014/1 has a serpentine lower body; DET 10020/1 is a dragon. They require separate action designs and must not silently receive an ordinary humanoid walking rig. RMB 1200/20 looks statue-like and 1200/24 is a partial-body source; both remain individual-review cases. Source poses also include sitting, kneeling, carrying props and raised arms; a source flat is not proof of a standing or moving frame.

The remaining group contains the E&B replacement 182/33, all 43 E&B resource appearances, six Lively Cities NPC replacements in archive 1200, and 157 non-NPC prop/statue/building references. E&B 1306/0–2 are cat-bodied Alfiq and remain special-anatomy cases, consistent with the prior exclusion from humanoid sleepers. The other E&B figures and six Lively Cities figures are humanoid. The prop groups comprise Rosy's resources 1210/24 and 56790, religious/desert/RMB statues in 1230, miscellaneous 218/540/1210/1220 objects, and building 1400/1401 textures. These objects do not become missing NPC animation jobs merely because a numeric archive/record exists.

The DET mobile folder `10050-10055 orc` contains 42 textures belonging to exactly two filename families, 1035 and 1053. Each has five walking records with four frames and one frontal idle. The folder name does not prove six families or their live runtime archive mapping. Evidence is in `mobile-locomotion/det-mobile-orc-coverage.json`.

The union inventory currently records 436 provider-qualified body identities before complete native applicability review. This is an intentionally conservative known-source inventory, not 436 approved humanoid NPCs. It includes current FLATS.CFG and the older visual catalogue as separate evidence: 206 current config records, 209 older visually catalogued records, 226 in their union. Five important source/config hashes still match the 21 October evidence.

The bundle index is complete for all 22 installed dfmod files at scan time; current settings enable 21. File paths and first-frame inspection cannot establish that every resource is instantiated by live gameplay. Loose replacements, dynamically selected assets, nonstandard texture names and special-body actors remain separate coverage questions.

## Native extraction and source review

All 226 native frame-zero references were subsequently recovered from unchanged TEXTURE archives and ART_PAL.COL. `extract_native_sources.py` follows the current engine decoder; retained independent references for 182/0 and 385/2/0 match decoded alpha and visible RGB pixels. All eight `native-source-contact-*.png` sheets were inspected. TEXTURE.199 records 0–16 are editor markers, so 17 known source rows are explicitly excluded from NPC animation production. All of archive 175 receives an authored-special-character anatomy flag; its unusual bodies must not be silently treated as ordinary human rigs. Native 184/34 contains a mounted rider and needs a separate dismounted body design.

The rebuilt inventory now includes available native source paths and 22 exact RGBA-plus-dimensions equivalence groups. The known-source total remains 436, with 17 excluded editor markers; it is still not a count of accepted ordinary humanoid characters. The installed loose Textures tree was checked and contained only readme files, including its CifRci and Img subdirectories, at this checkpoint.

## Pixel dimensions are not world dimensions

Native 381 walking record 2 is 110 pixels high with record scale -70, while idle record 5 is 165 pixels high with scale -134. `MobilePersonBillboard.cs:321–346` applies `size + int(size * scale / 256)` before global scaling and replacement overrides, giving respective heights 80 and 79. This explains the native pixel-size difference; it is not a 50% in-game height change. Future exports must record their pixel canvas, anatomy scale, pivot and intended world size separately. A candidate with short torso/legs relative to its head still fails anatomical comparison even when the global size is correct.
