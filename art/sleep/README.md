# Sleeping art studies

All PNGs here were generated with the built-in image tool from locally inspected original game references. They are development drafts, not accepted or bundled runtime assets. Original reference PNGs remain under ignored `work/sleep-source`; the committed CSV contains source metadata only. Curate human applicability before claiming coverage.

| Draft | Source | Dimensions | Status |
| --- | --- | --- | --- |
| `drafts/flat-182-1-blanket-side-v1.png` | Flat 182/1, original 61×117 | 2138×736 | Current camera/blanket study; native-scale and in-game review pending |
| `drafts/flat-182-1-overhead-superseded.png` | Flat 182/1 | 906×1737 | Superseded by side-view blanket decision |
| `drafts/mobile-385-overhead-superseded.png` | Mobile archive 385, idle record 5, original 72×163 | 834×1885 | Superseded by side-view blanket decision |

The overhead studies aimed to preserve clothes and body identity but used the wrong viewing approach for a standing player. They must not become final sleeping assets. The current study removes outfit-specific sleeping work by covering the body and retains face identity. It still requires lower-resolution/style validation and matching views; generation output size is not native game resolution.

## Current study prompt

Built-in tool, `transparent_background: true`, reference `work/sleep-source/flat-182-1.png`:

> Create ONE production-study sleeping NPC sprite for a Daggerfall Unity classic pixel-art mod. Reference image is the ORIGINAL NPC appearance and exact 1996 low-resolution game-art style reference, not an edit target. Preserve his dark-brown skin, stocky adult male face and short/bald-looking head silhouette. He is asleep on his back, eyes closed. His entire body FROM THE NECK DOWN, including hands, shoulders, clothes and feet, is fully covered by ONE THICK PUFFY WHITE BLANKET; only his face/head is visible at the left end resting on a small white pillow. NO visible hands, feet or clothing. Blanket lies along a horizontal bed, gentle raised body silhouette under it, not a shroud or coffin. Crucial camera: normal player standing beside the LONG SIDE of a bed, looking down by about 25 degrees, NOT overhead and NOT top-down. Head at LEFT, feet at RIGHT. Show side thickness of blanket and a small amount of its top; the head is viewed from this same side/slightly elevated perspective. Orthographic-ish game sprite with no dramatic perspective. White blanket with a few coarse warm-gray pixel shadow clusters, matching the original Daggerfall asset's coarse 8-bit indexed-palette dithering and sparse detail. Intended native footprint about 160 pixels wide by 55 pixels tall, hard single-pixel steps, no antialiasing, no smooth gradients, no modern high-definition painted texture, no high-resolution face. It must look like an original low-resolution 1996 bitmap sprite. Isolated blanket, pillow and visible head ONLY, transparent surrounding background, no bed frame, no mattress, no floor, no room, no text, no comparison panels, no border or cast shadow outside the sprite. This is a first angle study; do not include multiple views.

Generated source: `exec-5197a897-1f38-4f19-8dda-89cd3a3bfe73.png` in this task's Codex generated-images directory, 14 October 2026. The project copy preserves original bytes and alpha. No bitmap resampling or manual image repair was performed.
