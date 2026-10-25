# Decision service boundary — 20 October 2026

This report preserves the original Slice B checkpoint. The subsequent [Slice C report](2026-10-20-decision-shadow-routing.md) supersedes its no-caller/instruction-gap status and records current verification separately.

## Implemented scope

Slice B of the [NPC architecture](2026-10-20-npc-agent-architecture.md) now has a source implementation. It adds no action authority and has no production routing caller. `LocalDecisionClient` starts disabled; enabling also requires an explicitly configured expected model ID. Shadow routing, Unity lifecycle acceptance and a game-package rollout remain separate work.

The source consists of:

- `mod/Scripts/LocalDecisionWire.cs`: ordered UTF-8 `/v1/systemone` request construction, exact-byte SHA-256 and a bounded strict response parser.
- `mod/Scripts/LocalDecisionTransport.cs`: one loopback HTTP POST, no redirects/proxy/retries, absolute timeout with abort, bounded streaming and strict UTF-8 decoding.
- `mod/Scripts/LocalDecisionSession.cs`: one active request, owned cancellation token, worker parsing, bounded completion storage and main-thread freshness validation/delivery.
- `mod/Scripts/LocalDecisionClient.cs`: a small MonoBehaviour wrapper. Update pumps the session; disable/destroy cancels it. It does not create tasks, access inventory or call an executor.
- `NpcDecisionResult.IsCurrent`: the shared Core freshness predicate, reused by successful and failed transport outcomes. It preserves the established allowance for archive-head advancement within the same request binding.

## Contract and lifecycle

The caller supplies a previously captured immutable `NpcDecisionRequest`, a loopback connection and a main-thread function that returns the current binding/revisions/options. `ExpectedModelId` must be configured before `Enabled=true`. A busy, disabled or unconfigured session does not queue a request. Configuration changes invalidate outstanding work.

Wire input includes request ID, actor identity, original turn, context, snapshot/candidate revisions and the ordered offered options. The local ticket retains the full actor/session/save/archive/scene binding. The hash describes exact transmitted bytes; it is not an independent proof of all local binding fields or authenticated model provenance. The server's reported model ID must equal the configured ID, and an HTTP status of 200 must be confirmed before a successful decision is possible.

Responses require one `decision` choice with its probability map. Duplicate JSON properties, malformed or trailing JSON, invalid Unicode, numeric overflow or nonzero underflow, wrong field types and unsupported answer shapes are rejected. Core checks offered membership, exact vector keys/ranges/sum/selected maximum, confidence and freshness. Request and response wire bounds are 128 KiB; the Core request's smaller individual field limits still apply.

All session mutations and Pump run on the creation thread. HTTP and parsing run on a worker. Before any result is delivered, including timeout, HTTP failure, malformed response or model mismatch, the session captures current state and applies the shared Core freshness predicate. Missing/throwing capture or a changed binding returns Stale without a selected option. Pump rechecks the ticket after capture, so cancellation or disposal from that callback suppresses delivery. Consumption releases the slot before the user callback; callback exceptions are counted without exposing request text. At most one callback is delivered per Pump, including reentrant follow-on requests.

Cancellation invalidates ownership before aborting transport, clears already queued results and cannot affect a later request. Wrong-ID cancellation is inert. The real transport refuses non-loopback/wrong-path endpoints, never follows redirects, does not consume non-200 error bodies, checks declared and streamed response size, and enforces an absolute deadline even when a peer continuously sends small chunks. Technical failure is never converted into an ordinary NPC refusal.

## Verification

The portable `tests/DecisionAdapter.Tests` project compiles the actual runtime wire/session/transport source under C# 7.3 with a net8 test host; it does not simulate those implementations with replacements. Its fake and actual loopback TCP peers exercise malformed responses, model mismatch, cancellation races, stale success/failure, queue cleanup, callback reentrancy/exceptions, confirmed status, redirect refusal, byte/UTF-8 bounds and slow-drip deadlines. Tests require neither GPU/model services nor the game. The existing CI workflow now includes this project.

Parent results:

- **31/31 adapter cases** after TDD fixes, including a new callback-budget test that first observed two deliveries from one Pump and then verified one per Pump.
- **1191/1191 Core cases** after adding the shared freshness predicate and three regression cases covering binding dimensions, option identity/order and permitted archive-head advancement.
- **54/54 existing Python harness cases**.
- **RuntimeCompile net472 / C# 7.3: zero warnings and errors**, including the MonoBehaviour wrapper.

One explicit actual-service smoke used the C# session, transport and wire against the owned D3-mini Q4_K_M server. It returned `Ok`, chose `wait`, retained the exact locally computed request hash and configured model identity, and invoked its callback exactly once on the creation thread in approximately **475 ms**. This is a wire/dispatch smoke, not a semantic score or latency benchmark. Sources and executed assemblies were hashed before that call. The final one-callback-per-Pump change was made afterward and verified by the 31-case regression suite; the saved live manifest therefore identifies the preceding revision, not the final revision. A planned repeat was rejected by automatic tool policy before execution, without a detailed reason; no second model process started.

Evidence: `work/decision-boundary-20261020/{live-manifest.json,live-result.json,adapter-tests-final.log,core-tests-final.log,python-tests-final.log,runtime-compile-final.log,final-services.json}`. The owned Mini service was stopped; the selected Qwen service remains on port 8080. No new game bundle was built or installed.

## Remaining boundaries

The .NET host's creation thread is not a proof of Unity's actual MonoBehaviour lifecycle or Mono HTTP behavior. The next slice must observe real conversation snapshots in opt-in non-mutating shadow mode, test close/disable/save/scene ownership in the game, and retain native frame budgets before enabling an action path.

The shadow caller must also resolve prompt compatibility explicitly: Core `Purpose` is bounded to 128 characters and the current wire sends it as question instructions, while the offline routing instructions are longer. Do not silently truncate or replace the tested protocol. Version the instruction/context contract and assess its semantics before production use. This adapter does not resolve the existing offer/cancellation/hypothetical/unknown-target errors, general planning gaps, dialogue ID leakage or navigation stall.
