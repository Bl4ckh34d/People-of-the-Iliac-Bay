# Mobile locomotion production checkpoint, 28 October 2026

## Result

One new archive 385 left-profile opposite-arm running keyframe is generated and visually reviewed. It is a useful pose candidate, not a complete cycle, exact palette match, runtime asset or user-approved result. Two preceding eight-frame sheets are rejected because their camera-near arm keeps leading forward throughout both half-cycles.

Open `opposite-keyframe-comparison.png` for the native walk contact pose, failed whole-strip pose and new opposite-arm pose. Equal bounding height in this preview is for inspection only; it is not runtime scale or pivot packing. `385-run-opposite-keyframe-v1.png` is the unchanged generated source. Prompts are stored beside each output. All three calls used built-in Imagegen; SpriteCook tools were unavailable.

## Verified source coverage

`audit.py` parses installed native texture headers using the structure in DFU `TextureFile.cs`. All 24 civilian archives (381–398 and 451–456), plus guard 399, contain records 0–4 with four frames each. `MobilePersonBillboard.MoveAnims` maps eight bearings to records `[0,1,2,3,4,3,2,1]`; the final three use the native mirror contract. Native walking therefore already supplies 192 civilian bearing sequences plus eight guard sequences. This is native reuse, not newly authored animation.

`audit_det.py` reads the installed Daggerfall Expanded Textures bundle through UnityPy. Its folder named `10050-10055 orc` contains 42 Texture2D objects, but exactly two filename families: **1035 and 1053**. Each has records 0–4 and frames 0–3, plus record 5/frame 0. The source preview confirms a male orange-clad Orc and a female blue-skirted Orc. The folder label does not prove six runtime families. No runtime assignment to archives 10050–10055 was found in the bundle text assets. Pigeon folders are excluded. See `det-mobile-orc-coverage.json` for paths, dimensions, pixel hashes and the unresolved mapping boundary.

## Visual and alpha findings

The native-referenced single pose finally places the visible near upper arm and elbow behind the torso, while the far fist remains forward. The whole-sheet attempts did not achieve this. The single pose remains a candidate: clothing identity is recognizable, but precise native palette, proportions, temporal consistency, frame scale and pivot still need production work.

Raw tool previews suggested colored background haze. Local compositing corrects that impression: fully transparent pixels hide that RGB, and sprite core alpha is mostly 252–253. No fully opaque alpha-255 pixels exist. That fact alone is not a cutout failure; the native billboard cutoff is 0.5. No alpha-only retry or erosion was performed. Local previews were inspected with `view_image`. The new keyframe has clean readable cutout at preview scale; no game rendering claim is made.

## Remaining gaps

- No complete newly accepted running cycle exists for any civilian appearance. The 192 civilian run bearing sequences remain open. Existing 385 pilots are unapproved/rejected, not completion evidence.
- Guard 399 running remains open in all eight bearings.
- The two DET Orc filename families already have walking source frames. Running is absent in the inspected mobile texture sets; their runtime identity mapping remains unresolved.
- Archive 385 now has one opposite-arm profile candidate. Seven additional chronological phases and seven other bearings remain absent from this new attempt. Do not count this keyframe as an eight-frame sequence.
- No gameplay changes, Unity or game execution, build, desktop control, integration or user approval occurred.

`coverage.json` keeps `generated`, `technical_checked`, `visual_reviewed`, `integrated` and `user_approved` separate. Its `visual_passed` flag on the single keyframe only applies to the explicitly stated pose scope. Technical checks cover readable files, alpha, source metadata and hashes; they do not prove animation quality.

## Repeatable checks

From LivingCities:

```powershell
python art/production-20261028/mobile-locomotion/audit.py
python art/production-20261028/mobile-locomotion/audit_det.py
```

The scripts are read-only against native archives and the installed mod. They write only this production folder's compact reports and inspection previews. They do not duplicate native or mod source textures, alter generated artwork, or package a runtime atlas.
