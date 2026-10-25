# Gemma conversation boundary evaluation

This is a one-pass semantic regression suite for the production NPC speech-act classifier and its conversation-boundary policy. The 30 English cases are frozen by `fixtures/suite-lock.sha256`. Do not edit the suite after seeing model output or rerun a failed case to seek a better answer. A changed suite needs a new version and a separately reported result.

The evaluator compiles the Core `NpcSpeechActRequest.Create`, `LocalDialogueCompletion.Classify`, `DialogueCompletionText.Extract`, and `ConversationSpeechBoundaryPolicy.Decide` path. It links the mod's actual `LocalSpeechActLabelWire.cs` and `LocalDialogueNativeTemplate.cs`. It uses `/props`, `/apply-template`, `/tokenize`, and `/completion` in that order, with one request per stage and no retries. It never starts a server or model.

Validate the locked suite without HTTP:

```powershell
dotnet run --project tools/ConversationBoundaryEvaluation/ConversationBoundaryEvaluation.csproj -c Release -p:NuGetAudit=false -- --validate-suite tools/ConversationBoundaryEvaluation/fixtures/conversation-boundary-v1.json
```

Run once against an already running, dedicated CPU-only `gemma-4-E4B` llama.cpp server:

```powershell
dotnet run --project tools/ConversationBoundaryEvaluation/ConversationBoundaryEvaluation.csproj -c Release -p:NuGetAudit=false -- tools/ConversationBoundaryEvaluation/fixtures/conversation-boundary-v1.json tools/ConversationBoundaryEvaluation/results/gemma-cpu-20261024-01 http://127.0.0.1:8088
```

The output directory must not exist. `/props` must identify a `gemma-4-E4B` model. If `/props` exposes a GPU layer count, it must be zero. If it does not, `server.json` records that CPU execution must be verified independently from the server process. Every case record contains the raw production prompt, grammar, template/tokenization/completion request and response bodies, SHA-256 hashes, HTTP status and elapsed time, expected labels/action, actual label/action, and pass result. `summary.json` reports all completed cases. A transport or protocol failure stops the run; it is not silently retried.

The expected action is checked against the current compiled production boundary policy before any HTTP call. A case passes only when both the semantic label is allowed and the resulting policy action matches the frozen expected action. `RepairAttempt` controls reopen after a clear refusal; any other act while refusal remains active must end and report, including an ordinary shop question.
