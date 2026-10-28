# Archive 385: complete eight-bearing running candidate

The current deliverable is `eight-view-candidate-v1/manifest-reviewed.json`. It contains **32 distinct generated pose frames: eight canonical bearings × four chronological phases**. This completes one appearance's review candidate. It does not imply user approval, native game playback acceptance, runtime integration, or completion of the remaining families.

## Review files

- `eight-view-candidate-v1/animated-preview.gif`: all eight bearings together, eight fps.
- `eight-view-candidate-v1/review-0.png`: front, front-left, left profile and rear-left.
- `eight-view-candidate-v1/review-1.png`: back, rear-right, right profile and front-right.
- `eight-view-candidate-v1/full-sheet.png`: all 32 frames.
- `eight-view-candidate-v1/atlas-8x4.png` and `frames/`: RGBA packing candidates.
- `coverage-eight-view-supplement.json`: additive status and remaining scope.

Every packed frame was inspected through the local review images. All eight views are visibly distinct. Arm arrangements change between half-cycles. Front, back, front-right and rear-right needed individual source-conditioned corrections after whole strips repeated one arm lead. The new right-side views were independently generated, preserving the pouch as an asymmetric feature. **No generated output is mirrored.** The native reference mapping still correctly records DFU's own mirrored records for bearings 5–7.

The sequence is left-foot contact, right-knee flight, right-foot contact and left-knee flight. Phase labels describe the intended anatomical sequence. The long tunic obscures some near/far thigh crossings, so the labels are not proof of perfect three-dimensional limb ownership. Color saturation, face detail, folds and body width vary slightly between calls. Four phases omit intermediate support/passing poses. These remain visible review limitations rather than hidden acceptance claims.

## Pixel packing and world scale are separate

Fresh reads of installed `TEXTURE.385` establish:

| Native record | Pixel dimensions | Record scale | Adjusted dimensions before GlobalScale |
|---|---|---|---|
| 0 | 52 × 110 | −70, −70 | 38 × 80 |
| 1 | 65 × 110 | −70, −70 | 48 × 80 |
| 2 | 71 × 110 | −70, −70 | 52 × 80 |
| 3 | 64 × 110 | −70, −70 | 47 × 80 |
| 4 | 52 × 109 | −70, −70 | 38 × 80 |
| 5 idle | 72 × 163 | −134, −134 | 35 × 78 |

`MobilePersonBillboard.cs:321–346` applies `size + int(size * recordScale / 256)`, with C# truncation toward zero. `MeshReader.GlobalScale` is 0.025. Native walking height is therefore **2.00 Unity units**, while idle is **1.95**, before any optional `TextureReplacement` override. Those optional override effects were not verified here.

The proposed export uses a **160 × 176 pixel canvas**, a mean contact-body height of **110 pixels**, and foot pivot **(80,156)** in top-left pixel coordinates. To target native walking height, use **2/110 Unity units per content pixel**. That implies a padded quad about **2.9091 × 3.2 Unity units**, with its center **1.23636 units above the foot pivot**. The bottom-left UV pivot is `(0.5,20/176)`. The larger transparent quad does not make the figure 3.2 units tall: its average contact silhouette remains 2.00 units.

These values are a proposed renderer contract, not a tested implementation. No runtime code, game state, build or installed bundle was changed.

## Verification and provenance

Fresh checks establish 19 byte-identical raw generation outputs used in the final provenance graph, six exact mechanical reference crops, 32 unique RGBA frame hashes, exact frame/atlas-cell equality, four GIF phases, no cutout clipping at alpha 128, and contact/flight baseline checks. All flight frames clear the baseline by at least three pixels. Two frames needed an extra 2–3 pixel upward packing adjustment; their poses remain separately generated images, not translated stand-ins for missing phases.

Actual alpha gutters determine sheet crops. Assuming equal cells would cut the third right-profile pose's foot. Packing preserves alpha, uses a common scale per bearing after source-resolution normalization, and never redraws, stretches individual body parts, remaps palettes or mirrors figures. Source crops, hashes, prompt paths, input references, raw tool outputs and placement values are stored in the reviewed manifest.

Repeat from LivingCities:

```powershell
python art/production-20261028/mobile-locomotion/pack_eight_views.py
python art/production-20261028/mobile-locomotion/review_eight_views.py
```

Status remains explicitly separated: `generated=true`, `technical_checked=true`, `visual_reviewed=true`, `integrated=false`, `user_approved=false`. The remaining 23 ordinary civilian appearances, guard 399 and DET Orc source families remain outside this completed 385 candidate.
