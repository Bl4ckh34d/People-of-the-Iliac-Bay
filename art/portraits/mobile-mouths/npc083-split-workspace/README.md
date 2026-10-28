# Common portrait083 gallery candidate

Source: `TFAC00I0.RCI` record83, SHA256
`e46c900779f687610af7b821b988300b998aea48632c829537f88cb829b5acfb`.
Parent-reviewed, pending user review; not installed in the game.

Six expressions contain18 eye halves and54 speech-mouth halves. Native source
remains immutable; neutral open and neutral A/X are source-exact. The gallery
reuses the same six female clips as other female portraits.

This face has higher eyes than previous records. `browLockEndY:17` keeps brow
rows12..17 stable while allowing the lids below to close. The prior fixed22
boundary incorrectly retained open iris pixels. The builder now supports a
validated per-face boundary; default22 behavior is preserved for old layouts.
`tools/TestPortraitBrowBoundary.ps1` has parent-confirmed RED/GREEN evidence.

The first afraid eye atlas enlarged and raised the irises. Its raw/provenance/
request are preserved as `*-v1-rejected.*`. One targeted regeneration from the
original reference grid is selected at the ordinary current raw/provenance paths.
That eye-correction step did not rerender mouths. Original-palette quantization is disabled; parent
compared skin/lips/eyes and oral detail directly with the original instead.

Earlier independent evidence: `work/portrait083-independent-verification.json` and
`work/portrait083-provenance-review.json`. Parent visual review, hashes and remaining
art limitations are in `work/portrait083-parent-review.json`. Heavy angry brows,
some broad bright teeth and restrained eye expressions remain review points.
Thinking gaze directions, user acceptance and native integration remain open.

## Neutral/angry pose correction

The neutral and angry mouth sources now explicitly spell out all Rhubarb pose
definitions. Earlier defaults remain as `*-pre-pose.*`; the complete213-file prior
root is preserved in `work/portrait083-pre-pose-baseline`. The exact layout,18 eyes,
36 other mouths, brow boundary17 and failed afraid eye-v1 archive are unchanged.

Fresh `work/portrait083-pose-verification.json/.md` checks72 components and972
combinations, zero outside-mask edits, opaque features and source-exact neutral
open/A/X. All72 canonical components equal the reviewed candidate. The two new
generation sources pass exact prompt/hint/path/hash provenance; this does not
upgrade the historical source receipts. All74 source/served/HTTP hashes and the
catalog hash match. Browser loads corrected83 with shared female clips, audio paused.

Parent and independent review find more plausible D/F/G families with muted brown
source-like lips and warm skin. F now puckers instead of showing tooth contact.
Small warm tooth marks remain restrained; E/H and tongue detail are approximate.
Heavy angry brows are unchanged. No global palette mapping is introduced.
See `work/portrait083-pose-visual-review.json/.md`, parent-review JSON and
`work/portrait083-pose-gallery.png`. No new TTS, audible/user acceptance or native
installation is claimed.
