# Decision adapter tests

These tests link the actual runtime wire/session/HTTP transport files with the real Core assembly. They run on .NET 8 with C# 7.3 source compatibility and require no Daggerfall installation, GPU or model process. Real HTTP cases use disposable loopback TCP peers only. They do not execute Unity's MonoBehaviour lifecycle.

```powershell
dotnet run --project tests/DecisionAdapter.Tests --configuration Release
```

The default run never starts a model server, contacts an existing model endpoint or changes game data. Failure/cancellation, binding freshness, strict JSON, model identity, probability vectors, response limits, redirects, absolute deadlines and bounded callback delivery have dedicated cases. The root CI configuration includes this command; local validation does not claim that the remote CI job has run.

An optional explicit wire smoke can contact an already running decision service:

```powershell
dotnet run --project tests/DecisionAdapter.Tests -- --live http://127.0.0.1:8093 "EXACT_MODEL_ID_REPORTED_BY_THE_CONFIGURED_SERVICE"
```

Replace the final argument with the configured exact model ID. The smoke neither discovers nor starts/stops that service, and it requires a literal unauthenticated loopback HTTP address. It supplies synthetic context, reports typed status/hash/model/callback metadata, and scores no NPC meaning or game effect. Run it only in a deliberately prepared local test environment. The normal tests remain independent of this optional mode.

See [the boundary evidence](../../docs/superpowers/specs/2026-10-20-decision-service-boundary.md) for the current verified revision and the remaining Unity/shadow integration gates.
