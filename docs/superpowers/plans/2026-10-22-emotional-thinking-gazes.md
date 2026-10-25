# Emotional thinking gazes implementation plan

> For agentic workers: use the existing executing-plans workflow, with disjoint file ownership and parent visual acceptance. No commits or worktree changes in this shared checkout.

**Goal:** Every processed portrait077–090 has nine readable thinking directions for each of neutral/happy/angry/sad/afraid/guarded, each with open/half/closed phases, without switching its selected emotion.

**Architecture:** Extend the current source-preserving thinking pipeline with explicit emotion-bound reference and lid files. The gallery selects the current emotion's gaze map and never substitutes neutral thinking eyes for a missing emotion. Existing emotional PNGs and audio remain unchanged.

**Tech stack:** Windows PowerShell5/System.Drawing builders, Python/Pillow independent verification and publication, browser ES modules/Node tests, built-in ImageGen for new artwork.

**Spec:** User feedback22October, recorded in `art/portraits/mobile-mouths/USER-REVIEW.md`.

## Constraints

- User rejects procedural iris/pupil translations after the077 probe: pupils looked embedded in lids. Continue generated eye anatomy. For upward gaze allow the upper lid to rise and the visible aperture to widen naturally; do not combine a fixed source slit with forced vertical pupil offsets. The new neutral natural-lids atlas was user-approved visually, and all six077 moods are now being generated with the corrected anatomy.

- Keep the original64×64 source,64×35 eye halves, calibrated masks, palette and soft contrast.
- Keep each emotion's existing brow master, rather than replacing it with a generated neutral/thoughtful brow.
- Up/diagonal/down must be visibly distinct after native sampling; different hashes alone do not prove direction.
- Retain exact generation arguments/result/raw/reference receipts and rejected attempts. No relabeled inputs.
- Gallery cadence only:1–5 periods per eligible minute,1.8–6.5seconds; occasional neighboring compass shift and optional return. Speech/cycling does not suppress these gallery review movements.
- Same existing unversioned gallery URL/tab. No additional user-facing test pages/tabs.
- Finish bounded active neutral corrections078/079/090, then no further paid generation outside the current canonical face. Complete077's six emotional gaze sets before rolling out the method.
- All remaining original expressions/mouths stay stable while the user reviews. Mod/game/models/TTS work stays deferred. Pause at20%quota remaining; keepPC on.

## Task1: Emotion-bound source preparation and saving

Files: `tools/PreparePortraitThinkingWorkspace.ps1`, `tools/SavePortraitThinkingRawOutput.ps1`, isolated `work/Test-EmotionalThinkingInputs.ps1`.

Contract: optional fields in `thinking-layout.json`: `emotionId`, `referenceUpperFile`, `browSourceFile`, `halfSourceFile`, `closedSourceFile`. Defaults preserve current neutral behavior. Allowed emotions are exactly the six IDs above. All paths are portrait-root-relative, nonescaping,64×35 PNGs. `referenceUpperFile` defaults to `source-upper.png`; the selected emotional open source must match original pixels outside eye masks. Its exact repeated3×3 grid becomes the actual generation input. Retain the unchanged standard helper-stage receipt, and bind the actual emotional source filename/hash in the wrapper association.

- [ ] Synthetic test first: happy reference differs inside eye masks and is accepted; changed exterior, invalid path, wrong dimensions and invalid emotion are refused.
- [ ] Extend preparation without changing existing neutral defaults or overwriting outputs.
- [ ] Extend saving association to record the chosen emotional reference source. Validate grid bytes against that source, not assumed neutral.
- [ ] Run Windows PowerShell5 `work/Test-EmotionalThinkingInputs.ps1`, plus existing thinking saving tests. Save red/green evidence under work.

## Task2: Emotion-preserving assembly and independent verification

Files: `tools/BuildSplitPortraitThinking.ps1`, `work/verify_portrait_thinking_retrofit.py`, isolated `work/Test-EmotionalThinkingAssembly.ps1`.

Consume Task1 fields. Validate the3×3 reference against `referenceUpperFile`. Use `browSourceFile` as the stationary brow master if supplied; otherwise retain legacy generated-center behavior. Use explicit half/closed source files if supplied; otherwise existing neutral paths. Emit sidecar `emotionId` and all exact source hashes. The original exterior remains immutable.

Optional, only when calibrated: `halfLidOffsetsByGaze` maps known gaze IDs to integer offsets in[-3,3]. For each source lid rectangle, keep its top fixed, move the lower edge by offset and map sourceY=clamp(targetY-offset,sourceTop,sourceBottom). This copies lid/skin only, retains selected gaze below the edge and does not copy the neutral centered iris. Refuse an offset crossing the brow/mask boundary. Defaults preserve current assembly. The independent verifier reconstructs the same declared sources and checks exact pixels; visual half/open distinction remains a separate gate.

- [ ] Synthetic test first: happy master remains byte-identical across all27frames; neutral cannot leak through a half/closed source; exterior remains exact.
- [ ] Add source-aware optional fields and sidecar hashes, retaining neutral compatibility.
- [ ] Test offset clipping, source identity and refusal cases; run Windows PowerShell5 `work/Test-EmotionalThinkingAssembly.ps1` and Python retrofit tests.
- [ ] Verify new candidates into their own output directories. Never overwrite historical verification reports.

## Task3: Gallery matching and safe publication

Files: gallery `preview.js`, `preview.html`, `thinking-gaze-policy.mjs/.test.mjs`, `work/publish_portrait_thinking_retrofit.py` and its synthetic tests.

Manifest: optional `thinkingEyeSetsByEmotion` object, keys drawn from the six IDs, each value nine three-phase sets. Legacy `thinkingEyeSets` is neutral only. A missing current emotion uses its regular expression eyes and disables unavailable gaze controls; it never uses another emotion's gaze. A changed emotion resets the intermittent period while preserving valid manual intent. Mouth/audio choices remain unchanged.

- [ ] Test matched lookup, legacy-neutral-only behavior, missing/invalid emotion, and phase completeness before implementation.
- [ ] Integrate matched maps with the existing tested natural schedule. Keep the same gallery URL and no forced neutral-expression change.
- [ ] Extend publication to merge exactly one verified emotion map at a time, preserving all other maps and all nonthinking manifest fields. Keep versioned output roots/receipts and original artwork immutable.
- [ ] Run all gallery Node tests and synthetic publication tests; verify served hashes and actual behavior in the user's single gallery.

## Art and completion gates

- [ ] Parent inspect each emotion's actual reference before generation. Delegate independent emotions only within077 after input/assembly contracts pass.
- [ ] Parent compare each selected native gaze with same emotion's horizontal/center counterparts. Reject absent/reversed vertical direction, giant irises, white flashes, palette drift or missing blink intermediate.
- [ ] Verify six maps×nine directions×three phases=162 eye halves per portrait, all selected sources present and hashes bound.
- [ ] Roll out077–090, retaining current sources and user feedback. Notify user only when every processed portrait has all six correct maps and the combined natural preview works.
- [ ] Capture verified decisions in the existing DFU Obsidian note; append a verified checkpoint. Keep the full portrait goal active beyond this milestone.
