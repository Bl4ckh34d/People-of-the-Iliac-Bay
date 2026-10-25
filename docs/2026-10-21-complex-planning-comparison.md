# Complex NPC planning comparison

## Scope and evidence

Two preserved offline runs use the exact same six-case fixture, Qwen identity, attributed conversation history, NPC facts, synthetic target catalogue, conceptual binder and actual CoreBridge assembly. The suite SHA-256 is `1adbb51fc5ec818524a7ecff07b4b58b7a6539c6ab34c3e1e886a909582abcd5`; the parent independently matched every case-input hash between runs. No plan executed a native action, and the fixture does not establish NPC willingness, physical reachability or live target availability.

The three positive cases reuse the existing three-, four- and five-step production-test utterances. All durations are in-game seconds. Three controls cover negation, quoted historical instructions and unresolved/unsupported requests. Gold outputs never enter model payloads. First responses, request/response bytes and hashes are preserved without semantic retries.

- Sequential protocol: `work/paired-complex-frames-112/live-01`. D3 or Qwen chooses one action kind per call, followed by shared Qwen argument extraction and target binding.
- Direct protocol: `work/direct-complex-plans-114/live-01`. Qwen extracts the complete conceptual sequence in one call, followed by the same target binder.

These are different orchestration strategies. This is not an isolated model-only quality ranking or an installed production planner comparison.

## Observed results

| Case | Sequential D3 + Qwen | Sequential Qwen | Direct Qwen |
| --- | --- | --- | --- |
| Three-step chain | Repeated kinds; clarified at limit | Repeated kinds; invalid argument extraction | Correct sequence, targets and 18-second wait; Core accepted |
| Four-step chain | Extra Wait; Python proposal rejected by Core | Repeated kinds; invalid source quote | Correct sequence, targets and 18/20-second waits; Core accepted |
| Five-step chain | Extra kinds; invalid source quote | Repeated kinds; invalid source quote | Correct sequence, targets, waits and return destination; Core accepted |
| Negated request | No action | False Python plan, rejected by Core | Misread as a request; clarified, with no plan |
| Quoted historical request | No action | Clarification | False Python plan; Core rejected a mismatched Wait destination |
| Unresolved/unsupported request | Clarification | Clarification after repeated kinds | Invalid non-verbatim source quote; no plan |

Direct Qwen produced three semantically correct, Core-accepted positive plans. Both sequential arms produced zero Core-accepted plans. Direct Qwen still mishandled controls. In particular, rejecting its historical-quotation proposal for a Wait-anchor violation is incidental structural protection, not proof that the system understood historical attribution. A structurally valid misinterpretation could pass Core.

The sequential selector receives the whole original utterance on every call and a prefix containing only indexed action kinds. It has no consumed source spans or completed arguments. Both models repeatedly selected kinds. This is a plausible protocol weakness, but no intervention in this run proves it caused the repetitions. Do not repair it with semantic keyword rules or a blanket repeated-kind ban: legitimate tasks can repeat the same action kind.

## Latency and runtime boundaries

Direct extraction plus binding took approximately 2.73, 3.56 and 4.01 seconds for the three positive chains on isolated GPU Qwen. These are plan-production times, not complete conversation, TTS or gameplay latency. All ten direct model calls returned HTTP 200. Original CPU Qwen, Breeze and persistent settings were preserved; temporary GPU Qwen was stopped and GPU D3 restored, with healthy original endpoints verified.

## Implementation implication

Retain direct complete-sequence extraction as the baseline for complex tasks. This evidence does not justify mandatory per-step D3 selection. D3's better no-action choices in these controls are useful evidence for selective assistance, but six cases are insufficient to choose a global default or deploy a new route unconditionally. Broader unseen control cases and the production integration contract remain necessary. Dialogue must verbalize the same accepted plan and engine outcome rather than make an independent conflicting decision.

The comparison tooling now checks exact Core result IDs and counts, including empty submissions. The parent verified all 158 architecture-evaluation Python tests after the change. This is offline harness verification; the installed mod remains unchanged by these model trials.

## Broader corpus preflight116

The existing 28-case `conceptual-goal-heldout-v1.json` was copied to `conceptual-goal-heldout-core-v2.json` with only reference bindings added under `expected`. The parent verified all model-facing inputs and existing gold remain identical. Actual Core accepts six of eight reference plans and rejects two: `cg_transfer_owned_key` uses non-native `item:brass-key` as an item UID, and `cg_buy_then_give_vial` uses non-native `container:apothecary` / `item:vial-31` as service identifiers. These fields must be nonzero unsigned decimal identifiers under the real transfer/commerce validators.

No inference started for this expanded corpus. The failure is preserved in `work/breeze-tts-20261021/heldout-core-preflight-116.json`; the fixture report explains the exact fields. These are fixture defects, not model errors. Do not publish a narrowed green score or silently alter the frozen inputs. A corrected, explicitly versioned synthetic corpus or a clearly interpretation-only evaluation is needed before that broader comparison can support Core acceptance claims.
