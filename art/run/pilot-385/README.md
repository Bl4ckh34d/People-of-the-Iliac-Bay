# Archive 385 running animation pilot

This remains rejected pilot art. The user authorized one bounded actual-player
preview of this current draft on 16 October 2026, despite its known defects.
That runtime trial does not accept the art. Front-left, side and rear-left views
do not reliably reverse arm lead at the half-cycle, and side views lack clear
leg-passing poses. See `ARM-CYCLE-REVIEW.md`.

This pilot adds running art for one ordinary adult mobile appearance: Breton male,
outfit variant 0, texture archive 385. It is not a global texture replacement.
The caller must enable it only during fleeing and only on this appearance.
Normal walking and idle remain native.

## Assets and provenance

- `source/385-{record}-{frame}.png`: actual installed Daggerfall frames extracted
  through DFU TextureReader, not screenshots or generated references.
- `source/records.csv`: native dimensions, frame counts and scale metadata.
- `source/walking-contact.png`: inspected original five-view walking reference.
- `generated-run-v1.png`: initial built-in Imagegen output.
- `generated-run-v2.png`: built-in Imagegen correction of side/rear-left views.
- `run-385-atlas.png`: deterministic native-scale packing of the corrected art.
- `run-385-preview.png` and `.gif`: inspection sheet and four-frame animation.
- `manifest.json`: all runtime dimensions, mapping and frame rectangles.
- `pack.py`: repeatable nearest-neighbor resize, binary alpha and packing.

The tool generated RGBA artwork. Packing thresholds alpha at 128 and preserves
the opaque pixels. It uses one common scale across all frames, not individual
body stretching. Generated row spacing was irregular; verified transparent
gutters at y=298,567,838,1105 prevent slicing feet or adjacent heads. Packing
creates a 384x640 atlas, four columns and five rows of 96x128 cells. Ground pivot
is (48,120) in top-left cell coordinates. Odd frames have a three-pixel flight
offset. Maximum body height is 106 pixels. Filtering is Point, no mipmaps.

## DFU contract

Native `MobilePersonBillboard.MoveAnims` has eight orientations:

| Index | View | Native record | Mirror |
|---|---|---|---|
| 0 | Front | 0 | No |
| 1 | Front-left | 1 | No |
| 2 | Left profile | 2 | No |
| 3 | Rear-left | 3 | No |
| 4 | Back | 4 | No |
| 5 | Rear-right | 3 | Yes |
| 6 | Right profile | 2 | Yes |
| 7 | Front-right | 1 | Yes |

Native walking records have four frames each, at 4 fps. Their pixel sizes are
52x110,65x110,71x110,64x110,52x109, all with scale (-70,-70). Native idle is
record 5, two frames, 72x163, scale (-134,-134). Pilot uses four generated run
frames per view at 10 fps, a 0.4-second cycle. Movement speed remains owned by
the existing fleeing system; this art component never changes speed or routes.

`RunningPilotPose` clones the native quad and material, disables only the native
renderer while running and restores its previous enabled state on exit. Native
billboard animation/orientation continues underneath. The overlay follows the
native camera-facing rotation. Source record-0 height supplies pixel-to-world
scale. Overlay center sits one source pixel above native center, aligning its
ground pivot with the native bottom. Atlas UVs use Unity bottom-left coordinates;
manifest rectangles use image top-left coordinates.

Runtime preview integration:

```csharp
actor.SetRunningPilotAtlas(texture); // shared texture; owner disposes at shutdown
actor.Place(ground, direction, standing, fleeing); // fleeing=true only during active escape
```

`ResidentActor` permits living adult male Breton outfit variant 0, which DFU
maps to archive 385. It restores native rendering for walking, idle,
conversation, sleep, death and standing. Do not globally substitute this atlas
for native archive 385. Pilot remains appearance-scoped and temporary.

## Verification and limitations

Source extraction passed in `work/run-source-extract.log`, with Unity exit 0.
All five source views were inspected before generation. The corrected generated
sheet and packed atlas were inspected; all 20 figures are complete, transparent,
and free from grid-slicing debris. `python art/run/pilot-385/pack.py` verifies 20
frames, nonnegative cell offsets and binary alpha. It prints
`RUN_PILOT_PACK_PASS`.

This is a rejected pilot, not final art acceptance. Generated run poses differ
from walking through bent elbows, lifted knees and kicked-back heels. Some
contact silhouettes remain similar; arm reversal and side leg passing are
incomplete. Judge motion quality and identity continuity in game. The opt-in
`tools/TownTest.ps1 -FixtureCharacter <fixture> -RunningPilotProbe` exercises a
disposable actor, eight orientations, four frames and renderer restoration. Its
screenshots and runtime result are diagnostic evidence, not gameplay or art
acceptance. No live-game result is claimed until that probe runs.

## Generation prompts

Built-in Imagegen was used, not CLI/API fallback. First prompt requested an
identity-preserving 4x5 transparent run sheet from the inspected walking contact
sheet: the same brown-haired bearded adult, orange shoulder yoke, teal tunic,
yellow zigzag hem, brown belt pouch, gray leggings and green shoes; five native
view directions; four successive running phases; bent elbows, lifted knees,
heel kick-back and slight forward lean; classic low-resolution Daggerfall style.

Final corrective prompt:

> Edit target image1, image2 native reference. Keep exact 4 columns by 5 rows
> sheet, same transparent background, character identity, outfit and run cycle
> intent. Correct view angles ONLY in rows3 and4: row3 must be an EXACT screen
> LEFT side profile: one eye maximum, nose silhouetted left, far shoulder and arm
> occluded, torso side narrow, hips side narrow. Running motion travels strictly
> screen LEFT. Row4 must be rear-left 45degrees: see back and LEFT SIDE of
> head/tunic, no face, no beard, unequal shoulders, motion clearly diagonal away
> toward screen LEFT. Currently row4 is wrongly almost identical to straight
> back row5. Reference image2 row3 and row4 show intended angles. Row5 stays EXACT
> rear straight away; row2 front-left diagonal; row1 exact front. Keep all frames
> full body and uncropped, consistent centered placement and size. Preserve bent
> elbows pumping, raised knees, kick-back heels and distinct four run phases,
> avoid duplicate frame poses. Maintain low-resolution classic Daggerfall pixel
> art appearance, not cartoon. No text, labels or grids.
