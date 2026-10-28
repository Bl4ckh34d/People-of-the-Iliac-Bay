# Sitting sprite work

All 24 vanilla human mobile appearances (381–398 and 451–456) now have eight single sitting views: **192 static frames**, 24 transparent atlases. `validate_sitting.py` passes exact native canvas dimensions, eight orientation indices, binary alpha, and zero RGB in transparent pixels. Each appearance was visually inspected at native scale; the pilot and corrected 396 were also inspected at 3× nearest-neighbour scale. `all-appearances-source-comparison.png` compares native idle and seated front, while `all-eight-views-preview.png` shows all exported angles. The Daggerfall City subset is in `daggerfall-city-preview.png`.

The user explicitly approved both sitting pilots. This overrides the art worker's earlier visual rejection; the earlier concerns below are historical evidence, not an active acceptance gate. Production exports use the approved style with native idle references for each appearance. Cancelled keeper and idle-angle work was not resumed.

The native idle image defines appearance, proportions, palette and pixel density. Native walking records define eight directions and rear costume details only. Archive 385 idle is 72×163; walking frames have a different pixel scale and must not replace the idle size authority.

`manifest.json` defines the seat-forward contract agreed with the seat activity worker. Each appearance needs eight single poses, not an animation loop. Characters must contain no furniture. Pelvis rests on the seat, feet point toward seat front, and back points toward backrest. Native source mappings were checked against `MobilePersonBillboard.cs`.

## Pilot evidence

Original generated outputs are preserved without alpha replacement in `pilot-385/drafts`. The quarter-scale nearest-neighbour preview is diagnostic, not a native-size approved export: generated sheet cells are 384×512, so quarter-scale cells are 96×128. The native idle reference remains unmodified in `pilot-385/source`.

Both original drafts are RGBA 1536×1024 with alpha extrema 0–254. V2 has 1,008,600 alpha-zero pixels. The apparent dark/glowing backdrop in the image viewer partly consists of hidden RGB behind alpha zero; do not describe the PNG as opaque. Earlier worker concerns were changed proportions, modern black outlines/detail treatment, and missing belt purse in v2. The user reviewed both and accepted their appearance. No disconnected-limb compositing was used.

## Production exports

Each archive directory contains its unmodified native idle reference, original generated sheet, exact generation prompt, eight separate static transparent PNGs, a 4×2 atlas, directional manifest and native/3× nearest-neighbour previews. `export_sitting.py` reproduces export from the saved generated sheet. Export uses one uniform scale across all views of each appearance, keeps the exact native idle canvas dimensions, thresholds alpha at 128, and clears transparent RGB. It does not repaint or recombine body parts. The ground pivot is bottom-centre. Seat pivots are estimates and require runtime alignment verification; no game launch or integration was performed by this worker.

Archive 396 received a user-requested costume correction. The first generated version exposed hips/buttocks unlike the native walking side/rear frames. Its orange skirt now continuously covers hips and buttocks. The erroneous version remains historical evidence under `396/drafts`; only the corrected `396/generated.png` supplies the current atlas. The precise correction prompt is preserved separately.

Production styles preserve the user-approved pilot treatment. Small details and purse placement vary between generated bearings; these are not guarantees of pixel-for-pixel native costume reconstruction. Original idle and walking side/rear references are preserved per appearance for further review.

Generation used the built-in imagegen tool with native source references only. No rejected generated sheet was supplied as a reference. No gameplay code, build, deployment or game launch was performed by this art worker.

Original outputs: `C:/Users/Daniel/.codex/generated_images/01a14bad-55c7-7671-a3ab-5561fccfc6ac/exec-8831a144-9bdc-4cf3-a9d9-056e4f0a573c.png` and `exec-feb08c04-f915-47eb-a93d-ffaf06a1e054.png`.
