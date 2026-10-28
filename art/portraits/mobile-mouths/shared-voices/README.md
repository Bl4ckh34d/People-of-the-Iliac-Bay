# Shared review-gallery voices

Every female portrait uses `female.json`; every male portrait uses `male.json`.
Each manifest references the same six emotion-specific WAV/timing pairs for that sex.
The female manifest reuses the approved prototype's existing long clips without copying them.
The male clips are stored once under `male/`.

The gallery catalog maps `voiceSets` to these two manifests. Each animated catalog entry
must declare `voiceSet: female` or `voiceSet: male` from authoritative metadata or a
reviewed assignment. Missing assignments fail explicitly; there is no female fallback.
Original-only entries do not play unrelated audio.

Run `python tools/PublishPortraitGalleryVoices.py` to rebuild the shared package from
the retained source batches and validate all twelve WAV/timing pairs. No synthesis
occurs. `work/portrait-shared-voices-verification.json` records hashes and durations.
Recognizer timing estimates are not proof of pronunciation or emotional delivery.

This is a review-gallery convention. Individual in-game NPC voice identities remain
separate and are not replaced by these two shared speakers.
