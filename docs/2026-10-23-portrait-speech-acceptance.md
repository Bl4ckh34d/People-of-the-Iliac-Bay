# Portrait and speech integration: 23 October 2026

Installed Windows bundle: `peopleoftheiliacbay.dfmod`, SHA-256
`adb73ec2cbbf36656433b3ca24079cabe6397d7468c8706ee6a4123d69bb7f4c`.
Unity's `PeopleOfTheIliacBayBuild.VerifyAndBuild` passed and installed the bundle.
The final rebuild only updates the context probe's old personality-description expectation; production changes were already exercised by the live playback run.

## Implemented behavior

- Gallery catalog export includes CommonFaces 72–91, six moods, three gaze directions, blink phases and nine mouth shapes: 2,160 unique textures. Native portraits remain the fallback for unregistered or invalid assets.
- Gallery timing policy is shared and hash-bound. Asset/catalog updates are included by `Deploy.ps1`; JavaScript algorithm changes still require reviewing the C# equivalent. Export does not promote art review status.
- Each native person ID receives seven independently sampled N(0,1) voice-design traits, a unique persisted seed, and a separately generated neutral reference. Mood and personality affect utterance delivery while preserving that reference and seed. First reference generation is silent and uses the gallery's complete neutral sentence, not the short greeting.
- Normal speech waits for normalized WAV, exact word alignment and viseme cues. The native loader validates and caches both timelines before playback; the mixer receipt controls transcript reveal. Preparation failure exposes accepted text without unprepared audio.
- Normal gameplay cannot enable legacy sentence streaming through a saved setting alone. Explicit legacy test launches require `-peopleoftheiliacbay-allow-legacy-speech-streaming`.
- A valid disposition successor no longer suppresses the pending indicator. Its exact request binding retains the existing pending rows and clock; changed owners still fail existing ownership checks.
- Familiarity alone no longer provides the player's actual name. Evidence-based discovery of aliases/lies and resulting trust or resentment changes remain in ROADMAP IMM-08.
- `StartGame.ps1` stops only model/coordinator processes it started after the game exits. Reused services remain owned by their original caller. `-DevelopmentMode` keeps newly started services; direct game executable launches bypass this watcher.

## Verification

- RuntimeCompile: zero warnings/errors. Core tests: 1,687/1,687. Speech protocol: 22/22. Affected Python tests: 67/67, including combined per-NPC voice plus prepared presentation without a legacy registry, and opposite moods retaining one reference/seed.
- Gallery native probe: all 20 records, 486 compositions each, plus actor switching, source preservation, native fallback and real idle blinking; 10,692 counted composition checks including repeated controls.
- Identity native probe: event-created personality, current schema 44 FullSerializer round trip, role/fear projection, action snapshot ownership and exact baseline restoration passed.
- Context snapshot native probe: repeated-encounter familiarity does not reveal the player name; the life prompt retains its introduction/history rule. Detached facts, one-shot stages, request supersession, restore-epoch invalidation and archive-head binding passed.
- Retained-WAV native probe: thinking text, mixer-bound reveal, current-word highlight, replay, bound terminal failure, wrong-owner failure rejection and stale-close handling passed.
- Live native probe: fresh NPC opening greeting and real production `SubmitFreeText("Hi, how are you?")` with Gemma plus Breeze passed. No injected model response or forced routing. Generated reply: “I am enduring the cold. What brings you out into this snow?”
- Live reply `8e7ce376c9044e38b058dda76db5f35d`: 3.92 seconds audio; coordinator preparation 2,135 ms. It reused the initial 4.8-second neutral anchor and seed 580490649. Both sidecars existed by the nonzero mixer receipt; word reveal, animated mouth, captured mood projection and screenshot checks passed. Generation pending was visible for the measured 1.576-second generation pipeline.
- The owned Gothway Garden fixture was reloaded; original settings bytes were restored. Human saves were not saved or rewritten. Three exact test-owned services were stopped through ownership receipts, with no cleanup failures.

Evidence lives under `DFU/work/dialogue-speech-repair-20261023/`. Native screenshots are under DFU's Unity `Application.persistentDataPath` in LocalLow; the portable game log is `Player/PortableAppdata/Player.log`.

## Limits

This verifies native playback and presentation, not a human listening review of every generated voice or emotion. The live reply committed neutral mood; positive/negative delivery identity and all six portrait states have separate automated coverage. It is not a complete semantic-quality or roadmap acceptance.

First-time voice reference creation adds latency. CPU alignment/viseme helpers observe cancellation at helper completion or their bounded timeout. The entire prepared request has a 240-second deadline. Voice traits are textual design instructions, not numeric pitch-shifting DSP controls.

Two new probe issues were corrected before acceptance: native TalkManager caches portraits for the same target, so catalog testing must switch speakers; street readiness precedes actor spawning, so live selection must wait. The test wrapper must serialize settings as strings because DFU loads `Dictionary<string,string>` values. A legacy role probe's hard-coded schema 43 was updated to the current schema constant.
