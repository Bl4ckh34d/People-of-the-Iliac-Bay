# Native-context routing and eligibility evaluation — 20 October 2026

## Verdict

The native context format now has actual-model evidence. D3-mini is the stronger and more order-stable router in this finite diagnostic; that does not prove overall model superiority or end-to-end NPC competence. Neither router can authorize actions alone. Both still interpret some hypothetical/reported speech as a current request.

The v2 eligibility change removes unavailable offer/commitment routes from the native menu. This is a deterministic validity improvement, **not a demonstrated semantic-accuracy improvement**. Development accuracy improves for D3, but held-out accuracy falls for both models. Keep the route output as an untrusted proposal and retain downstream interpretation, native consent, exact binding and receipt gates.

## Runtime change

`LocalDecisionRouting` now uses purpose `npc-speech-act-route-v2`, context `native-speech-act-route-context-v2`, and candidate revision 2. The wire remains `npc-decision-v2`. SOCIAL and ACTION_REQUEST are always available; OFFER_RESPONSE requires the validated pending offer captured by the host, and COMMITMENT_UPDATE requires at least one active typed commitment. Inactive/proposed/completed records do not unlock it. `RouteVocabulary()` exposes a defensive read-only four-route taxonomy for test capture; actual requests contain only eligible choices. All options remain non-effectful.

The full classifier wording and route meanings are unchanged. No player-text keyword or regex determines menu eligibility. The host logger reads the versioned routing purpose from the builder. Pending-offer durations retain the native nonnegative contract, including zero for immediate actions; this router does not impose a new universal timed-action bound.

## Test inputs and controls

The explicit native probe captures exact UTF-8 request bytes with CreateNew files, byte counts and SHA-256, only when an owned fixture opts into a capture directory. Normal games and production logs do not write raw dialogue. The v2 capture also exports the exact complete route vocabulary. Its deterministic transport parses the actual outgoing criteria and returns probabilities for those keys only, so the native test does not fake a four-option response for a two-option request.

The initial 48 fresh diagnostic roots were frozen before inference. They became development inputs after their failures motivated the state-based menu change. Gold ambiguity was reviewed before any model output, with explicit alternatives for genuinely ambiguous speech. An additional 32 roots were authored and independently reviewed before any v2 inference: 16 SOCIAL, eight ACTION_REQUEST, four OFFER_RESPONSE and four COMMITMENT_UPDATE. These are fresh diagnostic cases, not a representative sample of all NPC interactions.

The 48-root cases hash is `43f9f7f2805c25dfee0b43d13521990598a67fcd716cbc8b356bc46a330420aa`; the 32-root hash is `ce058c6fa91f525a94827748db4a5c1657b62de8a066bb0dc14bbe6a46b4b819`. History, offers and commitments are explicitly synthetic overrides. Other NPC/world facts come from a real captured native snapshot. To compare contracts without changing the NPC, the controlled v2 template retains the v1 captured facts and modifies only purpose, candidate revision, context version and the eligible criteria. The actual v2 native capture verifies the same instructions and vocabulary. `paired-template-provenance.json` records the transformation and hashes.

Each root ran in both option orders against both models. Every run froze payloads, gold, sources and hashes before inference. The 640 calls ran serially with the game closed, no retries and no prompt/gold edits during the runs. Qwen uses the same input through a JSON-schema chat adapter; D3 uses its native choice endpoint. They are distinct output mechanisms, not interchangeable native implementations. The later runs' source hashes match the final runner. The first run predates v2 support; all 640 raw responses were subsequently revalidated without inference by the final parser and all decisions/scores were unchanged.

## Results

Counts are decisions over two option orders; those repeated arms are not independent samples.

| Sample | Model | v1 correct | v2 correct | v1 / v2 action-related routes on SOCIAL controls |
|---|---|---:|---:|---:|
| Retained development, 48 roots | D3-mini | 71/96 | 76/96 | 17/40 / 18/40 |
| Retained development, 48 roots | Qwen | 62/96 | 58/96 | 15/40 / 15/40 |
| Fresh held-out diagnostic, 32 roots | D3-mini | 61/64 | 59/64 | 3/32 / 4/32 |
| Fresh held-out diagnostic, 32 roots | Qwen | 49/64 | 42/64 | 4/32 / 7/32 |

V1 development contained ten unavailable D3 selections: six OFFER_RESPONSE choices without a live offer and four COMMITMENT_UPDATE choices without an active commitment. Qwen had two unavailable OFFER_RESPONSE selections. V2 has zero unavailable selections in either sample. Six of D3's ten become gold-compatible, two remain wrong as ACTION_REQUEST, and two move to wrong SOCIAL results. Both Qwen unavailable choices become correct SOCIAL results. Other decisions also change; do not count these repaired invalid choices as a net semantic gain.

The held-out v1 sample already had zero unavailable choices. V2 therefore repairs none there. D3 has two correct-to-wrong changes and no wrong-to-correct changes. Qwen has eleven correct-to-wrong changes and four wrong-to-correct changes. On v2 held-out roots, D3 is correct in both orders for 29/32 roots and changes route with order on one root; Qwen is correct in both orders for 14/32 and changes route on fifteen roots.

D3's remaining held-out failures include interpreting a hypothetical guide question and a reported future request from the player's brother as current requests, plus one live waiting-offer assent routed as a new request. Qwen also misses current commitments and shows strong option-order sensitivity. No generated route was executed; “false action” counters in raw files mean an action-related **route**, not an accepted job or native effect.

All 640 inference responses were HTTP 200, parseable, reported the expected model, obeyed the offered menu, and fit both the 10-second maximum and five-second default native budgets. V2 observed median latency was approximately 116 ms for D3 and 156–163 ms for Qwen; maxima were below 260 ms. These are warm serial observations with repeated context and prefix reuse, not a game-frame or simultaneous-production performance benchmark.

## Native verification and serving configuration

Parent verifies **57/57 adapter tests**, **1194/1194 Core tests**, **72/72 Python harness tests**, and zero-warning/error net472 C# 7.3 RuntimeCompile. New runner tests cover raw duplicate-key/numeric rejection, selected-max probability parity, history/context bounds, exact model IDs, frozen inputs, eligible menus and v1 compatibility.

Installed package SHA-256 is `6396B5700E5FBE5789824C462D2BF798478CCFDB3C318420A58F23EC35DDCFE0`. The owned v2 native capture probe passed and cleaned up game PID 29432. It exercises the production clock submission once, two successful callbacks, three distinct scene/save/close cancellations, session rotation and unchanged measured state during isolated shadow work. Scene/save invalidation is invoked in memory; this is not a real scene-transition or disk-restore test.

An actual-service smoke through the current C# adapter succeeded once on its creation thread at about 514 ms. Separately, the exact unmodified captured v2 native clock payload was sent byte-for-byte to D3 and returned SOCIAL at about 438 ms. These prove separate adapter/payload paths; they do not prove an NPC-to-model-to-action loop in Unity.

Both services used llama.cpp `b11680-b797b9ca8`, context 4096 and one slot. Models were D3-mini Q4_K_M with its Q8_0 projector and Qwen2.5-7B-Instruct-abliterated-v2 Q4_K_M. Model/runtime hashes and configuration are preserved. The guarded standalone startup created owned Mini PID 35440 without changing Qwen PID 19472. After testing, executable and start-time ownership were verified before stopping only Mini. Read-only final checks show Qwen on 8080 and no listener on 8093. No shutdown occurred.

## Evidence and next gate

Evidence lives under `work/native-route-replay-20261020/`: native v1/v2 captures and vocabulary, model/runtime/source/package manifests, serving configuration, frozen `replay-run1`, `heldout-v1`, `development-v2`, `heldout-v2` directories, raw attempts/results, `comparison-verdict.json`, `current-parser-revalidation.json`, test/build/native logs and final service state. The final runner sources are copied to `runner-source/`.

The next bounded gate is the full route-to-interpreter-to-Core lifecycle, starting with Wait and explicit Follow-to-Wait substitution controls. The existing CoreBridge needs a fixture-only timed Wait completion observation; current commerce-only receipts cannot stand in for it. Keep semantic fidelity, NPC willingness, acceptance, execution and receipt truth separate. This first action gate does not replace the larger roadmap or prove current/future actions, memory, agenda, combat, vision or hearing.
