# Combat art production — 28 October 2026

Active production. Selected melee candidates currently cover appearances 381–398: eight directions and four phases each (576 frames). Original-walk rear/profile pouch reconciliation is complete for these candidates; these are not final identity-approved sets. No runtime art was replaced; integrated=false and user_approved=false throughout.

## Source boundary

Native civilian MobilePersonBillboard supports Idle and Move. Its 24 civilian families are 381–398 and 451–456; guard 399 is separate. Records 0–4 contain walk poses; record 5 contains two frontal idle poses, not melee/bow attacks. Enemy attack record layouts do not supply identity-correct civilian artwork automatically.

Parent source inventory: ../source-inventory.json. The 357 known provider-qualified identities are an inventory, not completed combat coverage. Stationary/mod families and most mobile families remain open.

Native frontal idle art anchors identity. ORIGINAL native walk records have priority for directional outfit visibility. Earlier generated standing derivatives omitted or misplaced rear pouches; they are not authoritative native evidence. Family manifests retain derivative references separately from original-walk evidence and SHA-256 values. Motion templates use selected 385 phases; rear-left grip remains partially occluded and is not a fully approved reference.

## Selected outputs

- candidate-melee-{381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398}-eight-directions: 32 PNG frames, atlas, eight preview GIFs and provenance manifest each. Order: ready, windup, strike, recovery. Directions: front, front-left, left, rear-left, back, rear-right, right, front-right.
- melee-{archive}-directions-0-3-review.png and -4-7-review.png: native-scale contact sheets with fixed head/foot guide lines. Personally inspected by production agent and parent for 381/382/385. Seven views have plausible consistent weapon hand; rear-left strike/recovery wrist path is hidden behind torso and remains an explicit final inspection limitation.
- candidate-bow-right-pair: 385 right-profile draw/release pair only. No full bow cycle or eight-direction bow coverage.
- Raw PNGs and prompt-*.txt retain generation/repair evidence. Rejected originals remain outside selected family manifests.

385 initial strips failed camera/weapon continuity. Source-locked right-profile keyframe repair established a consistent near weapon arm; ready and recovery then completed four phases. Rear-left strike needed a correction. 382 rear-left strike hand switch was corrected; selected near left hand stays empty.

SUPERSEDED source assumption: 381 rear pouch was initially removed using generated standing references. Original native381 record3/4 shows the pouch; earlier back/rear-left v1 is now restored, rear-right v2 adds it. Separate original382/383 checks also show rear pouches; targeted edits restore them. Native384 likewise shows pouch in profile/rear despite frontal-idle omission; five v2 strips restore it. Profiles381–383 v2 relocate pouch behind hip as native record2 shows. 385 native rear records do not show a pouch; rear-right v2 removes the derivative's brown pouch. Updated contact pages were personally inspected. Preserve this history; previous removal prompts are superseded, not current guidance.

392 headwear correction: all eight v1 strips incorrectly omitted the blue projecting bonnet visible in original native walk records. Frontal idle alone had led to an incomplete headband interpretation. Selected v2 strips restore the blue crown/side panels in every view; rear hair hides most of the bonnet from back. Both updated contact pages were inspected by the production agent and parent. Hash-bound parent evidence and remaining limits are in 392-parent-review.json. Prior v1 source interpretation is superseded.

## Tool block

388 source correction: the initial production pass misread small brown/orange native pixels as a rear pouch. Enlarged original walk records 2/3/4 show no pouch. Both profiles and all three rear views now remove the invented pouch (v2, except rear-right v3). Earlier pouch-addition/relocation prompts are superseded. Front and front-quarter pouches remain based on original frontal idle. Parent identified the contradiction; production agent independently verified enlarged originals and re-inspected both corrected contact pages.

385 bow nock/ready request was rejected by image_gen input moderation (violence), requestId 28e491ac-0539-4d32-8336-38158f33d65b. generation-block.json and prompt-bow-right-nock-blocked.txt retain the failure. No retry/rephrase of that denied request was attempted. Bow nock/recovery remains missing.

## Technical contract and limits

technically_checked means dimensions/hashes/alpha/pivots measured; visually_reviewed means inspected, not user approval. All candidates remain separate from integrated art. Raw alpha preserved: most foreground alpha is 252/253, near opacity, with partial edge pixels. No thresholding, erosion, palette replacement or scripted anatomy repair applied. Generated images are not pixel-identical to native art.

Packaging uses nearest-neighbor scale shared across each four-phase strip and foot-center/baseline translation. Pixel canvas 190×260; foot pivot [95,244]. Pixel dimensions are separate from world size: MobilePersonBillboard calculates native size + int(size × scale / 256), applies texture overrides, then GlobalScale. Native idle and walk heights can differ while producing similar world height. Manifests retain native ScaleXY and pre-override size; no runtime world size was set.

GIFs are inspection aids, not audiovisual/runtime acceptance. No Unity/game/build/desktop used. No shared queue, runtime code, installed bundle or source art changed.

## Reproduce checks

From DFU workspace:

    python LivingCities/art/production-20261028/combat/check_combat_art.py
    python LivingCities/art/production-20261028/combat/pack_directional_melee.py
    python LivingCities/art/production-20261028/combat/pack_family_strips.py 381
    python LivingCities/art/production-20261028/combat/pack_family_strips.py 382

Scripts measure/crop/pack generated art and compose review sheets with Pillow. Artwork creation/repair uses image_gen. Review sheets must be inspected after packing; script completion alone is not visual acceptance.




