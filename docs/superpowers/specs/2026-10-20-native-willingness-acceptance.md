# Native D3 willingness — 20 October 2026

Status: implemented, installed and native integrity checks pass on package `BF278D71631074835587843251FA9F0966F5735AD10BE0DC385EDCF7D0C6882F`. Model duration fidelity fails in the recorded example. The full mod roadmap remains active.

## Runtime change

With live D3 routing enabled, Qwen interprets action parameters without deciding NPC willingness. One D3 ACCEPT/DECLINE decision evaluates the complete immutable proposal, original player turn, attributed history, captured memory, personality, disposition and current activity. Only ACCEPT reaches existing native acceptance. This applies to single semantic actions, ordered native-supported tasks and accepted pending offers.

Every ordered step, target field, item quantity, duration and quoted price contributes to the proposal signature. Native feasibility and price are refreshed after the asynchronous decision. A changed proposal is not silently committed. A failed willingness request leaves agreement unconfirmed; it never turns interpretation-only output into consent. Existing conversation ownership and cancellation suppress obsolete callbacks. Callback exceptions are logged and never retried because an effect may already have been saved.

Qwen-only play keeps its previous combined interpretation/willingness prompts. Cancellation of an existing commitment remains separate from new willingness. Interpretation-only offer prompts distinguish player rejection (DECLINE) from directly stopping an existing accepted commitment (CANCEL).

Accepted acknowledgments still use authoritative typed receipts. This slice does not introduce a second prose renderer or replace the current task interpreter/binder.

## Contract parity and automated verification

- Core: 1209/1209 tests pass, including interpretation-only prompts and unchanged legacy prompt hashes.
- Decision adapter: 77/77 tests pass, including detached complete proposals, Unicode, target identity, bounds and finite consent choices.
- Production semantic parsing supplies a duration field even for non-timed actions. The proposal preserves that metadata, while native task validation uses zero effective duration for non-timed steps.
- Single-action validation uses the native action contract; multi-step composition retains its narrower supported-kind contract. Native-only single kinds are not incorrectly rejected as unsupported task steps.
- Purchase quotes follow native wallet bounds rather than an invented one-million-gold cap. Regression tests reproduce the previous rejection and verify the correction.
- RuntimeCompile passes with zero warnings/errors. Unity builds and installs the combined package.

## Native evidence and limits

The dedicated owned-fixture probe checks deterministic ACCEPT, DECLINE, malformed response, main-thread callbacks, no effects during evaluation and actual Talk-close cancellation. It then submits one current action request through production free-text chat and both real models. Semantic refusal or unresolved interpretation is recorded without rerolls. A clearly labelled synthetic decision can separately exercise the shared willingness gate and physical Wait mechanics when the live turn commits no action.

Intermediate failures are retained in `work/native-willingness-20261020`: an invalid test-only connection timeout, an outer timeout without enough diagnostics, and a routine-release failure from a fixture that placed the player on the NPC and allowed only 30 seconds for physical recovery. These failures do not by themselves prove a production defect. The revised fixture uses a surveyed observer position, waits for entrance readiness, logs actual timing/state and allows the same 150-second recovery window as the existing physical MoveTo/Wait probe.

Final native run exits zero. It confirms five deterministic main-thread callbacks, including a real two-step MoveTo/bound-Wait proposal, malformed-response handling, no effects during decision evaluation and cancellation by closing actual Talk. The one production free-text request uses real D3 routing, Qwen interpretation and real D3 willingness, in that wire order. D3 chooses ACTION_REQUEST then ACCEPT. No synthetic acceptance is used in this final run. The actual native reply is an authoritative receipt, not an additional Qwen prose generation.

The player requests ten seconds, but Qwen proposes sixty and D3 accepts it. DFU executes the saved sixty-second plan in 60.94 game seconds with 0.019 m maximum measured drift. `requestedDurationMatched=false` is retained in the passing integrity result. This is an explicit semantic failure, not a model-quality pass. Earlier outcomes and the duration-mismatch failure remain preserved rather than being discarded in favor of the final result.

The 150-second recovery diagnostic exposes a separate production bug: `ReturnActionToRoutine` rebuilds an already valid route every 1.5 seconds, resetting `Next` and waypoint progress. The actor moves 17.591 m but retains ownership. The fix builds only when the route is missing; the existing stall path still clears routes and retries after real obstruction. Native regression now advances through the retained 77-point route, physically reaches its routine door and releases ownership. No teleport, collision bypass or immediate ownership reset is added.

Evidence: `work/native-willingness-20261020/verified-result.json`, `native-passed.log`, `core-tests.log`, `adapter-tests.log`, `unity-build-passed.log`, and preserved intermediate logs. Dist and installed package hashes match; the bundle contains 635 runtime assets. No game/editor process remains after the owned test. Both local model services remain available.

Coverage remains bounded: ordered proposal evaluation is tested natively, but this run does not execute every ordered action family, force an actual save/load or scene transition during willingness, or certify general semantic accuracy. Accepted receipts remain deterministic. General conceptual extraction and planning remain incomplete.

## Next architectural slice

Continue the approved [goal/parameter extraction and native binding plan](../plans/2026-10-20-native-goal-binding.md) before naturalizing accepted receipts. Preserve exact original request spans, quantities, durations and unresolved constraints. Bind only verified references, then submit the grounded proposal to this willingness gate. Occasional local-model semantic errors remain accepted limitations; no universal perfect-score gate is added.
