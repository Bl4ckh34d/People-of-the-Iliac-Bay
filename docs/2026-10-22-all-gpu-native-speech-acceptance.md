# All-GPU native spoken conversation acceptance

22 October 2026, checkpoint 153b. The selected Qwen2.5-3B Q4, D3-Nano Q8 and Breeze Q4 GPU profile now passes a bounded native spoken conversation test. This supersedes checkpoint152's open full-WAV integration gate; it does not establish general dialogue quality, streamed speech acceptance or complex task execution.

## What ran

The existing owned appraisal/reopen fixture uses three real `SubmitFreeText` turns, normal D3 routing and first Qwen completions. The opt-in `-DecisionRoutingAppraisalReopenSpeech` modifier enables the production full-WAV Breeze path. Each accepted response must match a real mixer-onset receipt by person, conversation token, request identity and exact text. Retained per-request counters must show actual nonzero samples. The request must complete, the queue must drain and playback must stop before the next prompt. Two opening greetings are excluded and drained separately. The final speech PASS follows all three turn markers and exact baseline restoration.

| Turn | Accepted first Qwen wording | Nonzero mixer samples |
| --- | --- | ---: |
| Day inquiry | I've been managing as usual, what about yours? | 325636 |
| Direct insult | I see. Perhaps we should talk when you're not so angry. | 389630 |
| Apology after reopen | I understand. May I wait here for the agreed time? | 405368 |

All routes are Nano `SOCIAL`; each turn records one Qwen request and completion. The third request contains a verified pending wait offer. Its wording asks consent; it is not evidence of an accepted or executed wait. Earlier152's different unsupported wait promise remains preserved, not overwritten by this run.

The first153 attempt exposed two test defects: raising the D3 transport timeout above its permitted10000ms caused immediate `invalid_transport_request`, and reading a progressive UI row's visible label compared only the first word. The test now keeps the D3 timeout at10000ms and reads canonical row text. An earlier helper compile error was also corrected. Failed evidence remains in `native-full-speech-153`; the successful first outputs after those test corrections remain in `native-full-speech-153b`.

## Parent verification

- Fresh Core1592/1592 and decision adapter84/84 pass.
- Unity builds and installs745 assets; installed bundle SHA256 is `4219F2F1CBB0BFAE734803C6218CAD23F15378743C7A21C0766F2B38FD1488AD`.
- All587 current runtime/Core source files match deployed source copies; the installed bundle matches `dist`.
- Saved settings bytes exactly match the pre-test bytes; NPC voice remains enabled.
- Loopback model process identities match the previously verified selected GPU services. Qwen and Nano loaded paths and4096-token contexts match selections; Breeze remains pinned Q4 CUDA without lazy/idle unloading. These checks do not measure new peak VRAM.
- The raw native journal SHA256 is `E533034E55C1D2D7B46CCC0EBE266D8535A15EB07C59C402F04A1C532DE43B32`.

Evidence: `work/breeze-tts-20261021/parent-verification-153.json`, its verifier, `service-identity-153.json`, fresh test logs and `native-full-speech-153b/raw-journal.jsonl`. Generated WAVs were removed by normal conversation cleanup; the journal preserves receipt/counter evidence. No playback reroll or substitute WAV was used for these three replies.

The full roadmap remains active. Streamed presentation, opening/topic presentation beyond this receipt test, broad personality-dependent response quality, and real multi-step actions require their own evidence. New mobile portrait sheets are browser previews and are not included in this installed bundle.
