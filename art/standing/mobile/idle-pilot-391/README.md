# Archive 391 directional idle review pilot

Status: **unaccepted review pilot**, generated 18 October 2026. No runtime integration or batch production.

The original `art/standing/source/native-idle/391-0.png` is the immutable front reference: archive 391, record 5, frame 0, 71×166 pixels. View 0 is copied byte-for-byte. The native walking records 1–4 supply bearings and costume occlusion only; their 110/109-pixel height does not set the idle resolution.

## Review files

- `391-idle-review-4x-v1.png`: labeled nearest-neighbor 4× comparison, original front first.
- `391-idle-native-atlas-v1.png`: transparent native-resolution atlas, 320×332 pixels; eight 80×166 cells, ordered left to right, top to bottom.
- `391-idle-view-*.png`: individual transparent frames, all 166 pixels tall, with proportional widths. The original front remains 71×166.
- `generated-sheet-v1.png`: untouched built-in imagegen output.
- `prompt-v1.txt`: complete generation prompt and source roles.
- `manifest-v1.json`: source dimensions, crop bounds, processing and bearings.
- `prepare_review.py`: reproducible crop, nearest-neighbor scale and review assembly.

## Bearing convention

0 front; 1 front-left; 2 left profile; 3 rear-left; 4 back; 5 rear-right; 6 right profile; 7 front-right. Left/right describes the nose direction in the image, not anatomical side. The pouch belongs to the character's anatomical right, which appears image-left in the front and image-right in the back. Generated views are independently drawn; none is a blind horizontal mirror.

## Verification and remaining review

All eight frame files are RGBA with transparent and opaque pixels. The copied front is byte-identical to the original. All generated frames retain their original alpha, cropped using visible alpha bounds and reduced proportionally with nearest-neighbor sampling. The atlas adds transparent padding and does not rescale frames. No silhouette is clipped to the original front width; the back needs 77 pixels at the target height.

The sheet depicts planted standing feet and distinct bearings. The generated hands/face are heavier, and the teal leg highlights brighter, than the original. The palette is visually related but not an exact indexed-palette match. These differences require user review; this folder does not claim production acceptance. No further appearances should be generated until the pilot is accepted.
