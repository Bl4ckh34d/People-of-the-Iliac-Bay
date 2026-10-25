# Gwen portrait viseme prototype

This is a single-person visual prototype, not a production-wide portrait system. It is allowlisted to saved person ID `s:206728581:h:131074:r:0` and native face record 76. A captured installed-game crop matched that record within the Breton female outfit-3 variation range; DFU does not persist the selected face record in the resident save, so the allowlist must fail closed if the live portrait differs.

The generated 1254×1254 atlas has nine equal 418×418 cells. Read cells in row-major order from the top-left: `A B C / D E F / G H X`. The native face remains the source of truth. Each update copies only the normalized crop corresponding to top-left native pixels `(18,35)` through `(45,54)` from the selected generated cell into the original 64×64 face. This widened crop follows user feedback that the lower lip clipped in the live prototype. Shape `X`, gaps, playback stop, speaker change, window close, or any identity/size mismatch restores the untouched native texture. A portrait identity or face-index mismatch clears the prototype composite instead of writing the previous actor's cached texture. The generated full-face tile must never replace the native portrait. Rhubarb H is a raised-tongue long-L mouth shape; K/G sounds use B. The atlas generation prompt used L for H, which is consistent with Rhubarb's mapping.

Use Rhubarb's own A–H/X cues from a `.visemes.json` sidecar, bound to the exact finalized WAV hash, request text hash, person/session, rate, and frame count before loading. `AudioSource.timeSamples` is the full-WAV playback clock; a binary search selects the current cue. While no valid sidecar is ready, measured output RMS drives a two-state open/closed `amplitude-approximation` fallback. Neither method is phoneme-perfect lip sync. Rhubarb processing runs separately from Breeze playback.

Primary references:

- [Rhubarb lip-sync README](https://github.com/DanielSWolf/rhubarb-lip-sync/blob/master/README.adoc) for A–H/X cue meanings and JSON cue timing.
- [Microsoft viseme documentation](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-speech-synthesis-viseme) for the general limitation that viseme groups approximate many phonemes rather than representing exact mouth geometry.

The face reference and cell geometry are recorded in `tools/BreezeTtsTrial/gwen-viseme-atlas-v1.json`.
