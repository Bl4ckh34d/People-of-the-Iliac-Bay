# Persistent mood behavior evaluation

Corpus contains 12 frozen scenarios with three sequential turns each. It uses native probe identity `s:206728581:h:131074:r:0` and Gothway Garden as its origin. All voice/context variations and the 10:00 AM, 3 Midyear 405 clock are synthetic; the save date is Morning Star. Expected mood-delta ranges are broad review aids. They do not determine mechanical pass/fail.

Run local self-tests without network access:

```powershell
dotnet run --project tools/MoodEvaluation -- self-test
dotnet build tools/MoodEvaluation/MoodEvaluation.csproj --configuration Release
```

Parent-only model run against a local llama.cpp server:

```powershell
dotnet run --project tools/MoodEvaluation -- run --endpoint http://127.0.0.1:8080/completion --output work/mood-evaluation-20261021/run.jsonl
```

Runner sends one request per turn, in corpus order, with no retry. It records model metadata from `/v1/models`, context size from `/props`, each `/tokenize` request, exact fitted completion JSON, raw response envelope, extracted result, Core parser outcome, atomic mood/memory commit, next-turn context, and latency. It flushes one JSONL row after every turn. The final summary separates mechanical failures from diagnostic delta-range differences.
