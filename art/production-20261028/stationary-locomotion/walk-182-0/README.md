# Shopkeeper182/0 walking: eight-view review candidate

The selected set contains eight body viewpoints and four authored phases per viewpoint. It was assembled after correcting collapsed contact/support phases with individual image-generation edits. All 32 native review cells are distinct, remain inside their cells, have the expected binary review cutoff, and match their pinned source file hashes. Each review GIF contains four frames at250ms. These mechanical checks do not replace visual gait acceptance.

The complete contact sheet was reviewed: body viewpoints are distinguishable and corrected contact phases show arm changes. Front-quarter heads remain closer to profile than their bodies. Trousers are redder than the original dark rust-brown. Near/far limb depth and exact pouch handedness remain review limits. This is a useful review candidate, not production approval or installed art.

## Selected delivery

- `walk-eight-view-review-sheet-2x.png`: all32frames with direction labels.
- `walk-eight-view-review.gif`: all eight directions animated together at4fps.
- `walk-eight-view-review-atlas.png`: derived384×768atlas, eight96px rows and four96px columns.
- `walk-review-manifest.json`: exact selected source PNGs, cell indices/crop rectangles, SHA-256 hashes, head registration and common per-view scale.
- `walk-verification.json`: reproducible mechanical evidence from `../verify_walk_views.py`.
- `view-N-review.gif` and `view-N-review-native.png`: per-direction inspection.

Directions are0front,1front-left,2left-profile,3rear-left,4back,5rear-right,6right-profile,7front-right. Every right-side direction was generated from source; no final view is a mirrored left bitmap. Single repairs replace only their selected phase; discarded portions of whole-sheet edits are not silently used.

The actual original identity source is `../../sources/native/182_0-0.png`,47×76pixels, file SHA-256 `de5d67533ab2e9de9a8b3cbfbb4ab8d720976175437577f7c1ec9c6c71d3365a`. Native385/1 and385/3 originals supplied camera/motion references only; their different face and clothes were not intended identity inputs. Their source archive/palette/pixel hashes are in `../native-motion-references.json`.

## Selection and rejected alternatives

Front uses original v1 cells0,2,3 and the generated support-leg repair for cell1. Left profile uses the separately generated four-keyframe cycle. The other directions use v1 sheet cells plus the specifically named `view-N-phase-P-repair-v1.png` overrides recorded in the manifest.

`view-1-quarter-camera-v2.png`, `view-7-quarter-camera-v2.png` and `view-1-quarter-single-unselected-v1.png` are not selected. Whole-sheet camera edits collapsed phases again, and the right-quarter version introduced a moustache. The single-quarter trial changed the torso angle but was not expanded after review found the existing eight body views usable. Do not substitute these files merely because their version number is newer.

`quarter-camera-input-pins.json` and `keyframe-input-pins.json` preserve hashes of the exact derived references used by those calls. Original generated PNG bytes remain unchanged. Review transforms use nearest-neighbor scaling, per-frame horizontal head registration, one common vertical anchor/scale per direction and the existing0.5alpha cutoff. No limb synthesis, interpolation, final-view mirroring or duplicate-frame invention occurs in packing.

The stationary catalogue contains special anatomy and editor markers. This one appearance is not universal stationary-NPC coverage. Production integration, all other identities and running are separate work.
