# Native decision shadow routing — 20 October 2026

This report preserves the first fixed-menu Slice C checkpoint. The [subsequent v2 eligibility and actual-model evaluation](2026-10-20-native-route-replay-evaluation.md) supersedes the current menu/version/test-count/package status below while retaining its native evidence and limitations.

## Scope

This is Slice C of the [approved NPC architecture](2026-10-20-npc-agent-architecture.md). It extends the [Slice B boundary](2026-10-20-decision-service-boundary.md) with an opt-in observer at the real player-turn entry point. It does not replace the current conversation router, authorize an action, or change NPC dialogue. The default-disabled state is an integration stage, not the intended permanent architecture.

`SubmitFreeText` observes the same successfully captured immutable snapshot used by the normal conversation. It does not capture again, consume a conversation stage token, execute tasks, or append dialogue. Returning greetings are not new player turns and do not enter this observer. The ordinary game has no shadow session or request construction without the explicit command-line opt-in.

## Versioned inputs

`NpcDecisionRequest` now separates strict short `Purpose` metadata from `Instructions`. The wire version is `npc-decision-v2`; the routing purpose is `npc-speech-act-route-v1`; the native context version is `native-speech-act-route-context-v1`. The legacy request constructor preserves compatibility by mapping Instructions to Purpose. Explicit instructions and original player text permit CR/LF/TAB within 8,192 characters; context permits them within 16,384 characters. Other control characters are rejected. No field silently truncates the latest turn.

The fixed SOCIAL / ACTION_REQUEST / OFFER_RESPONSE / COMMITMENT_UPDATE options and full instructions preserve the existing `goal_protocol.py` router wording. Every offered route is non-effectful. The context contains detached native facts, current typed commitment IDs/statuses/revisions, a validated current offer copy or null, attributed prior transcript rows, and separately labeled historical memory. The current turn stays separate in OriginalTurn. History drops whole oldest turns to a 6,000-character budget and records the omitted count. Native historical recall is bounded to 4,096 characters. Oversized/invalid observations are skipped with metadata rather than submitted with clipped player intent.

Offers are read only when the existing native history, person, session and expiry predicates match. Offers are never inferred from old dialogue. Active commitments use explicit typed status rules; proposed, completed, failed and cancelled tasks do not become live commitments merely because their text appears in history. Multiple live commitments remain an array.

## Ownership and lifecycle

`LocalDecisionShadow` retains one latest pending observation, coalesces replacements before HTTP submission, cancels its tracked predecessor, and deduplicates exact logical inputs independently of random request GUIDs. Session configuration changes invalidate dedup state. A shadow cancellation cannot cancel another owner's unrelated session request. Results remain typed metadata; `NativeDispatchAttempted=false` describes this observer's lack of dispatch, not a measurement of all game-world effects.

The host pumps completions on the main thread. Current native snapshot ownership is checked again before delivery. A newer snapshot or committed successor conservatively makes the old snapshot stale. New/closed sessions, save-epoch changes, scene load/unload callbacks, host disable, settings disable and destruction cancel owned work. No observer callback supplies a player-facing refusal or overrides current classification.

Logs contain IDs, revisions, wire protocol, routing purpose, status/failure, selected route, exact request hash, latency and bounded diagnostic code. They omit raw turns, names, model paths and raw model responses.

## Explicit opt-in

Start the already configured local decision service separately, then launch DFU with:

```text
-livingcities-decision-shadow -livingcities-decision-model "<exact model ID returned by the service>" -livingcities-decision-endpoint http://127.0.0.1:8093/v1/systemone -livingcities-decision-timeout 5000
```

The configuration parser starts no service and probes no endpoint. It requires an exact model ID, a loopback unauthenticated HTTP `/v1/systemone` address without query/fragment, and a 100–10,000 ms timeout. Defaults are port 8093 and 5,000 ms. Invalid or ambiguous configuration keeps the observer absent.

## Verification

Parent verification currently records **52/52 adapter**, **1194/1194 Core**, and **54/54 Python harness** cases, plus zero-warning/error C# 7.3 net472 RuntimeCompile. New cases cover exact multiline instruction preservation and limits, native route/context construction, whole-turn history bounds, current offer/commitment data, explicit configuration, logical dedup, latest-only coalescing, unrelated-request cancellation, configuration changes, observer reentrancy and typed stale/failure results. Configuration acceptance regressions were observed failing with the parser stubbed before the final green run.

The native probe uses a real loaded resident and the actual host observer/request builder with an injected deterministic transport. It separately tests a synthetic captured turn and the production `SubmitFreeText("What time is it?")` hook. The clock reply is a deterministic existing native fast path, so neither test requires a model request. Expected clock dialogue changes happen before the second shadow-only state comparison. The probe compares archive checkpoint, native disposition, transcript, actor/player positions and action jobs, and exercises cancellation through scene/save callbacks and real Talk closure/reopening. Scene and save invalidation are invoked in memory; this is not an actual scene transition or disk save/load test.

Parent built and installed the Unity package and ran `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-9a5f197b -FixtureSaveName 'People - Gothway Garden' -DecisionShadowProbe`. The owned game process 21628 exited after the launcher confirmed `LIVING_CITIES_DECISION_SHADOW_NATIVE_PASS` and `LIVING_CITIES_TOWN_TEST_PASS`. The probe observed two successful callbacks and three separately blocked/cancelled requests. The real production clock submission captured exactly one new request revision and submitted exactly one additional shadow request. The checked archive, disposition, transcript, actor/player positions and action jobs remained unchanged during each isolated shadow interval. This proves no change to those measured state dimensions; it does not claim a full serialized-world comparison.

Installed package SHA-256: `BB77D2ACE1AA4D1C29BEAA2D2EE7DED4932523D9013B9B5626D58702B14C8F1D`. All fourteen frozen source-file hashes still matched after native acceptance. Unity's batch invocation failed its license check; the existing build script's hidden interactive-editor fallback completed successfully. The native log retains unrelated localization/material errors; this is a scoped probe pass, not an error-free whole-mod audit. No model service was started or switched. Read-only post-run checks show Qwen PID 19472 on 8080 and no Mini listener on 8093.

Evidence: `work/decision-shadow-20261020/{source-manifest.json,package-manifest.json,adapter-tests.log,core-tests.log,python-tests.log,runtime-compile.log,build-install.log,unity-build.log,native-run.log,native-player.log,final-services.json}`.

## Remaining gates

This does not provide new D3 semantic scores or prove all current/future NPC actions. The prompt text is preserved, but the native context envelope differs from the earlier offline fixture shape and still needs actual-model evaluation. Real transition/disable coverage, broader actors, frame/latency measurements and the one-action production slice remain open. Existing goal interpretation, offers/cancellation, hypothetical routing, reply ID leakage and navigation-stall findings remain tracked separately. Do not promote a deterministic transport pass into model accuracy or action-execution acceptance.
