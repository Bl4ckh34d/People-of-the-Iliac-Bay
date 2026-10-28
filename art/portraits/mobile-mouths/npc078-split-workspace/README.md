# Common portrait 078

Immutable source: TFAC00I0.RCI record78, original PNG SHA-256
`4cfab5dce8d40ff0246728c2c28922d9b5627cec9549f9b5252f2f601e4978ce`.
Native64×64; split at y35. See `layout.json` for this face's masks and selections.

Current revision5 selects happy/angry/afraid eye `raw-v2.png` and new neutral/angry
mouth `raw.png` sources with explicit Rhubarb pose meanings. Four other mouth sets
remain unchanged. Previous default sources are retained as `*-pre-pose.*`; the
older angry raw-v2/v3 and their receipts remain historical evidence.
Fearful `raw-v3.png` is a failed study and is not selected: its generated round iris
still crossed the lower eyelid despite a targeted correction prompt.

Instead, `eyeFeatureLocks.sourceFile` preserves the exact original open eyeballs
and the matching neutral half/closed lid geometry under the generated worried brows.
Happy half/closed eyes reuse the stable neutral lid phases to prevent upward jumps.
Angry brow tips below y22 use the open master during blinking. These coordinates
belong to record78; do not copy them to a different face without inspection.

`work/portrait078-revision2-independent-verification.json/.md` reopens18 eye and54
mouth PNGs, verifies972 combinations, all14 source locks and unchanged original,
neutral frames and unrelated mouths. Parent inspected all54 mouth compositions and
all18 eye phases, plus real gallery playback and emotion switching. Parent accepts
this as the next review candidate; the user's next visual review is still pending.

Gallery audio comes from the shared female six-mood manifest, selected by catalog
`voiceSet`, not from this portrait's legacy sample fields. No new clips are synthesized
for this face. No native game installation is claimed.

## Palette follow-up

The user rejected v2 angry lips as too red. V3 uses a more muted lip palette but
introduced an orange skin fringe. An optional per-face `mouthSkinPalette` now uses
four inspected original skin anchors to reduce that cast only within the configured
skin-hue range; it is not a universal recolor and must not be copied blindly to
other faces. The raw generation stays untouched. A four-point regression improves
mean channel error from11.17 to below6. Independent46-point comparison also improves
A/X/E, but still finds a warm/dark residual fringe: this is a partial palette fix,
not exact palette matching. Keep this limitation for the user's next review.

Those palette measurements describe revision4. Current revision5 evidence is
`work/portrait078-revision5-verification.json/.md`:72 components,972 combinations,
14 locks, unchanged18 eye components and36 unrelated mouth components. Neutral A/X
remain original-exact. A complete210-file revision4 baseline is retained under
`work/portrait078-revision4-baseline` with its pre-adoption inventory.

Parent compared every new neutral/angry pose against the original, revision4 and
an isolated global-palette variant. The new shapes materially improve wide D,
puckered F and tooth/lip G. E/H remain approximate at native scale. The existing
angry skin anchors are retained; their historical rationale mentions v3, but they
now process the selected new source. No global palette flag is enabled.

The new closed angry forms show less skin fringe and muted rust-brown lips.
The unchanged four-point regression passes (mean absolute RGB error0); a wider
46-point fixed ring improves A/X from8.05/8.17 to5.25/4.20. These masks are not skin
segmentation and cannot prove exact color placement, especially for open mouths.
A narrow dark/warm lip edge and bright tooth highlights remain review points.
Global palette mapping was not adopted because it flattened oral details.
See `work/portrait078-pose-palette-review.json/.md` and parent-review JSON.

Only the two new mouth sources have the current standard-helper provenance audit,
`work/portrait078-pose-provenance-review.json`; no stronger claim about legacy
receipts is implied. Canonical rebuilding matches all72 reviewed candidate parts.
This remains a gallery candidate pending user review, not a new native installation.
