# Weather relevance evaluation

This six-turn, English evaluation exercises production `LocalDialogueCompletion.Build` and the linked `LocalDialogueNativeTemplate`. It records model replies and context for human review. It does not apply keyword rules or assign automatic semantic pass/fail scores.

The frozen cases cover a greeting, work question, name question, direct weather question, apology after harassment with saved negative disposition, and an opinion/name follow-up after the NPC already mentioned snow. Every case supplies verified snow context.

Validate the fixture and production prompt/template path without contacting a server:

```powershell
dotnet run --project tools/WeatherRelevanceEvaluation/WeatherRelevanceEvaluation.csproj -c Release -p:NuGetAudit=false -- --validate-suite tools/WeatherRelevanceEvaluation/fixtures/weather-relevance-v1.json
```

Run one pass against an already running local llama.cpp server. The default requires CPU-only execution:

```powershell
dotnet run --project tools/WeatherRelevanceEvaluation/WeatherRelevanceEvaluation.csproj -c Release -p:NuGetAudit=false -- tools/WeatherRelevanceEvaluation/fixtures/weather-relevance-v1.json tools/WeatherRelevanceEvaluation/results/gemma-cpu-weather-20261024-01 http://127.0.0.1:8088
```

The evaluator never starts a server. It only accepts a loopback HTTP origin and checks `/props` before sending cases. In the default mode, any exposed nonzero GPU-layer count stops the run. If `/props` omits GPU-layer counts, the output records that the server process needs independent device verification. The output directory must be new.

GPU execution is rejected unless explicitly opted into with a trailing `--allow-gpu`. For example, append the flag to the command above when GPU execution is permitted for that evaluation. The flag may also be used without an endpoint to keep the default `http://127.0.0.1:8088`:

```powershell
dotnet run --project tools/WeatherRelevanceEvaluation/WeatherRelevanceEvaluation.csproj -c Release -p:NuGetAudit=false -- tools/WeatherRelevanceEvaluation/fixtures/weather-relevance-v1.json tools/WeatherRelevanceEvaluation/results/gemma-gpu-weather-20261024-01 http://127.0.0.1:8088 --allow-gpu
```

`manifest.json`, `server.json`, and `summary.json` record whether GPU layers were permitted. `server.json` and `summary.json` also record every GPU-layer field exposed by `/props`; if none are exposed, they say the server process needs independent verification. This flag changes only the evaluator's device gate and does not start or reconfigure the server.

Each case record preserves the Core prompt and payload, raw `/apply-template`, `/tokenize`, and `/completion` requests and responses, timing, token count, and extracted reply. The console prints each reply. There are no retries; a transport or protocol failure stops later cases. Review each reply against `reviewFocus` in its record. Do not edit the frozen fixture after seeing model output; use a new suite version for changes.
