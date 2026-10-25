# NPC Architecture Evaluation Harness

## Native-context route replay

`run_native_route_replay.py` evaluates the exact native router envelope with frozen synthetic conversation/state overrides. It evaluates route classification only; it never plans, accepts or executes actions. The [20 October report](../../docs/superpowers/specs/2026-10-20-native-route-replay-evaluation.md) records 640 calls and the negative held-out result of state-based route masking.

With the explicit owned fixture available, capture exact payloads through the native deterministic probe:

```powershell
tools/TownTest.ps1 -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b `
  -FixtureSaveName 'People - Gothway Garden' -DecisionShadowProbe `
  -DecisionShadowCaptureDirectory 'C:\absolute\fresh-capture-directory'
```

The directory path must be absolute. Existing capture filenames are refused, and actual writes use CreateNew. No model is contacted by this probe. The v2 capture includes `first-request.json`, `clock-request.json`, byte/hash sidecars and `route-vocabulary.json`. Without the capture flag no raw request file is written.

After verifying the game is closed and the intended local services are ready, use their exact `/v1/models` IDs:

```powershell
python tools/NpcArchitectureEval/run_native_route_replay.py `
  --template 'C:\absolute\fresh-capture-directory\first-request.json' `
  --route-vocabulary 'C:\absolute\fresh-capture-directory\route-vocabulary.json' `
  --suite tools/NpcArchitectureEval/fixtures/native-route-heldout-v2.json `
  --run-dir work/new-native-route-run `
  --expected-model '<exact D3 model ID>' `
  --qwen-url http://127.0.0.1:8080 --expected-qwen-model '<exact Qwen model ID>'
```

V1 templates retain their historical four-route menu and omit `--route-vocabulary`; v2 menus always include SOCIAL/ACTION_REQUEST and add offer/commitment routes only from eligible typed state. Both option orders run. Qwen is optional. Run directories must be fresh; payload/source/template/suite/vocabulary hashes are frozen before inference. No service is started, stopped or switched. The default ten-second request ceiling matches the maximum configurable native budget, not its five-second default; latency is retained for both thresholds. An explicit `--diagnostic-timeout` can retain slower diagnostic output, but late responses remain marked over budget. This harness does not establish Unity frame performance or general model accuracy.

Offline, offered-choice comparison for frozen synthetic scenarios. The choice runner does not launch the game or execute native actions. Separate CoreBridge modes exercise actual Core ledgers with explicit synthetic observations/receipts; they do not verify native game effects. Each model receives only the whitelisted current input, and exact loopback HTTP attempts/results are recorded.

By default, the two checked-in base fixture files (`social-world.json` and `action-combat.json`) are the run inputs, preserving the frozen 72-case comparison. `coverage-gaps.json` is an optional 22-case extension and is never added to that default. Supply one or more explicit `--suite PATH` arguments to run only those suites or combine them; relative paths resolve from the repository root. Each run validates selected fixtures, records their hashes plus code hashes in `manifest.json` before inference, and refuses an already-existing run directory. `attempts.jsonl` has one durable attempt and result record per HTTP call; `results.jsonl`, `summary.json`, and `skipped.json` preserve decisions, scores, and chain-aborted turns. Failed or unoffered choices do not become later-turn history and are not retried.

The only supported endpoints are unauthenticated HTTP on `localhost` or a loopback IP. The Windows game-process guard must establish Daggerfall Unity is closed before evaluation. Keep both servers single-slot and idle; do not run other inference clients during a comparison. The runner records discovered model IDs instead of requiring particular model names.

From the repository root, run deterministic unit tests without contacting either model server:

```powershell
python -m unittest discover -s tools/NpcArchitectureEval/tests -v
```

After explicitly starting the intended local servers and verifying the game is closed, launch a fresh run with both endpoints and a new output path:

```powershell
python tools/NpcArchitectureEval/run_evaluation.py `
  --run-dir work/npc-capability-eval-20261020/run-portable-v1 `
  --models d3 qwen `
  --d3-url http://127.0.0.1:8093 `
  --qwen-url http://127.0.0.1:8080
```

Run only D3 (for example, when testing another D3 model without loading Qwen) by selecting it and supplying only its endpoint:

```powershell
python tools/NpcArchitectureEval/run_evaluation.py `
  --run-dir work/npc-capability-eval-20261020/run-d3-v1 `
  --models d3 `
  --d3-url http://127.0.0.1:8093
```

Run the extension cases through the same two adapters without changing the base 72-case fixture set:

```powershell
python tools/NpcArchitectureEval/run_evaluation.py `
  --run-dir work/npc-capability-eval-20261020/run-coverage-gaps-v1 `
  --models d3 qwen `
  --d3-url http://127.0.0.1:8093 `
  --qwen-url http://127.0.0.1:8080 `
  --suite tools/NpcArchitectureEval/fixtures/coverage-gaps.json
```

If launched outside the repository root, use the absolute path to `run_evaluation.py`; a relative `--run-dir` still resolves from the repository root. Never reuse a run directory. A failed run leaves its records intact; choose a new directory for any later run.

Metrics describe offered-choice agreement on these synthetic fixtures only. D3 native choice criteria and Qwen strict JSON use different format adapters; multi-turn order arms can diverge through actual choices and histories. Latency includes local warm-up/cache and scheduling effects. Results do not establish agent quality, free-form planning ability, willingness, native binding correctness, action execution, or game-world outcomes. Keep those claims to separate contract and in-game tests.

## Image-choice smoke test

`run_visual_evaluation.py` runs a separate, D3-only image-choice test with one question per image in original and reversed criteria order. It reads the image file exactly as supplied, converts those original bytes to a `data:` URL, and records source-file hashes before calls. It excludes `expected` and `provenance` from the request. The expected value is only used in saved results and summary. It does not capture game screenshots or validate NPC-eye combat perception.

Create a JSON suite beside your own images; do not put copyrighted game screenshots or other private images into the repository. Example schema for two user-supplied stills:

```json
{
  "cases": [
    {
      "id": "still-outdoor",
      "imagePath": "images/outdoor.png",
      "state": "Classify only the scene visible in the supplied image. No hidden game state is available.",
      "question": "Which visible scene category best matches the image?",
      "criteria": {
        "outside_snow": "Open outdoor space with visible snow",
        "inside_room": "Enclosed furnished indoor room",
        "unclear": "Cannot determine from the image"
      },
      "expected": ["outside_snow"],
      "provenance": "User-supplied still; hand-labeled for smoke testing."
    },
    {
      "id": "still-indoor",
      "imagePath": "images/indoor.png",
      "state": "Classify only the scene visible in the supplied image. No hidden game state is available.",
      "question": "Which visible scene category best matches the image?",
      "criteria": {
        "outside_snow": "Open outdoor space with visible snow",
        "inside_room": "Enclosed furnished indoor room",
        "unclear": "Cannot determine from the image"
      },
      "expected": ["inside_room"],
      "provenance": "User-supplied still; hand-labeled for smoke testing."
    }
  ]
}
```

Run with an unused output directory and the explicit local endpoint:

```powershell
python tools/NpcArchitectureEval/run_visual_evaluation.py `
  --suite path\to\your-visual-suite.json `
  --run-dir work/npc-capability-eval-20261020/run-visual-v1 `
  --d3-url http://127.0.0.1:8093
```

Relative suite paths resolve from the repository root; each `imagePath` resolves relative to its suite file. The manifest hashes the suite, runner, client, and source images. Attempts preserve request/response bytes through the shared loopback-only ledger. One technical failure stays recorded and is never rerun automatically. These results can test image transport, visible-scene classification, and option-order sensitivity only; they do not show that an NPC sees the world through this input, understands combat, or chooses safe combat actions.

## Actual-Core own-inventory tool pilot

`run_core_tool_pilot.py` is a bounded tool-use smoke test for one synthetic fixture participant. D3 chooses whether to call the offered read-only `read_own_inventory` tool; Qwen generates a short reply from the returned facts. The task itself is scripted: Python supplies the Purchase-then-Transfer proposal and applies explicit synthetic receipt values. The models do not plan or choose those actions. The pilot checks three checkpoints (before purchase, after purchase, and after restore/handover) against the fixture's actual Core inventory reader.

This pilot does not run the game or native executor. Consent, seller/recipient identities, stock, route arrival, purchase/handover receipts, and inventory state belong to the synthetic Core fixture. “Core-valid” or “facts correct” does not establish NPC willingness, live-world target existence, player inventory/global inventory correctness, native effects, combat behavior, or free-form goal planning. It exercises one buyer inventory only.

Build the bridge from the repository root:

```powershell
dotnet build tools/NpcArchitectureEval/CoreBridge/CoreBridge.csproj
```

With Daggerfall Unity closed and the intended local model servers already available on the loopback defaults (D3 on port 8093 and Qwen on port 8080), run to a new directory:

```powershell
python tools/NpcArchitectureEval/run_core_tool_pilot.py `
  --run-dir work/npc-capability-eval-20261020/core-tool-pilot-v1
```

Optional `--d3-url` and `--qwen-url` values use the same unauthenticated loopback-only HTTP validation as the evaluation runner. The script validates both URLs, checks that the game is closed, and refuses an existing output path before contacting model endpoints. It does not start, stop, or manage servers. The manifest records runner, model-client, CoreBridge, and `PeopleOfTheIliacBay.Core.dll` hashes plus discovered model IDs. Core commands and responses are correlated by request ID in `core-events.jsonl`; model attempts are single-shot and ledgered in `model-attempts.jsonl`. Results are kept in `results.json`, and the final synthetic Core snapshot is in `final-state.json`.

Packaging checks (no server calls or Core launch):

```powershell
python -m unittest tools.NpcArchitectureEval.tests.test_core_tool_pilot -v
```

## Action-specific goal protocol and causal evaluation

`goal_protocol.py` is the versioned v3 offline goal contract. Wait/Follow expose only duration arguments; MoveTo/Interact expose only a candidate reference; Purchase/Transfer expose quantity. Models never supply native item-source indices. The deterministic fixture binder derives a handover dependency from a unique earlier purchase of the exact item and quantity. Ambiguous sources, repeated handovers, unsupported quantities, incomplete targets, unavailable exact-quantity quotes, unsupported player funding and unresolved conditions do not produce plans. Outgoing person transfers are the only inventory direction covered by this protocol. An authored `ownedItemObserved` fixture marker is not a live inventory observation or freshness guarantee.

`run_goal_evaluation.py` exercises event-first D3 routing, optional Qwen interpretation, this binder and the prebuilt actual Core task validator. Each `--suite` argument includes a separately supplied cases-array SHA-256; the fixture's declared hash must match. Original and reversed route orders are counterbalanced. Inputs whitelist the snapshot and current player text; expected results remain scoring-only. The manifest freezes scripts, corpora and both Core assemblies before inference. Existing run paths are refused, the game must be closed, endpoints must be loopback HTTP and single-slot, and calls are never retried automatically.

```powershell
dotnet build tools/NpcArchitectureEval/CoreBridge/CoreBridge.csproj
python tools/NpcArchitectureEval/run_goal_evaluation.py `
  --run-dir work/goal-v3-new-run `
  --suite tools/NpcArchitectureEval/fixtures/goal-wiring-v3.json 80ae13c25e29b9976639169f87904b1a3928cb12bb12d04aa178ba068456d0c8
```

The bundled twenty-root suite includes supported requests, ordered duration/commerce steps, live offers/cancellation, reported or hypothetical requests, negation, missing targets and excessive quantities. It is a diagnostic sample, not exhaustive capability coverage. A skipped interpreter is recorded as unobserved; the social route can infer no action without fabricating an interpreter response. Strict stage agreement and semantic false proposals are separate metrics. Core acceptance checks structure, consent-path rejection and duplicate protection; it does not prove player meaning, NPC willingness, physical feasibility or effects. Raw per-proposal Core results, errors and missing result IDs must be inspected alongside summary counts.

The current prompt revision is `goal-v3.1`. The shortened v3 prompt regressed despite correct schema output; a controlled prompt/schema diagnostic and the retained failed runs are documented in [the causal evaluation report](../../docs/superpowers/specs/2026-10-20-causal-goal-protocol-evaluation.md).

`run_causal_goal_pilot.py` connects model-selected own-inventory/stock reads, language-generated Purchase→Transfer steps, post-plan decisions, actual Core acceptance and explicit synthetic receipts. Every scenario/order has a new disposable Core session. It does not replace a bad proposal with a scripted gold task. The fixture adapter supports exactly one observed shop item and one NPC recipient, with snapshot restore between purchase and handover; it refuses other proposals. Core consent and physical receipts remain explicit simulator inputs. Structured reply fields are checked against final Core state; natural dialogue quality requires separate review.

```powershell
python tools/NpcArchitectureEval/run_causal_goal_pilot.py `
  --run-dir work/causal-goal-new-run
```

Both local model services must already be running, the game must be closed, and the output path must be unused. The pilot never manages model/game processes. All failures and abstentions remain in `results.jsonl`; count successful task completion separately from harness completion. The fixed four-scenario sample is not a broad causal capability suite.
