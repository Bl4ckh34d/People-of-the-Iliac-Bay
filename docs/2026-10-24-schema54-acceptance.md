# Schema 54 — bounded experience detail

Status: this bounded checkpoint is natively accepted and installed. The full ROADMAP remains open. Built, installed and archived final package SHA256: `4a924910cf3df34b87420d3d397c54f8b40b4a9872289d706e354bc19348c122` (`schema54-unity-final-build.log`, `schema54-accepted.dfmod`). All 693 deployed runtime/Core/editor source files match `schema54-source-final-freeze.json`.

## Scope

The experience inbox retains at most 8,192 current details globally and 256 per observer. Legacy over-capacity imports remain lossless and block new admission until eligible detail can retire. Absolute enqueue ordinals and classified source markers preserve replay protection after detail removal. Pending appraisals, required speech context and active mood effects remain pinned. Old unavailable mood detail has a typed availability result.

Retired speech appraisals keep exact immutable `.pic` cues linked to their original `.pim` conversation records. Pending conversation prefixes are archived in order before their cues publish. Portable archive manifest v2 carries both exact chains; failed validation does not publish a save slot. This does not summarize or delete autobiographical memory, bound the immutable archive, or enable STT.

Runtime maintenance runs behind the existing ready/unpaused/no-dialogue gate, examining at most 16 inbox rows and retiring at most four groups per two-second tick. These limits do not guarantee a fixed wall-clock budget: successful publication validates world state and eligible speech can require archive/index work.

Inn receipts now enforce native menu categories: drinks 0–3, food 4–10. The existing native adapter already used those categories. This is not the complete need-driven Inn journey. Accessible homes remain the preferred need destination; schema 53's observed home meal remains the delivered Food effect.

## CPU verification

- Main suite after the appointment-duty repair: 1,696/1,696, `work/life-core-20261023/temp/attended-duty-final-green.log`. Adding the speech clock-boundary regression produced 1,697/1,697 in the probe owner's Release terminal run with workspace TEMP; it changed no Core implementation.
- Final RuntimeCompile: zero warnings/errors, `schema54-runtime-social-clock.log` in the same work directory.
- Focused retention 8/8, migration 8/8, cue branch 1/1, Inn consumption 17/17; portable archive 4/4 with 28 assertions plus the existing transfer script.
- Real-retirement source replay: appointment/controlled completed native transfer receipt 2/2, older projection suites 6/6 each, source-ticket speech retirement/JSON restore/branch test passed, speech admission 12/12.
- Health-only long run accepted and retired 10,001 events over 27.40 simulated years with six JSON restores. A separate near-capacity mixed pending fixture retained all 8,192 rows and its overflow source. The lazy all-pending maintenance path measured p50/p95/max 0.000/0.001/0.968 ms; admission remained 17.164/49.633/100.186 ms. This is not native frame-pacing evidence or one combined multi-year mixed-source run.
- Repaired source manifest: `schema54-source-repair-freeze.json`; Core DLL SHA256 `ead89dcb52c4e66fde69168a7f4911f369a3af230340ef2c81e75d2585a68b86`.

## Native findings and repairs

The first candidate (`31a8416a39fe28f9c180cdc5ca02e4450ee7f058495863fd0d67183d9c170653`) passed actual healthcare, v5 retirement, pending pins, typed historical availability, native FullSerializer and branch replay. Its v5 PASS followed exact original-save restoration. Evidence: `schema54-healthcare-{native,town,player}.log`.

The poison test passed its actual native assignment stream, then failed the controlled capacity fixture. Investigation found a production issue: every historical attended appointment remained an active motivation duty, so more than 32 completed meetings blocked appraisal capture. A regression with 33 genuine reserved/observed meetings reproduced it. The shared duty projection now excludes only attended meetings strictly after their agreed end. The exact end remains valid, and accepted/waiting grace periods remain intact. No appointment or experience history is deleted to hide the problem.

The social test first stopped at explicit test-setting preflight. With scoped test settings, it exposed stale probe behavior: Native Talk had already opened a speech session, but the probe tried opening that same session again. The probe now reuses the actual host session and checks exact person/session/request bindings. Original settings were restored byte-for-byte (SHA256 `fec49ac45d8a3277f48b97d61c025df0991bce67863c9975d92e206e3c407885`). Both failed native runs restored the original fixture; all 13 baseline fixture hashes remained unchanged.

The final native results below supersede the intermediate failures without discarding their evidence.

On repaired package `845a03d5…`, both healthcare/v5 and native poison reruns passed with exact baseline restoration (`schema54-healthcare-repair-{native,town,player}.log` and `schema54-poison-repair-{native,town,player}.log`). Healthcare also verified real potion effect/one-unit consumption, alarm interruption, autonomous recovery, physical return, paid-service pause without paused-time billing, and exact player-currency restoration. Poison verified real assignment capture and source-cursor replay plus controlled pending-at-capacity drain through native FullSerializer; it does not establish combat infliction, symptom simulation or persistence of the active poison effect.

The repaired social probe then revealed a second test-only mismatch: its issued speech used fractional snapshot time, while the real input/commit path uses the native whole-second clock. The existing Core correctly rejected a source observed later than the commit timestamp. The probe now uses the production clock and matches the production cleanup of rejected receipts. Its final run passed duplicate repair, exactly one final mood/speech commit, stale callback rejection, bounded unavailable fallback, invalid-receipt rejection and controlled streaming-queue checks. Evidence: `schema54-social-final-{native,town,player}.log`, exit zero. This uses controlled callbacks and does not test live Gemma quality, TTS, audio playback or UI word timing.

The final bundle differs from the healthcare/poison-accepted `845a03d5…` candidate only in `SocialDuplicateReplyProbe.cs`; all production Core/runtime behavior is identical. The final source manifest and deployed files were hash-checked. All 13 original fixture hashes are unchanged, settings are byte-identical to the original, and game/editor processes are closed. No PC shutdown was performed.
