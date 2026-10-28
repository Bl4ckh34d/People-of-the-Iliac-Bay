# Portrait77: original-preserving split-half prototype

This implements the user's requested separation of upper eye/brow/blink states from lower speech-mouth states. It is a browser prototype, not installed mod content. Earlier freely regenerated full-face emotion sheets and the single happy full portrait were rejected because their geometry, palette, and texture drifted.

## Original and exact boundaries

`original.png` is the native64×64 TFAC00I0.RCI record77. The split is at native y35: upper64×35, lower64×29. All samples keep these complete-half coordinates. Mouths are never stretched independently of their source half.

Final changes are limited to eye/brow rectangles `(15,15,16,17)` and `(34,15,17,17)`, and mouth rectangle `(22,39,20,13)` in full-face coordinates. The eyebrow envelope was extended two rows upward after the user reported cropping. One-pixel mask-edge blending stays inside these bounds. The inner feature area is opaque, replacing the original eyes/lips rather than leaving them visible behind a transparent overlay. Hair, upper forehead, background, head contour, neck, collarbone and other pixels outside these regions remain the original. This invariant does not prove that the expression looks good.

## Parts and provenance

Six eye expressions have three lid phases each. Six mouth expressions have nine A–H/X shapes each. The72 small opaque half-images can form972 independent eye/mouth combinations without regenerating complete faces. Neutral/open and neutral A/X preserve original pixels exactly. Emotional closed-mouth poses can differ from the original as requested.

Raw upper sheets use a2×2 layout: open, half, closed, repeated open. Raw lower sheets use3×3 A B C / D E F / G H X. Actual generated dimensions vary slightly and are recorded in each provenance JSON. Builders sample whole cells into native dimensions before masking. Original raw sheets remain untouched.

Sad and skeptical eye sheets have targeted corrected versions. Skeptical currently selects `raw-v5.png`, which restores the generated outer left eyebrow that was too faint. Sad mouths select `mouth/sad/raw-v2.png`. Only the corrected closed cell is selected from the happy correction sheet; only cell1 from sad `half-correction-v3.png` supplies its half blink. Sad v4 remains a rejected study: its iris still shrinks. Brow rows15–22 use one open master across lid phases. `layout.eyeFeatureLocks` additionally preserves the sad exposed eye row28 (including the three-pixel irises) and upper crease rows23–26 across relevant blink phases, preventing white flashes and crease brightness flicker. These locks are native coordinates specific to this portrait. Neutral uses the genuine original brow. The first historical brow audit accidentally measured forehead rows13–16; it is explicitly superseded in `work/happy-brow-review.json`.

Builders: `tools/BuildSplitEmotionMouths.ps1`, then `tools/BuildSplitPortraitPrototype.ps1`. `manifest.json` contains browser-relative selected assets. `mouth/emotion-sets.json` records its generated parts and pixel verification. The older81-combination receipt predates this expansion and must not be cited for the complete72-part version.

## Browser behavior

The compact gallery shows only the original and composed full portraits. Upper/lower comparison views and magenta difference canvases were removed at the user's request. Difference PNGs remain available for offline verification. Blink, double-blink, eye-hold, mouth-cycle and rest buttons use emoji with descriptive tooltips and accessible names.

Eyes and mouths can be selected separately. Six same-text emotional WAVs and their matching Rhubarb timelines drive the mouth from actual audio time. Eye-expression buttons also switch the matching voice clip, preserving playback intent and looping; the independent mouth-expression override changes only the mouth. The current long paragraph runs23.92–30.24seconds per emotion; all six contain all nine A–H/X cue types. All use the same fixed voice reference, seed and identity instruction. Emotional delivery uses Breeze prose instructions, including guarded, rather than a guaranteed typed emotion control. Looping replays existing audio and performs no new inference. Audible emotion quality and same-speaker similarity remain listener judgments. Short clips and the original reference remain available in the source assets.

Ordinary blinks sometimes form an overlapping pair: the second starts55–70% through the first. A short1–1.8-second follow-up is more likely after a double blink. Longer eye holds occur at random start intervals15–60seconds, independently of audio loops. Half-open holds last1.5–8seconds during speech or idle; closed holds last1.5–3seconds only while speaking and release through a half frame if speech stops. Closed holds and blink motion use existing half frames on both sides, with no image crossfading. All timings here are UI-animation seconds, not NPC schedule durations.

`portrait-catalog.json` includes503 common and61 special original portraits. Records77,78 and79 have complete animated sets. Other entries show their own original with unrelated animation/audio disabled. The catalog's explicit `voiceSet` routes animated entries to one shared female or male six-mood package in `../shared-voices/`; the gallery intentionally reuses these twelve clips across portraits. Individual in-game NPC voices are unchanged. Mod-specific custom portraits remain outside this native-bank catalog until their sources are explicitly registered.

The user approved record77's split-mask prototype and final local eyelid repair. Record78's revised compositions pass technical checks and parent visual review; the user's next review remains pending. No new portrait registry or assets have been installed into DFU. Special treatments and exclusions are recorded in `../animation-policy.json` and `../ROLLOUT.md`.
