# Portrait 77: complete emotion, speech, and blink prototype

This is the first complete emotion prototype for one mobile NPC portrait. It is a browser review artifact, not installed game content. The user authorized rollout to all NPCs, then requested a complete redo of this first set after identifying blurry angry eyes and insufficient distinction between sad and afraid. The initial root-level emotion sheets are superseded review history, not the rollout template.

## Revision 2

The user has now rejected revision2's visual style: excessive contrast and harsh edges, unstable/smeared eyes, weak guarded/thinking brows, and too little distinction between neutral and happy. Its189-frame browser and hash checks pass technically; this is not art acceptance. Do not use it as the all-NPC production template. The next controlled test is one complete happy portrait directly from the genuine native original, under `../original-style-probe/`. The earlier failed feature-cutout methods are not an approved solution merely because they preserve outside pixels.

`v2/` contains fresh complete sheets for all six emotions and three closed-mouth thinking-gaze sheets. The thinking order is screen-relative: upper-left, up, upper-right / left, center, right / lower-left, down, lower-right. Each set has three eye phases. Total:21 sheets and189 portrait frames. The live manifest selects only the current revision. Prompts and hashes are recorded per set; discarded attempts stay outside the selected manifest.

The gallery includes a six-face comparison, individually selectable mouth/eye/gaze frames, irregular thinking-gaze changes, and independent blinks. Gaze direction needs inspection: the first thinking draft duplicated the upper diagonals and was sent back for correction. Do not infer correct gaze from the requested prompt alone. Final visual acceptance and native installation remain separate gates.

## Assets

Six expressions: neutral, happy/warm, angry, sad, afraid/worried, and guarded/suspicious. Each has three complete 3×3 portrait atlases: eyes open, half closed, and fully closed. The nine row-major mouth poses are `A B C / D E F / G H X`, including relaxed closed `X`. This makes 18 sheets and 162 complete portrait frames.

Every sheet is 1254×1254, with 418×418 cells. The preview samples complete cells into a 64×64 canvas. It does not paste separate mouth or eye patches over the original. Neutral/open is an exact copy of the approved version-5 sheet. Other sheets were edited with the built-in image tool; prompts, generated-source paths, hashes, and unused attempts are recorded in the six `*-provenance.json` files.

`manifest.json` identifies the selected files and their hashes. Each revision's `asset-verification.json` records dimensions and hashes. `review-contact-sheet.png` is a review montage only, never a runtime atlas. Retained rejected PNGs are excluded from the manifest.

## Review

Open `gallery.html` through the existing localhost server. Choose an expression, inspect any mouth and eye phase, or play the reference clip. Automatic blinking runs independently of speech and preserves the active audio-clock mouth cue. The sequential tour plays the same clip for each expression and waits for its end before starting the next.

The clip and Rhubarb cue file come from the parent directory. This preview generates no new speech. The neutral reference voice does not change emotion when the portrait expression changes.

The sheets are visually coherent enough for review, but generation is not pixel-identical between phases. Happy speech has broader teeth and smiles than neutral; guarded is stern and skeptical. Final visual acceptance belongs to the user. No further NPC portraits or native runtime bindings are included here.
