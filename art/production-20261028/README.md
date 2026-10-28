# NPC animation production, 28 October 2026

The requested scope is every mobile and stationary NPC appearance in the installed DFU configuration and its enabled mods: walk, run, standing idle, melee, bow, climb, jump and swim in eight views, one bed-compatible sleeper, and continued portrait animation.

This is an active production queue, not a completed asset release. Existing appearances and identities remain authoritative. Native five-view art with the engine's verified mirroring is existing eight-direction coverage; it is not newly generated art. New asymmetric weapons and clothing require explicit directional review.

Open `review.html` for a compact local preview of actual current candidates. `build_review_index.py` refreshes that document without changing artwork. The page explicitly separates generated candidates from integrated or accepted assets.

## Ownership and execution

The user explicitly authorized up to six Astra agents with High reasoning for this task. The parent owns source inventory, cross-agent coverage and final integration. Exclusive production directories are:

- `mobile-locomotion`: mobile walk/run.
- `stationary-locomotion`: stationary walk/run, including mod appearances.
- `combat`: melee and bow.
- `traversal`: climbing, jumping and swimming.
- `idle-sleep`: standing and bed sleepers.
- `portraits`: existing portrait repairs and uncovered portraits.

Each worker first verifies a source-bound pilot, then expands only a successful method. Keep failed attempts distinguishable from selected candidates. Do not replace canonical assets or gallery selections with a merely generated candidate. Shared gameplay code, builds, the running game and existing uncommitted work are outside these production edits.

## Source inventory

`build_source_inventory.py` writes `source-inventory.json` from current FLATS.CFG, enabled-mod settings, the hash-checked historical identity catalogue and existing sleep provenance. Its scope is the union of known identities, not a claim that every mod humanoid has been discovered. The mod container index supplies further classification candidates, particularly DET billboards outside the earlier texture scan. Archive/record identifiers always retain their provider; static portrait index 451 is not mobile billboard archive 451.

## Acceptance and progress

- [ ] Classify all enabled-mod humanoid sources and explicit exclusions.
- [ ] Reconcile each action against native and existing generated assets.
- [ ] Produce and visually review successful pilots for missing action families.
- [ ] Expand successful pilots to every missing applicable appearance/view.
- [ ] Validate alpha, cell isolation, dimensions, source identity and movement continuity.
- [ ] Review every portrait mood, gaze, blink transition and speech-mouth set.
- [ ] Package selected assets for the existing consumers and verify their references.
- [ ] Report remaining gaps honestly; retain generated, technically checked, visually reviewed, integrated and user-approved as separate states.

Sleep uses the established overhead green blanket and white pillow treatment. Portraits retain the established original-conditioned expression workflow. Inventory totals, file presence and hashes cannot prove visual acceptance or in-game behavior.
