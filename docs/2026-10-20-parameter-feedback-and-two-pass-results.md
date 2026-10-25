# Parameter feedback, two-pass extraction and friendship acceptance

This checkpoint continues the approved NPC architecture. The normal launcher retains one-pass extraction. The optional `-peopleoftheiliacbay-goal-two-pass` path is experimental; its parameter quality does not justify normal-launch promotion.

## Missing-parameter feedback

The normal extraction callback now recognizes a protocol-valid request whose Wait step lacks a duration. It freezes the original binding, turn, intent, step count, kinds and order, then sends one parameter repair request. That request includes the complete original turn, attributed history, previous output as quoted data, and the specific missing field, such as `Wait missing: steps[1].duration`. Each asynchronous stage consumes its own current-context token. Closing Talk or changing the current context prevents a late result from continuing.

The repair does not call itself. A malformed, unavailable, still-incomplete or structurally changed repair is rejected before willingness or native commitment. An empty repair prompt also rejects before dispatch. When the first proposal was protocol-valid, repair must preserve its existing sources, targets, quantities, durations, constraints, exclusions and unresolved-detail lists; only a missing Wait duration may be filled. A malformed first parameter output has no trusted full proposal, so its replacement instead passes the frozen structure and ordinary native gates. A valid complete result continues through existing target binding, D3 willingness and native validation. No regex interprets player language.

**Scope is deliberately limited:** the additional automatic completeness check currently detects missing Wait durations. Existing native checks reject unsupported steps, unresolved targets/constraints, invalid parameters and model-declared missing details, but those do not all receive this corrective pass. An omitted quantity, target or task that the model fails to identify is not guaranteed to be detected. This is not a general semantic completeness checker.

Native binding independently rejects a Wait with null duration. It no longer silently substitutes a candidate's unrelated 300-game-second duration. The existing five-argument injected parser seam deliberately bypasses repair so native rejection can be tested independently.

Rejections carry a bounded typed `UnresolvedReason` into the existing action-wording call. That call is instructed to ask for clarification in character, avoid technical field names, and request confirmation/rephrasing if the original turn already contained the omitted value. This is model guidance, not proof that every generated question will be appropriate.

## Paired local-model diagnostic

`tools/TestTwoPassGoalExtraction.ps1` runs actual Core prompts, grammars and parsers. Twelve frozen cases comprise three existing development chains, seven fresh positive cases and two negative controls. Both variants receive the same factual context/history; scoring expectations remain outside model input. The run made 36 calls without corrective retries or tuning.

| Measure | One pass | Two passes |
| --- | ---: | ---: |
| Core-valid output | 8/12 | 10/12 |
| Strict reviewed-field fidelity | 0/12 | 0/12 |
| Mean total latency | 1,443.5 ms | 2,336.5 ms |

Stage-one intent and kinds match the reviewed expectation in 8/12 cases. The three known 3/4/5-step requests decompose more accurately in the corrected two-pass variant, including their five explicit Wait durations, but other fields remain incomplete. Both variants falsely interpret the two negative controls as requests. This diagnostic isolates extraction and does not test the preceding D3 route decision. Empty target strings do not alone prove all target information is lost: native binding also receives the step source and complete turn.

Raw payloads, results and manual audit: `work/two-pass-production-20261020/paired1/`. Earlier unsuccessful variants remain preserved under `work/two-stage-chains-20261020/`.

## Native model limits retained

The opt-in real three-step extraction fixture correctly decomposes MoveTo/Wait/MoveTo, but its parameter pass omits the requested Wait duration. On package `6741CB30`, one actual repair call receives the missing-field feedback and still returns null duration. Native processing rejects it with zero D3 calls and no saved task/action. The positive chain acceptance therefore fails; it is not reported as a planning success. The D3 ACCEPT in this fixture is controlled, and is not reached in this failed run.

Evidence: `work/two-pass-production-20261020/native-repair-chain3.log` and its runner log. No repeated prompt tuning or 4/5-step rerolls are used to conceal the failure. Previously successful controlled execution of 3/4/5-step chains remains separate evidence in [the earlier acceptance report](2026-10-20-action-wording-and-chain-acceptance.md).

## Friendship acceptance remains partial

The new owned native probe verifies the initial five-point cap, repeated-line/reopen resistance, fractional twelve-hour credit, the first interval increase, and actual disk persistence of disposition offset/stage/fractional credit. Social classification is injected at the existing typed boundary; it is not a live semantic classification test.

Full multi-day acceptance remains open. After reloading a checkpoint at 15:03, the exact NPC Gwen Cienne is not an active outdoor actor. Her saved itinerary projects her home by approximately 12:08, so waiting longer does not create a legitimate outdoor conversation target. The probe preserves its awake/active/exact-identity requirements instead of substituting an NPC or disabling production schedule rules. Baseline save/clock restoration and owned temporary checkpoint deletion complete after the failed run.

Evidence: `work/friendship-native-20261020/native3.log`, `checkpoint3/`, and the read-only saved-itinerary review. Next acceptance fixture must visit the NPC's current building or select an exact identity whose projected contact locations fit the required clock advances. This does not establish a failure of the friendship ledger itself.

## Current verification

Installed/dist SHA-256: `E3D1F5BA8C883CA66B860FF29222A52B6B1B889E0F6F2149B058C04F4629A270`, with 651 runtime assets. Both bundle hashes match. Core: 1,267/1,267; decision adapter: 77/77. Runtime and Unity builds pass with zero warnings/errors. RED/GREEN evidence covers both repair eligibility and preservation of unrelated parameters.

The final owned `-GoalBindingProbe` run exits zero. It separately verifies an ungrounded duration quote rejects; null duration does not receive a default or reach D3; the normal one-pass continuation makes exactly one real Qwen repair call; and the still-null repair saves no action/task and dispatches no willingness call. Only the initial incomplete extraction is injected in this control. A separate injected complete eighteen-second proposal receives controlled D3 ACCEPT, saves exactly 18.000 game seconds and completes natively. The independent production free-text turn runs real routing/extraction but rejects at unavailable-target binding; that is a retained model/binding outcome, not a live planning success.

Final evidence: `work/two-pass-production-20261020/{core-final.log,adapter-final.log,runtime-final.log,build-final.log,native-final-run.log,native-final.log}`. The previous owned probe observer race is repaired by rereading native state after the same-frame reply can end its polling loop. No production execution gate is weakened. No DFU or Unity process remains after the owned run.

The broad roadmap remains active. No shutdown is requested.
