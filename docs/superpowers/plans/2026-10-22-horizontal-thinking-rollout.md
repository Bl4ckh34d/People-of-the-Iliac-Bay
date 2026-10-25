# Horizontal thinking portraits: current user priority

This plan supersedes the nine-direction production milestone in
`2026-10-22-emotional-thinking-gazes.md`. Keep old assets and receipts as history.

## Ordered deliverables

1. Complete left/right thinking eyes for all six moods on already animated077–090.
2. Create complete072–076: six readable moods, all regular blink phases, A–H/X
   mouth poses, and left/right thinking with blink phases for each mood.
3. Create complete091–095 with the same coverage.
4. Apply the detailed base-expression/mouth corrections preserved in
   `art/portraits/mobile-mouths/USER-REVIEW.md` afterward.

The review gallery remains the same unversioned URL and tab. Do not continue
up/down/diagonal generation. Never substitute neutral gazes into another mood.
One canonical portrait at a time; independent mood generation may run in parallel.
Use Luna/high or lower only. At20% remaining quota finish bounded active work,
verify/save, and pause. Keep the PC on.

## Small atlas contract

New configuration: `gazeLayout: "horizontal-pair"`, with an explicit emotionId
and the existing four canonical same-emotion source paths. Legacy configurations
remain readable and verifiable without rewriting their historical receipts.

The actual2×2 reference repeats the selected emotion's open source across its top
row and its half source across the bottom row. Each cell remains64×35, enlarged8×;
reference canvas1024×560. Generated atlas has the same cell aspect and order:

| Cell | Content |
| --- | --- |
|0|Left, open eyes|
|1|Right, open eyes|
|2|Left, half blink|
|3|Right, half blink|

Closed eyes reuse the same emotion's closed source. All phases retain one shared
emotional open-brow master and exact original exterior. The generated half cells
provide lid coverage; legacy half-lid rectangle overlays and gaze offsets do not
apply to this new mode. No procedural iris movement or new pupil drawing.

Each emotion yields2 directions×3 phases=6 eye halves; each portrait36 thinking
halves. Existing regular emotion/blink and mouth sets remain separate. Existing
nine-direction maps may be filtered to left/right for preview without deleting
their source art. New published pair maps contain only the two directions.

## Gallery behavior

Only Mit Blinzeln/Aus/Wechselnd/Links/Rechts controls. Random periods remain1–5 per
minute,1.8–6.5seconds during the gallery review cadence, including speech loops.
Hold one direction throughout a period; do not jump directly left-to-right inside
it. Ordinary blinks/holds, mood-specific voices and mouth animation stay intact.

The optional skin-edge blend remains face-calibrated, inside mask boundaries,
without iris/lid blur.077's two-row y30–31 profile has actual generated-frame
evidence; do not copy that profile blindly to other faces.

## Verification and source preparation

- Input helper verifies top-open/bottom-half rows, exact paths/hashes and exterior.
- Builder/verifier independently reconstruct2×2 raw cells, fixed brows/exterior and
  source-derived closed phase. Expected report:2 gazes/6 phases/13440 pixel checks.
- Publisher accepts pair maps while retaining legacy nine-direction support.
- Gallery accepts legacy maps but selects only left/right. Actual browser proof
  must wait for rendered data attributes, not assume the first animation frame
  follows immediately after a UI change.
- Prepared source-only roots078–090 remain usable. Their old3×3 grids are historical
  preparation; make new horizontal attempt roots and new2×2 grids without overwrites.
- Judge final64×35 eyes for subdued glints, irregular stepped iris clusters,
  coherent lids and stable blink anatomy. Reject vector-like round disks, purple
  rectangular bars, missing lids and harsh white flashes. Mechanical passes are
  not user approval.

No mod implementation, model comparison, voice synthesis or game tests belong to
this portrait-production sequence.

## Full portrait production after the existing-face milestone

For each new canonical face, inspect its original and calibrate a fresh layout.
Future source proposals are in `work/portraitNNN-full-preflight`; the parent's
source-only review is in `work/portrait072-076-091-095-inventory/parent-source-proposal-review.md`.
NPC075's provisional top mask clips original brow pixels and must be expanded;
NPC072 needs spare room for raised brows and the AA mouth opening. Proposals are
not final production layouts.

Prepare the immutable original halves and repeated reference grids with
`PreparePortraitSplitWorkspace.ps1`. Six workers own disjoint mood directories.
Each generates one base-eye atlas and one mouth atlas from the exact inspected
local half-grid, retaining full results, exact arguments, requests and raw bytes.
Base eyes are2×2 open/half/closed/open; mouths are3×3 A/B/C/D/E/F/G/H/X with the
full Rhubarb definitions from `rhubarb-mouth-contract.json`. No free-form letter
interpretation, full-face atlas, independent mouth scaling or procedural iris edits.

After all twelve raw sheets exist, build neutral mouths first using
`BuildSplitNeutralMouths.ps1`, then `BuildSplitEmotionMouths.ps1`, then
`BuildSplitPortraitPrototype.ps1`. Keep one coordinator responsible for the shared
manifest and combined build. Inspect eighteen eye phases and fifty-four mouths,
source-like palettes, distinct moods, closed lids, stable brows and valid mouth
pose semantics. Independently verify all972 eye/mouth composites and exact
original exterior; neutral open and neutral A/X must be original-exact.

Only after base phases are selected, prepare the six horizontal thinking grids
from their actual same-emotion open/half/closed outputs. Produce and review the
thirty-six thinking halves before calling the new portrait complete. Publish
complete manifests into the same gallery dropdown, preserving shared female/male
voice references and existing timing files. Audio is reused, not regenerated.
Record mechanical verification and parent review separately from pending user
acceptance. One bounded correction requires a concrete inspected defect; retain
all rejected attempts. Save difficult special cases in the exception list and
continue rather than repeatedly guessing.
