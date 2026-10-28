# Single-model willingness contract tests

This project links the production willingness proposal, prompt and bounded choice parser. It verifies detached ordered proposals, identity/resource bindings, exact durations and quantities, Unicode handling, prompt data isolation, and consent choices. It does not contact a model, start a service, or modify game data.

```powershell
dotnet run --project tests/DecisionAdapter.Tests
```

The second-model HTTP/session/shadow tests were removed with that backend. Same-model foreground/background cancellation remains covered by `tests/SingleModelGoal.Regression` and native conversation lifecycle probes. Goal sequence probes now use the configured dialogue model for willingness. Native movement, real model quality, TTS and stale callbacks through Unity still require an explicitly owned game run.
