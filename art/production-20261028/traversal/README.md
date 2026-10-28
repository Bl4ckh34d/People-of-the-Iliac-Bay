# Traversal production checkpoint — 28 October 2026

Scope: missing climbing, jumping and swimming artwork in eight bearings. This folder contains appearance381 action candidates and appearance382 follow-up candidates, not completed coverage of the source inventory. No gameplay code, Unity build, Player installation or shared production queue was changed.

## Current review artifacts

| Action | Packed output | Technical checks | Visual result | Integrated / user-approved |
|---|---|---|---|---|
| Jumping | `381-jump-eight-views-v1/`,32frames,8bearings×4phases | RGBA, four distinct phases per bearing, no alpha128 clipping, exact atlas cells | All32frames inspected. Directional poses work as a candidate; face, proportions and pouch consistency remain limited. | No / No |
| Climbing | `381-climb-eight-views-v1/`,32frames,8bearings×4phases | RGBA, four distinct phases per bearing, no sourcecrop/frame alpha128 clipping, exact atlas, shared scale per bearing | New reach/pull/tuck/push movement; all32frames reviewed. Front-right camera repaired. Far-limb occlusion, identity variation and exact wall contact remain unaccepted. | No / No |
| Swimming | `381-swim-eight-views-v2/`,32frames,8bearings×4phases | RGBA, four distinct phases per bearing, no alpha128 clipping, exact atlas, one scale per bearing | Long-body master expanded to8bearings. Front/rear camera genuinely regenerated lower; rear recovery arms repaired. Parent prefersv2; quarter-camera, anatomy and pouch drift remain. | No / No |

Jump previews: `381-jump-eight-views-v1/review-bearings-0-3.png`, `review-bearings-4-7.png`, `cycle-eight-views.gif`.

Climb previews: `381-climb-eight-views-v1/review-bearings-0-3.png`, `review-bearings-4-7.png` (also2x versions), and `cycle-eight-views.gif`. The selected32frames use a new reach/pull/tuck/push sequence. Old contralateral contact work in `381-climb-right-cycle-v2` and `381-climb-rear-cycle-v1` remains separate diagnostic evidence; its poses were not relabeled as the new movement.

Swim previews: `381-swim-eight-views-v2/review-bearings-0-3.png`, `review-bearings-4-7.png`, `cycle-eight-views.gif`. Source comparison `381-swim-right-cycle-v2/native-comparison.png` displays unchanged native idle73×165 and walking profile67×110, then the four corrected profile phases at shared2× zoom. Profile phases use one factor21/206. Each additional bearing has one head-based scale shared by all its phases; no per-pose bounding-height fitting. `381-swim-rear-camera-repair-v1/camera-comparison.png` records the camera correction. Old compact profile and too-high front/rear cameras remain superseded evidence.

## Production method and evidence

- Built-in Imagegen produced genuinely new limb poses, with native appearance381 sources and later successful keyframes as conditioning images. Prompts are retained beside generated sources.
- Initial32-cell sheets were not accepted. Camera/phase inconsistency and clipping triggered a narrower single-keyframe route. Front jumping was generated as four independent poses, then seven directional four-phase strips.
- Two targeted Imagegen repairs removed a duplicate green pouch in jump bearing1; selected source is `381-jump-view-1-generated-v3.png`.
- Packing uses nearest-neighbor sampling and transparent placement only. No mirrored or translated idle image is described as a new animation phase. Packed output alpha is retained. Review panels simulate alpha cutoff128 without changing delivered frames.
- Partial alpha alone is not a failure. Initial alpha counts were misleading: visible foreground above cutoff has median251–253. Actual contact reviews, clipping, anatomy and camera consistency decide visual status.
- Every packed frame and atlas has SHA-256 provenance in its local manifest. Initial-sheet pixel reports are in `technical-checks.json`. Reproduction tools are local to this folder and do not mutate source artwork.

## Native source findings

Native appearance381 idle source is `art/standing/source/native-idle/381-0.png`,73×165, SHA-256 `8a62820f6d066ebb5b7da0c94d9b190c966fa96e2c29bc658ab8fe01abef16a4`. Native walking profile is `art/standing/source/381-2-0.png`,67×110. These are different native source scales; the pilot does not claim a proven runtime world-size mapping.

**Scale correction:** native walking scaleY=-70 and idle scaleY=-134 make the110px and165px records nearly equal in world height. `MobilePersonBillboard.CacheRecordSizesAndFrames` uses `size + int(size*scale/256)`: walking gives80 and idle79 before `MeshReader.GlobalScale`. Do not interpret different native pixel heights as physical size differences. `native-scale-contract.json` records this separately from generated pixel canvases and their still-unintegrated intended actor world height79.

**Swim anatomy repair:** `381-swim-glide-repair-v1/native-before-after.png` compares the unchanged sources, compact136px glide and anatomically regenerated194px glide at the same21px head target. Parent inspected and accepted the latter as the corrected body master. Pull, recovery and kick were then independently regenerated from this master and packed into `381-swim-right-cycle-v2` with one shared scale. No canvas stretching or local anatomical pixel painting was used. Parent acceptance of a working master is not final user approval.

`daggerfall-unity/Assets/Scripts/Internal/MobilePersonBillboard.cs` defines only Idle and Move. Walking uses records0–4 and mirrors3,2,1 for the remaining bearings. No native NPC swimming sprite/waterline representation was found in current `LivingCities/mod/Scripts` or `LivingCities/src`; the native Player swimming controller is separate. Swim art therefore remains a full-body transparent candidate with no baked water.

## Remaining coverage

Appearance381 has96selected review-candidate frames:32jump,32swim and32climb, each8bearings×4phases. Appearance382 adds32jump,32climb and32swim frames. Appearance383 adds32jump,32climb and20swim frames. Total276selected candidates; no accepted production artwork or runtime integration. Old contact/contralateral pilots remain excluded diagnostics. Other provider-qualified identities remain unproduced for traversal at this checkpoint. Consult current parent `source-inventory.json` for identities and applicability; its count is copied with its hash into `coverage.json`.

Appearance383 retains the native teal tunic and yellow trim. Jump uses20 authored frames plus12 declared symmetric mirrors. Climb uses32 authored frames with the anatomical right-arm phase; bearing3 pull/tuck were repaired individually. Front size, right-profile lean, sleeve-edge variation and far-limb occlusion remain limits. Swim has20 authored frames at bearings0/1/2/3/6, with genuine joint phases and shared scale within each bearing. Bearings4/5/7 remain open. Rear4 received a tool output moderation block; the already-completed retry is excluded from all selected assets and coverage. Exact requests, error and ordering are retained in `families/383/unselected-evidence/swim-rear-tool-block.json`. No further retry or use of that output is authorized.

Appearance382 jump uses20 authored frames and12 exact native-rule mirrors, suitable only for this symmetric jump sequence. The rule is NOT automatically valid for climbing or swimming, whose anatomical hand phase must remain consistent across cameras. `families/382/jump-eight-views-v1/review-bearings-0-3.png` and `review-bearings-4-7.png` show the32frames at native pixel size. Front0 is visibly larger than other directions; head/body proportions, palette, temporal transitions and physical runtime scale remain unaccepted. Profile takeoff's duplicate pouch was corrected by Imagegen; only that frame uses repaired sourcev2.

`families/382/climb-eight-views-v1/` contains32 authored reach/pull/tuck/push frames, no mirrors. Explicit anatomical RIGHT-arm phase is visible from front/rear and opposing profiles. Far-limb occlusion and greater lean in right profile remain limits. Exact wall contact and time continuity are unaccepted. Rear pouch position was repaired in reach/pull/push, original tuck retained. Native382 frontwalk111px at scaleY=-70 gives81 beforeGlobalScale; generated worldscale is not applied.

`families/382/swim-profile-v1/native-comparison.png` compares unchanged native382 with glide/pull/recovery/kick. One shared head-based factor21/64 preserves long body and actual joint changes. All eight swim bearings now in `families/382/swim-eight-views-v1`,32 authored frames with no mirrors. Rear Glide alone uses a camera repair; quarter-camera elevation, body/head proportions and temporal/waterline behavior remain unaccepted. The preceding381 climb also retains an anatomical-hand continuity risk: independently plausible opposed profiles can raise different body hands. Native walk mirroring alone does not prove any traversal phase semantics.

No runtime integration, complete native-palette equivalence or user visual approval is claimed.

## Climbing diagnostic follow-up

`381-climb-front-diagnostic-v1/contact-review.png` makes opposite arm/knee identity visible. A single-frame repair corrected the right-arm/left-knee pose after a grid repair accidentally moved the knee. The pouch remains on the same screen side, but the repaired phase has different body/head proportions and the pull transitions are not accepted. This diagnostic is excluded from selected coverage.

The additional raw left-profile grid `381-climb-view-2-generated-v1.png` remains an unselected source. The front-left grid `381-climb-view-1-generated-v1.png` is rejected as a cycle because its fourth phase turns to the opposite bearing.

`381-climb-paired-pilot-v1/contact-review.png` established the simpler reach/pull/tuck/push sequence. Parent inspected it and accepted it as a working basis for the remaining bearings. All eight are now in the selected climb candidate; fixed wall grips, far-limb anatomy, weight transfer and temporal behavior remain unvalidated. The original contralateral pilots were superseded for selected coverage.
