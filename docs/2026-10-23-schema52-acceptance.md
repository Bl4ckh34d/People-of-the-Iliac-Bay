# Schema 52: service health interruption and settlement

## Later native paid-care acceptance

The strengthened probe package is now installed with matching dist/player SHA256 `912A5C6C2185C30AA7ADB35D682A33E6836F9675EE37852A1509DB20A46DDAA5`. Production Core/runtime logic is unchanged from the first checkpoint below; the probe and harness now require ordinary runtime service interruption and resumption as well as isolated settlement.

The actual treated resident receives a controlled contract through real player funding and a saved Active transition. Ordinary healthcare pauses it before potion execution, preserves its billing counters through alarm and repeated treatment, keeps it paused during an explicitly observed `Recovered`/`ReturningToRoutine` action, and resumes only after physical return without billing paused time. The probe never directly calls Suspend or Resume. Original player coins/LOC and the exact fixture save are restored; all 13 baseline hashes remain unchanged. Harness exits 0. Runtime compilation has zero warnings/errors; independent source review preceded the build.

Evidence: `work/life-core-20261023/schema52-paid-care-{build,runtime,native}.log`, `schema52-paid-care-accepted-{town,player}.log`, and `schema52-paid-care-accepted.dfmod`. This proves the funded health interruption/recovery path; it still does not prove player-negotiated recruitment, normal service movement, renewal or estate settlement.

## Initial native settlement checkpoint

The reviewed package was built, installed and natively tested on 23 October 2026. Dist and installed bundle SHA256 both equal `9753B38CF9A69A8599165A62488BF2417AB5FEB4E2D26AB36BDC5DE92552E1C1`. The accepted bundle is preserved at `work/life-core-20261023/schema52-accepted.dfmod`.

## Delivered source

Paid service can pause for urgent feasible care before ordinary work is cancelled. Negotiated paid rest remains billable within the existing treatment threshold. An admitted episode can continue treatment after partial recovery; switching from paid rest to another treatment pauses service. A second injury can transfer the unpaid pause to its current care episode. Resume requires full health and completed physical return. Late expiry uses actual native transfer time and capped service accrual.

The settlement adapter credits the NPC wallet together with contract/evidence publication, refunds actual player coins, and rolls back native currency on a rejected Core commit. Schema 52 persists treatment admission and rejects an impossible admission marker on a fresh revision-one episode. Legacy episodes default conservatively.

## Evidence

- Core: 1695/1695; focused settlement 8, pause 9, service/care store 10, atomic care admission 10, arbitration 18. Logs: `work/life-core-20261023/schema52-*.log`.
- Runtime compilation: zero warnings/errors. Independent source review completed before Unity build. Build log: `schema52-package-build.log`; portrait cache reused 20 entries and 2160 unique textures.
- Native settlement fixture: real mixed coin/LOC funding, health-owned half-period pause, 60-gold NPC earnings, 60-gold player refund, zero escrow, forced post-refund rejection with exact rollback, FullSerializer roundtrip and stale/replay/overflow/death rejection. Original player currency and native clock restored.
- Native ordinary healthcare: actual potion effects and one-unit consumption, rollback/replay protection, real hostile alarm interrupts care, native flight, resumed treatment after alarm expiry, full recovery, released treatment ownership, physical return and no post-recovery consumption.
- Framework persistence passes at schema 52. Exact original test save reloaded; all 13 baseline file hashes unchanged.
- Native harness exited 0. Archived logs: `schema52-accepted-town.log`, `schema52-accepted-player.log`, `schema52-native-healthcare.log` in the same work directory.

## Limits and remaining work

The native settlement fixture is controlled and disposable. It does not prove player recruitment, the complete ordinary paid-companion interruption/resume journey, or every lodging/location combination. Those require additional gameplay acceptance. Death/estate payouts and renewal transactions remain open. The rollback-failure retry latch is runtime-only; separate DFU/mod save files are not crash-atomic. The broader shared core and ROADMAP remain incomplete; autobiographical compaction and STT remain excluded.
