# Jester source family 182/5

The native originals 182/5, 334/6 and 346/3 are byte-identical PNGs and pixel-identical at 86 × 131. `source-family.json` retains their three provider identities and source hashes separately. One authored appearance can therefore be reviewed for all three identities; this does not mean three independent appearances were generated.

The jester has a right-hand marotte. Its anatomical RIGHT torso, sleeve and cap tip are gold; the anatomical LEFT counterparts are red-orange. The leggings are counterchanged: RIGHT leg red-orange, LEFT leg gold. The marotte has a miniature matching jester head and a striped olive shaft. These asymmetric features require explicit review. Whole-sprite mirroring is not used.

## Selected review candidates

`selection.json` is the explicit selected-source list. `manifest.json` reports current candidate views and missing views separately for walking and running. Production acceptance remains zero.

All eight walking directions now have four selected motion-review frames. Every native-sized direction and the combined sheet were inspected locally. The parent separately reviewed the first left-profile cycle. Supporting and passing legs visibly alternate gold/red. Targeted arm and leg repairs correct the opposite contacts without using mirrored bodies. Original palette, face and proportions are not declared exact. Side bodies can expose more chest than exact profiles, and head/cap and marotte dimensions vary across directions.

The left running candidate has alternating gold/red contact legs and actual flight poses. A targeted phase 1 arm repair restores the counter-swing. That repair also reduces the marotte head size, which remains a visible limitation. Native flight clearance is 12 and 10 pixels; contact phases meet the common baseline.

The first right walking arm repair reduced head and prop proportions. A subsequent edit inside the full strip restored its selected body heights to 127–130 pixels. Rear-left and front-right initially had incorrect marotte ownership; corrected single keyframes established the correct anatomy before cycle generation. Rear-left then needed a forward passing-leg repair. Front-right and rear-right required contact-leg corrections. These successful selected sources are pinned in the manifest. `rejected-quarter-strips.json` retains the earlier failure evidence; those unselected raw strips are not counted again.

All eight running directions now have four selected review frames. Every direction and the combined sheet were inspected locally. The first back-running strip mixed shoe colors and repeated a contact side; leg ownership and the counter-arm were corrected. The first right-running strip also mixed leg colors. Conditioning on the reviewed jester walk from that same bearing, together with the reviewed 385 run template, preserved costume anatomy more reliably. Rear-left initially remained walking-like and required explicitly authored raised trailing heels and flight knees. A final bounded edit now tucks the free red arm back in phases 0/3 and leaves it forward in phases 1/2; the native preview was inspected. Its flight clearance is larger than other directions because both knees fold more strongly; no code-generated upward flight shift is used.

Selected standalone frames are available in `walk/frames/` and `run/frames/`. The manifest pins each PNG hash and its raw generation source. These are review assets, not installed runtime replacements.

## Reproduction and limits

Run `python pack_family.py`, then `python verify_family.py` from this directory. The packer creates review PNGs and four-frame GIFs at 131-pixel logical height, 160-pixel cells, baseline 148 and alpha cutoff 128. It uses a common scale per direction, head registration and rigid contact translation. Raw generated sources remain unchanged. No limb edits, mirroring, interpolation or duplicate-frame synthesis occurs in these scripts. Four distinct images are a layout check, not gait acceptance.

Generation JSON files retain exact prompts, generated source locations and reference hashes. Walking references use reviewed 182/0 motion from the same bearing. Running references use reviewed generated 385 frames from the same bearing; they are not native running originals. Later run generations use the matching reviewed jester walk as the immediate appearance reference, retaining its chain back to original 182/5. Derived crop JSON files record exact source rectangles and hashes. `verification.json` checks all original hashes, selected source hashes, derived crop pixel parity, alpha, frame bounds, distinct pixels and four-frame GIF packaging.

Builtin `image_gen` performs all creative edits. Local Python only assembles references and review outputs and verifies hashes and layout. Shared manifests, queues, gameplay code, Unity, builds and runtime assets remain unchanged.
