# Mod implementation resumed: reliable appraisal and selected-model evidence

22 October 2026. This chat resumes mod implementation while the original chat continues portrait production. The approved common NPC framework remains the target; this is a bounded implementation checkpoint, not completion of the roadmap.

## Implemented fixes

`NpcExperienceCommit` previously rejected a pending NPC appraisal whenever *any* service contract changed. The ticket now captures the observer's complete contract ID/revision map, including reserved and inactive contracts. A different NPC's contract mutation no longer discards this observer's computed response. The observer's own added, removed or revised contracts still invalidate the ticket. Appointment, task, action, relationship, mood, knowledge, save-branch and exact-once checks remain in place. This removes one demonstrated source of unnecessary re-evaluation; no overall latency improvement is claimed. Global appointment/relationship revision invalidation remains a separate follow-up.

The selected-model evaluator now checks transport evidence before parsing or committing a synthetic episode: exact case-owned artifact path, captured request bytes, source ownership, unique ordered start/completed journal records, request/response hashes, HTTP 200, lane and exact model identity. Failed, missing, changed or swapped responses cannot be treated as valid Core output. The Python runner checks model identity on every response and permits offline resume of terminal/uncertain runs without contacting servers. Its source is included in session pins. Decision validation explicitly describes a frozen request, not live game freshness. These are local integrity checks, not cryptographic authentication against coordinated editing of evidence and journal.

The P07 active-task fixture had expired at capture (`DeadlineAt=500`, capture time `1000`). Its deadline is now `1500`, with a regression assertion. P15 already included `nativeValuation=unknown` versus `nativeValuation=known,nativeGoldValue=35`; the earlier input audit's missing-valuation claim was false. A regression protects the actual projection. Historical captures and reviews remain intact.

## Verification

- Baseline: Core 1631/1631 and evaluator self-test passed.
- New regression tests reproduced the unrelated-contract invalidation, expired-task fixture, and twelve transport/receipt failures before fixes.
- Final source checks: Core 1634/1634, DecisionAdapter 84/84, focused Python transport/session tests 28/28; evaluator self-test and selected-session self-test passed. RuntimeCompile built with zero warnings/errors.
- An independent Luna/high read-only review found no actionable defects in the scoped changes. It did not run a game or certify model semantics.
- Unity built, reopened and installed the 828-asset bundle after its existing editor-mode fallback recovered the batch license failure. Installed SHA-256: `13df0a67fc918ef8f1ae86673f78ef06608c105259e8358047b4abbcb01469a0`. The previous `d3278604` bundle is preserved and hash-verified in `work/mod-resume-install-backup/`. Deployed Core source matches the repository byte-for-byte. Logs/receipt: `work/mod-resume-unity-build.log` and `work/mod-resume-install-verification.json`. Native gameplay acceptance of this correction remains open.

## Actual selected-model run

The existing services reported Qwen2.5-3B-Instruct Q4_K_M on port 8080 and D3-Nano Q8_0 on 8093. No model or device selection was changed. The selected request set was frozen before generation. Every request was attempted once; no failures or poor answers were rerolled.

`work/mod-resume-selected-02/` retains the derived suite, original suite hash, current source pins, exact requests/responses, token preflight, HTTP/model evidence and Core results. It contains 22 Qwen appraisal requests (11 complete control/treatment pairs) and 14 D3 decisions. All 36 returned HTTP 200 and passed the production parser through the repaired receipt boundary. No result was committed to a live game. Maximum Qwen prompt length was 1901 tokens, plus the unchanged 1024-token response budget, within context 4096.

Observed response times: Qwen median 2197 ms (2022–2621 ms), D3 median 68 ms (58–91 ms). These are local isolated-request timings, not end-to-end conversation/TTS latency or a loaded-game VRAM test. The previous all-GPU experiment150 remains the hardware-fit evidence.

Technical validity is not semantic success:

- All three indirect-threat treatment cases P11/P12/P13 were classified `DirectThreat`. The quotation in P11 also received an affinity penalty. These are retained attribution errors.
- All 22 appraisals proposed zero mood and zero trust changes. Some affinity/fear values differed, but this run does not demonstrate useful personality, gift-value or trust differentiation.
- D3 selected `ACCEPT` in all 14 ready cases. This does not prove 14 mistakes: several requests may reasonably be accepted. It supplies no demonstrated choice-level differentiation in these contrasts.
- Both D3 attribution cases remain blocked because the production willingness input lacks the necessary event-source receipt. They were not fabricated or generated.
- Independently created appraisal stores still have different opaque branch/knowledge epoch IDs. These are nuisance prompt differences. P14 attribution and P17 no-show also carry compound context changes. Do not describe the pairs as exact single-factor causal measurements.

## Rejected output-order experiment

`work/mod-resume-field-order-01/` contains 22 further first responses. Each uses its baseline's exact prompt/binding, grammar rules and decoding settings, changing only grammar field order so speech class/target precede effect magnitudes. No production source was changed for this experiment, and its responses were not Core-committed or accepted as native outcomes.

All three indirect-threat treatment cases remain `DirectThreat`. Effects are identical in 14/22 cases; eight speech cases change numeric effects. In particular, P11's quoted threat and P12's reported threat gain a proposed fear increase of 25, and P12's affinity penalty becomes -25. The rearrangement therefore does not solve attribution and can worsen false effects. It is not promoted.

## Implementation priorities for small models

1. Add and evaluate a narrow, receipt-bound speech-act/target stage before effect appraisal. Classify actual speaker, quotation/report/hypothesis and target without asking the same small inference to interpret a large policy plus numeric effects. Compare optional Nano and Qwen-only paths on held-out paraphrases; no keyword/regex intent shortcuts. A label remains fallible, with native freshness, ownership and effect bounds unchanged.
2. Keep tasks small: interpretation, native feasibility, discretionary choice, execution receipt, then natural wording. D3 chooses among current eligible outcomes; Qwen phrases confirmed results. Neither model may invent completed actions, prices, knowledge or contract rights.
3. Compact prompts by decision relevance and keep opaque binding data host-owned where a separately versioned protocol can preserve exact receipt validation. Do not delete evidence, weaken the parser or assume a shorter prompt improves behavior without paired/held-out checks.
4. Preserve persistent personality, independent affinity/trust/fear/resentment, observer-known memories and actual obligations across speaking, deciding and appraising. Prefer event-triggered updates and foreground-priority scheduling over per-frame inference or another resident heavy model.
5. Continue the approved roadmap with separate native acceptance: multi-step tasks and truthful failure wording; dialogue/TTS cancellation and replay; schedule/quest compatibility; contract funding/settlement; then physical companion roles and return home. Source fixtures do not establish these absent gameplay features.

Portrait assets, the selected model files and the game-save data are outside this checkpoint's authored changes. Existing approved portrait packaging can be rebuilt by the standard build script; broad portrait rollout remains with the other chat.
