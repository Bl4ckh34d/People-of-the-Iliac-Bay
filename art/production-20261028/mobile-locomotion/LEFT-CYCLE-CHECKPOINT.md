# Archive 385: first four-phase left-profile candidate

This supplements the earlier single-keyframe checkpoint without changing its historical report. The complete new candidate is `left-cycle-v2/`, with authoritative review/provenance in `manifest-reviewed.json` and additive coverage in `coverage-cycle-supplement.json`.

The four chronological phases are near-foot contact, far-knee flight, far-foot contact and near-knee flight. They use four distinct, source-conditioned generated images. The original opposite-arm keyframe supplies the appearance master; both contact poses additionally reference the native four-frame walking strip. The near-flight correction references the master and opposite flight geometry. There is no sprite reflection, duplicate-frame substitution, manual repaint or palette remapping.

The visible near arm is behind the torso in phases 1 and 4 and forward across the torso in phases 2 and 3. Each flight pose has four pixels of clearance above the contact baseline. Packing uses the same 0.096 nearest-neighbor scale and head anchor for every image; vertical placement establishes contact or flight only. These are genuine pose changes, not translated copies. All four source images share the same 1142 × 1377 canvas and closely matching head/torso proportions.

The four-frame cycle is a **review candidate**, not final approved animation. The contact sheet shows consistent appearance and clear arm reversal, but turquoise saturation and small cloth folds vary between generations. The long tunic conceals much of the near/far upper-leg crossing. Four-frame cadence omits intermediate passing/support poses. No native game playback or final temporal acceptance is claimed.

The earlier `left-cycle-v1/` used the original master as its near-flight phase and visibly lifted that phase's head too far during foot alignment. The new single-source-conditioned near-flight v2 reduces this asymmetry without moving or stretching image parts.

Reviewed locally using `view_image`:

- `left-cycle-v2/contact-sheet.png`: all phases, common body scale, baseline.
- `opposite-keyframe-comparison.png`: native identity and visible arm-depth comparison.

Available for playback: `left-cycle-v2/preview.gif` at eight fps. The four PNG frame files and atlas retain generated alpha. Fresh checks confirm four unique source/frame hashes, unchanged raw copies, exact atlas-cell equality, four GIF frames and no threshold-128 cutout clipping. Mod/native source coverage remains unchanged. Archive 385's other seven bearings, every other mobile run family and runtime integration remain open.

Repeat verification from LivingCities:

```powershell
python art/production-20261028/mobile-locomotion/review_left_cycle.py
```

Status: generated, technically checked and visually reviewed. Integrated: false. User approved: false.
