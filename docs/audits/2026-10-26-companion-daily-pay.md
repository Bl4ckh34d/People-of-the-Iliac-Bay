# Companion daily pay — 26 October 2026

## Implemented rule

The existing paid Follow offer still requires NPC willingness and a native player Yes/No confirmation. It quotes exactly 120 gold for one 24-game-hour block starting at the accepted action time. It does not renew automatically. Dismissal ends the service immediately. Once the day starts, its whole fee is earned; dismissal, withdrawal, health-care suspension and expiry do not create a partial-day refund.

The existing native funding and settlement adapters remain the currency authority. Funding debits the player's actual purse, and settlement credits the NPC's saved integer-gold wallet through the existing combined ledger/evidence/inventory commit. This change does not claim that earned gold is paid into the NPC wallet at the instant of hiring: the existing payout still occurs at settlement.

The generic ledger accepts exactly one daily rate per funding receipt. It rejects multi-day advance funding. Its existing explicit renewal permission can fund the next day only at the exact previous block end; native companion hiring sets that permission to false. Calendar midnight and repeated observations cannot buy additional days.

## Failed admission

Previously, the native hire helper committed service start before attempting to bind the accepted Follow action. A subsequent binding failure could therefore turn a failed hire into a started, chargeable day.

`WorldStateStore.TryBindPaidFollowAction` now prepares the started contract and the bound action without publishing either. It validates both before the ledger commit, then assigns the already-validated action synchronously. The native helper no longer starts the contract separately. Any admission failure before this successful binding leaves a reserved contract with zero earned gold, eligible for the existing full native refund.

The exact service-time counters remain available for existing pause, expiry and receipt validation. They no longer calculate wages. Obsolete partial-pay development contract states are not migrated.

## Verification

Observed red tests before production edits:

- Fixed daily fee, fractional payout refusal and related service tests fail against the former per-second accrual.
- Core suite reports 2143/2144, with `Funded reservation starts only while binding its own accepted Follow` failing because the former adapter rejected reserved contracts.

Green results after the correction:

- `dotnet run --project tests/NpcServiceContract.Tests/NpcServiceContract.Tests.csproj --no-restore`: 22/22.
- `dotnet run --project tests/NpcServiceHealthPause.Regression/NpcServiceHealthPause.Regression.csproj --no-restore`: 9/9.
- `dotnet run --project tests/NpcServiceHealthCareStore.Regression/NpcServiceHealthCareStore.Regression.csproj --no-restore`: 10/10.
- `dotnet run --project tests/NpcServiceSettlement.Regression/NpcServiceSettlement.Regression.csproj --no-restore`: 8/8.
- `dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj --no-restore`: 2144/2144 in the shared source checkpoint.
- `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore`: zero warnings and errors.

Coverage includes start-relative expiry across midnight, full early-dismissal earnings, partial-payment refusal, no duplicate payout, unsupported multi-day funding, pre-service full refund, health pause, save/restore, stale/foreign/replayed settlement evidence and atomic NPC wallet credit.

`NpcServiceSettlementNativeProbe` now expects 120 gold of NPC earnings and no player refund for a started day. It retains exact player-purse invariance, controlled commit refusal, stale/foreign/replayed evidence and FullSerializer checks. The obsolete partial-refund overflow branch was removed because that settlement no longer credits the player. This native fixture was compiled but not run.

## Remaining acceptance

No native hire, dismissal, failed-hire refund, expiry, public-door travel or exterior travel was executed for this change. Native confirmation text, actual currency changes and continuation after save/load still need game acceptance. No party-management UI, multi-day purchase, combat recruitment or new movement system is added here.
