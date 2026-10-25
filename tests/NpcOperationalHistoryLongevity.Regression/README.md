# NPC operational history longevity regression

```powershell
dotnet restore tests/NpcOperationalHistoryLongevity.Regression/NpcOperationalHistoryLongevity.Regression.csproj -p:NuGetAudit=false --ignore-failed-sources
dotnet run --project tests/NpcOperationalHistoryLongevity.Regression/NpcOperationalHistoryLongevity.Regression.csproj --no-restore --configuration Release -- --full
```

The full mode advances more than five years of native game seconds. It admits and completes 10,001 conversation tasks, dispatches and reconciles one real task-step action per task, and admits and completes nine standalone conversation actions per cycle: 100,010 actions total. Every cycle performs one bounded maintenance call. Checkpoints assert that detailed ledgers remain below the former 1,000-task and 10,000-action lifetime caps; the final save must have retired the first task and standalone action. Restore and sibling-branch checks exercise source replay isolation. A separate case protects an active task-step pair and an undelivered failure receipt.

Without `--full`, the same transitions run for 100 cycles to diagnose contract failures quickly. The full mode is the longevity acceptance test; the short mode alone does not establish the capacity claim.

The full run prints total elapsed time, longest and cumulative maintenance duration, and peak retained detail. It also samples the runtime-sized four-row, one-group call at a representative steady-state ledger and reports a call that actually publishes a prune. These measurements are Core-only diagnostics; they do not establish smooth native frame pacing or multiplayer scale.
