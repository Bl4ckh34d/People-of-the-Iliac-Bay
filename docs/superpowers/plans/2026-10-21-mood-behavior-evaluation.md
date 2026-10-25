# Persistent Mood Behavior Evaluation Plan

**Goal:** Evaluate 12 frozen three-turn scenarios through production Core prompt, response, and mood transaction code.

**Design:** A single .NET console project reads a frozen scenario corpus, uses `LocalDialogueCompletion.Build`, production `/props` and `/tokenize` budget fitting, sends turns sequentially to a supplied loopback llama.cpp endpoint, and flushes one JSONL record per response. Replay uses `DialogueCompletionText.Extract`, `SocialDialogueResult.TryParse`, and source-specific `WorldStateStore` mood commits. Offline `self-test` mode makes no HTTP calls.

**Constraints:** 12 scenarios × 3 turns; no retries; no language regex or keyword scoring; semantic delta ranges are diagnostic only. This implementation will not run a model. Parent runs the command and reviews captured evidence.

**Files:** `tools/MoodEvaluation/MoodEvaluation.csproj`, `Program.cs`, `fixtures/scenarios-v1.json`, and `README.md`.

**Verification:** Run `dotnet run --project tools/MoodEvaluation -- self-test` and `dotnet build tools/MoodEvaluation/MoodEvaluation.csproj -c Release`. Parent-only model run: `dotnet run --project tools/MoodEvaluation -- run --endpoint http://127.0.0.1:8080/completion --output work/mood-evaluation-20261021/run.jsonl`.
