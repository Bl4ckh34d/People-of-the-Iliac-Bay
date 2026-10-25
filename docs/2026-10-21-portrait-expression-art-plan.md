# Portrait expression art plan

Status: Gwen-only expression prototype is wired behind `-peopleoftheiliacbay-portrait-visemes`. Offline policy suite passes; parent owns build and live visual verification. Atlas art quality remains pending user feedback.

The separate `-peopleoftheiliacbay-gwen-expression-preview` flag arms a visual-only 45-second carousel. It waits for matching live Gwen audio, labels each phase, changes only portrait expression, and restores neutral baseline at completion. It does not inject expression into TTS context or alter audio/dialogue.

## Design split

Treat portrait appearance as three independent layers:

1. **Native identity:** DFU's current 64×64 `texturePortrait`. It owns face, hair, clothing, palette, and background. Never replace it with a generated full-face image.
2. **Speech mouth:** Rhubarb A–H/X shapes, or clearly labeled RMS open/closed fallback. Prototype crop uses `(x=18,y=35,w=28,h=20)` after user reported lower-lip clipping. `X` selects matching closed-mouth expression.
3. **Expression and blink:** eye/brow crop `(x=10,y=17,w=45,h=17)`, selected from typed actor mood/state. Irregular UI-time blink runs during speech. Pending gaze uses pupil crop `(x=16,y=22,w=32,h=7)` only when exact ephemeral pending row and busy state both exist.

Compose mouth and expression masks from the original native portrait each update. Do not compose over the prior frame; this prevents palette drift and accumulated edge damage. When speech ends, use a closed-mouth variant for the selected expression, then restore exact native face when conversation closes or actor changes.

## Generated review sheets

Three full-face atlas references live under `art/portraits/prototype-gwen/`; manifest is `tools/BreezeTtsTrial/gwen-expression-atlases-v1.json`. They target only Gwen fixture and captured face record 76. Runtime crops only selected feature rectangles and discards every other generated pixel.

- Thinking gaze, 3×3 row-major: center, up, up-right / left, down-left, down, down-right / up-left, right. User-facing sequence can choose neutral center plus eight directions after crop review.
- Emotion eyes/blinks, 4×4 row-major: neutral, happy, sad, angry / fear, skeptical, disgusted, warm / neutral blink, happy blink, sad blink, angry blink / fear blink, skeptical blink, disgust blink, warm blink.
- Closed-mouth baseline, 3×3 row-major: neutral, happy, sad / angry pressed, angry clenched teeth, disgust / fear parted, tired relaxed, smirk.

The 4×4 grid divides 1254 pixels into fractional 313.5-pixel cells. Runtime uses normalized UV sampling. `art/portraits/prototype-gwen/composite-preview.html` starts from captured native 64×64 face and applies only feature masks. No emotion, gaze, blink, or mouth sheet overwrites identity source.

Keep emotion modest at 64×64. Preserve eye color, face silhouette, hairline, lighting, and source palette outside exact masks. Blink uses short closed-lid pose and continues during speech; it never changes mouth cues or replaces whole portrait.

Add a separate neutral **thinking gaze** set for long response waits: center, slight up-left, up-right, down-left, and down-right. Move pupils only; do not add a frown, smile, or concern brow. Hold each gaze briefly, return to center, and use small irregular pauses. Keep this a neutral idle behavior, separate from mood and speech. Do not make every mood × gaze × blink combination into a hand-authored tile; compose independent pupil, eyelid, expression, and mouth masks from the native face.

The art manifest records source person ID, captured face index and source/output hashes, native dimensions, atlas dimensions, crop rectangles, tile order, and review status. Human review of in-game composite remains pending. Generated pixels outside masks are discarded at runtime.

## Runtime contract

- Keep race/timbre, stable voice profile/reference/seed, accent origin, and emotional delivery as separate inputs.
- Map only trusted structured state to expression: mood, fear, resentment, and any explicitly supported disposition. Fear takes priority; anger requires both strongly negative mood and resentment ≥50. Negative mood alone stays sad. Missing or unknown state means neutral; never infer emotion from dialogue words.
- Combine expression layer with speech-mouth layer. Emotion changes eyes/brows and the closed-mouth baseline; Rhubarb controls the mouth while speaking.
- Keep expression and blink optional, deterministic-testable, and fail-closed to the native face. Do not mutate `DaggerfallTalkWindow.texturePortrait`.
- Record whether rendered state came from validated Rhubarb cues, amplitude approximation, or idle expression. Do not label RMS as lip sync.

## Review gates

1. Capture current live Gwen face and mouth prototype during actual speech. Verify panel restores exact native pixels after speech and after speaker/window changes.
2. Review speech mouth with eye layer. Fix crop, scale, color, edge artifacts, and movement first.
3. Review expression states, gaze, and blink in static and in-game composites. Reject any mask that changes pixels outside its named rectangle.
4. Keep other NPCs disabled until per-face art workflow and identity evidence exist.
5. Confirm thinking gaze stays subtle and does not imply a dialogue outcome.

## Verified UI facts and known risk

DFU `DaggerfallTalkWindow` owns protected `texturePortrait` and `panelPortrait`; `panelPortrait` is a 64×64 panel whose `BackgroundTexture` is assigned from `texturePortrait` during setup and portrait updates. The prototype reflects both fields, keeps `texturePortrait` as source, and displays the composed texture by setting only `panelPortrait.BackgroundTexture`.

Review risk before expanding beyond Gwen: restoration now checks current conversation owner, person ID, and face index. It restores current/cached native texture only for the same binding; it clears the prototype composite when actor/face/session no longer matches. Offline policy tests cover changed actor, changed face, closed session, and same-binding restoration. Parent must still verify live speaker-switch ordering.

References: `mod/Scripts/GwenPortraitVisemeOverlay.cs`, `tools/BreezeTtsTrial/gwen-viseme-atlas-v1.json`, and DFU `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallTalkWindow.cs` fields `texturePortrait`, `panelPortrait`, and portrait assignment methods.
