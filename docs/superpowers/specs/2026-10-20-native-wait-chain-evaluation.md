# Native-context Wait chain evaluation

## Verdict

The first complete captured-context route/interpreter/actual-Core pilot is implemented and executed. It does **not** pass the semantic action gate. D3 routes 68/70 arms correctly, but Qwen generates twelve semantically wrong Wait plans that the synthetic-consent Core fixture accepts and completes. Typed legality and valid receipts cannot prove that a plan matches the player's request. Do not enable this experimental chain for normal gameplay.

## Frozen experiment

`tools/NpcArchitectureEval/fixtures/native-wait-chain-v1.json` contains 35 roots, tested in both route-option orders: ten supported requests, thirteen no-action controls and twelve unsupported requests. Before inference, parent review clarified two ambiguous quotation controls without changing categories or gold; provenance records the correction. Canonical cases SHA-256 is `c10eb8faa2563ff655f4845d6a206491c1ead537d4b8631c0ba08af6a8dd5eba`. These are diagnostic examples, not representative gameplay sampling; repeated arms are not independent samples.

An owned native probe on package `0A9B07FD0373C1A0E4EAC20B4BD779AE95F6C604BDE61208524B9192CCE7EE83` captures exactly one actual `ConversationActionCandidates` Wait. Its settlement-local WorldPoint is copied unchanged into the pilot. The disposable player is moved into the native eight-metre eligibility range before the shadow baseline and restored afterwards. The native probe still uses deterministic transport and dispatches no actions.

The sidecar identifies the same captured actor, snapshot and scene; its source request SHA-256 is `43812714c0b58532dfd339444055eb481c519357e40f1fd3a74b090da142fce7`. Parent directly generates and checks the capture pair. The runner freezes their hashes but does not cryptographically authenticate the candidate sidecar or independently cross-check every session/archive binding field. This is a trusted offline capture replay, not live request freshness proof.

`run1` stops before inference because the expected Qwen ID uses different path separators from the existing server's exact ID. Retain that failure. `run2` uses the server's observed exact ID after independently checking its normalized model path. No model or prompt changes occur between them. Run2 freezes all 140 possible request payloads, source/Core DLL hashes and gold before inference. There are 116 actual calls: 70 D3 routes and 46 Qwen interpretations. All return HTTP 200 with expected models and within ten seconds. Request/response hashes and every frozen source hash are independently rechecked afterwards.

## Results

| Group | Arms | Correct route | Core accepts | Semantically wrong acceptances |
|---|---:|---:|---:|---:|
| Supported Wait | 20 | 20 | 18 | 0 |
| Social/quoted/hypothetical controls | 26 | 24 | 2 | 2 |
| Unsupported actions/targets/conditions | 24 | 24 | 10 | 10 |
| Total | 70 | 68 | 30 | 12 |

All thirty accepted actions reach actual Core Completed action/task state with matching effect IDs. Completion observations and willingness are explicitly synthetic; the Core resident is the fixture buyer, not the native actor. No real NPC consents or Unity actions execute. A receipt proves that Core completed the supplied action, including a wrong one; it does not repair interpretation.

The wrong acceptances are the hypothetical captain question, Follow-only request, MoveTo-then-Wait request, remote-gate Wait, bell condition and porter condition, each in both orders. The route catches all unsupported requests correctly; Qwen still substitutes the available Wait target or drops a condition. Eight other interpretations fail the amount/unit invariant, including both supported requests with omitted duration: Qwen emits `amount:null` with `unit:"seconds"`. Those are rejected, not silently repaired. Two unsupported arms clarify; twelve are invalid. No-action due to invalid extraction is not successful clarification.

Warm serial route latency is median 114.1 ms, maximum 479.5 ms. Qwen interpretation median is 1007.9 ms, maximum 3956.1 ms. This is not a concurrent game-frame benchmark. Native natural-language replies, genuine willingness, arbitrary actions and live lifecycle integration remain untested by this pilot.

## Deterministic verification

Parent independently verifies 88/88 Python harness tests, 57/57 adapter tests, 1194/1194 Core tests, all three CoreBridge self-test modes and zero-warning/error net472 RuntimeCompile. The new fixture command `observe_wait_completion` accepts only the exact Executing Wait revision at or after FinishAt and before DeadlineAt. Early, stale, wrong-kind, duplicate and cancelled completions are rejected; restore and reconciliation retain actual Core state. It is available only in the offline bridge.

Evidence is under `work/native-wait-chain-20261020/`: native captures and logs, deployed-source/package manifests, frozen model/runtime hashes, both run manifests, raw model attempts/results, Core command journals, parent smoke and independent verdict. Owned Mini PID 25048 is stopped after executable/start-time verification. Qwen PID 19472 remains on 8080; no game process remains.

## Native performance diagnosis

Probe-gated phase timing is installed in package `E0097A80D59878544E2652C43EB0F087519DFBE6594E3DD7C2A6A575CD576581`. Repository/deployed `UnloadedSettlementSurvey.cs` hashes match. Detailed outlier logs are capped at twelve per survey and normal play adds no timing allocations.

The saved-city performance probe reproduces the original failure: `RMBLayout.GetLocationBuildingData` takes 1007.27 ms for map 414355624, enclosing ReadSteps takes 1007.36 ms, and maximum sampled frame is 1014.88 ms. The one-second gate remains failed. The preceding 60-second sample peaks at 281.47 ms, so measuring only initial loading would miss the regression. Raw log is `performance-instrumented-qwen.log`. This confirms the eager whole-location block-load region, not an individual block, disk parser or mod override as the ultimate cause. No performance repair is claimed.

## Next changes

1. Separate conceptual request extraction from available-action binding. The current candidate-only schema can represent only Wait; evaluate whether a full action vocabulary plus explicit unresolved/unsupported references prevents semantic substitution. Preserve conditions and multi-step requests before planning; do not add language regexes.
2. Score willingness separately after grounding a complete proposal. Current fixture consent is not a model decision. Test receipt-grounded natural English separately from structural completion.
3. Add an explicit deterministic Follow-to-Wait false-acceptance regression. Preserve this diagnostic as development data and author new reviewed cases before evaluating a changed protocol.
4. Replace or incrementally schedule eager location loading while preserving named-building assignment, mod replacements, block order and complete-only cache publication. A mod distribution must not silently require a custom DFU executable. Measure individual block reads before claiming cooperative yields solve every stall.
