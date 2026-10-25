# Stationary flat voice-age catalog

This catalog gives conservative voice-age starting points for stationary NPC flat art. It records stable asset identity (`texture-flat:<archive>:<record>`) so integration can look up the art without relying on a character name. It contains 209 entries from the previously inventoried native flat-appearance archives 175–184, 334, 346, and 357. Archive 199 is excluded because its records are quest-marker graphics.

Age classes are visual estimates, not canon ages or exact years. I reviewed the sprite images themselves; captions, roles, names, and creature size do not determine age. Gray/white hair, facial aging, and clearly youthful facial/body proportions support estimates. An obscured or nonhuman face, tiny sprite, or ambiguous mature-looking adult stays `unknown` or gets low confidence. The `visualReason` field gives the observed cue. The separate Nulfaga authored override is voice-direction metadata, not an inference from role: the native quest text names Nulfaga, and the existing reviewed-art record identifies her flat as 182/44; the sprite itself is visibly aged. Her authored elderly/erratic treatment should remain separate from the visual age estimate.

## Contact-sheet gallery

Each sheet labels every thumbnail with the archive and record. Each catalog record also links its individual native-exported PNG under `work/sleep-source/flat-<archive>-<record>.png`.

| Archive | Contact sheet |
|---|---|
| 175 | [contact-175.png](../../work/sleep-source/contact-175.png) |
| 176 | [contact-176.png](../../work/sleep-source/contact-176.png) |
| 177 | [contact-177.png](../../work/sleep-source/contact-177.png) |
| 178 | [contact-178.png](../../work/sleep-source/contact-178.png) |
| 179 | [contact-179.png](../../work/sleep-source/contact-179.png) |
| 180 | [contact-180.png](../../work/sleep-source/contact-180.png) |
| 181 | [contact-181.png](../../work/sleep-source/contact-181.png) |
| 182 | [contact-182.png](../../work/sleep-source/contact-182.png) |
| 183 | [contact-183.png](../../work/sleep-source/contact-183.png) |
| 184 | [contact-184.png](../../work/sleep-source/contact-184.png) |
| 334 | [contact-334.png](../../work/sleep-source/contact-334.png) |
| 346 | [contact-346.png](../../work/sleep-source/contact-346.png) |
| 357 | [contact-357.png](../../work/sleep-source/contact-357.png) |

## Asset-source and mod caveat

The sheets and individual PNGs are produced by the existing read-only `SleepAssetSurvey` using `GameFiles/arena2`, DFU `TextureReader`, and `FlatsFile`; this keeps IDs tied to native archive/record references. The native age catalog remains a baseline. The separate [origin and sex evidence catalog](stationary_voice_origin_sex_catalog.md) records the active package snapshot and the one direct NPC-flat image replacement found in the decoded installed mod assets (`texture-flat:182:33`), including its separately reviewed low-confidence age hint. It also explains the limits of the asset survey and how sex/origin metadata should be sourced.

## Use

Look up `assetId` and use `ageEstimate` as an initial TTS profile hint, with `confidence` and `visualReason` kept as evidence. Preserve `unknown` where uncertain; do not silently promote low-confidence judgments into authored canon. The `knownFigure` metadata is an explicitly documented exception for Nulfaga and is separate from the sprite-age estimate.

