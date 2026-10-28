# Appearance 385 running pilot — review candidate

This is the complete current attempt, delivered for user critique. It is not approved and is not integrated into the game. Generation is paused. No other appearance has been started.

- `full-sheet.png`: all eight bearings, four chronological frames per bearing.
- `full-sheet-2x.png`: nearest-neighbor enlarged review sheet.
- `animated-preview.gif`: all eight bearings simultaneously, 125 ms per frame (8 fps).
- `atlas-8x4.png`: transparent atlas, bearing rows and frame columns.
- `frames/`: 32 transparent RGBA frames, each 192 × 224 pixels.
- `manifest.json`: source crops, placement, scale, timing, bearing and intended phase metadata.
- `verification.json`: mechanical validation. All 32 frames match their atlas cells and contain real transparency.

## Phase and remaining defects

The intended arm order is A–B–A–B: A means right arm forward and left leg forward; B means left arm forward and right leg forward. Front and rear views account for viewpoint reversal. The visible arm silhouettes now alternate in all eight strips.

The gait quality gate is not fully passed. Profile and rear-quarter leg depth/overlap remains weak, particularly views 2, 5 and 6. Passing/recovery transitions remain weak across the four-pose cycle. Flight clearance is inconsistent across bearings; view 5 frame 3 in particular reads as contact rather than clear flight. These issues are disclosed rather than hidden by another generation loop. Full animated playback acceptance is not claimed.

Eight bearings: 0 front, 1 front-left, 2 left profile, 3 rear-left, 4 back, 5 rear-right, 6 right profile, 7 front-right. Left-facing bearings have the nose toward image-left; right-facing bearings have it toward image-right.

The original walking animation has four frames per bearing. The new candidate retains that count. The 163-pixel native idle height supplies the scale reference; transparent padding accommodates the wider running silhouette. Per-bearing uniform nearest-neighbor reduction preserves each strip's internal size differences. Contact frames are aligned to a common baseline; flight frames are not foot-normalized.

Generated with the built-in imagegen tool. Versioned prompts and original generated strips are retained in the parent directory. Existing idle, sitting, sleeping and prior running pilot assets were not overwritten.
