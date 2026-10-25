# Acute fear and player-contact implementation

This executes the already approved identity-framework requirements: non-player danger produces short-lived fear, player-directed fear recovers over days, and fear recovery never repairs reliability trust. It does not replace Daggerfall stealth or implement the exploratory general sight/hearing roadmap.

## Evidence and design

Native hostile-enemy witnesses already exist in `CivilianResponses.Threat`: living, awake, unprotected resident actors within an unobstructed20-unit ray. Threat identifiers may be persistent enemy LoadIDs or session-only identifiers. Alarm expiry, an unloaded enemy, or registry disappearance does not prove a threat died. Persisting an active-danger Boolean would leave session-key fears stuck after reload.

Persist one acute-fear anchor per known NPC, updated only by fresh native threat observations. It represents perceived recent danger, not an assertion that an unseen enemy remains active or has died. Recover exponentially over in-game hours after the last observation. Repeated observations maintain at least the trait-adjusted observed intensity without adding fear every frame. Bound refresh cadence; allow a stronger observation immediately. Keep the source identifier as last-observation provenance, never as a permanent active-danger dependency. Save the chosen recovery rate; do not reroll personality or recovery during reads.

Use the existing directed-fear math with a separate provisional two-game-hour base half-life and the existing stable sensitivity/resilience/self-esteem/humor modifiers. Keep directed fear's existing day-scale recovery and trust/resentment records unchanged. Acute fear and directed fear are separate facts in model context; generic voice fear may use their maximum. Never blame the player for an unrelated hostile enemy.

A separate pure player-contact projection may amplify the current decayed directed residual while contact is verified. It must not write a relationship, reset its anchor, restore an old peak, or increase trust. Direct conversation is valid contact evidence. General player vision remains a later adapter until concealment, facing, distance and stealth semantics are properly supported; existing narrow ray helpers do not establish that coverage.

## Work packages

1. Core RED: add acute-fear record/view/policy and store contract tests before implementation. Cover hour decay, stable trait differences, repeated observations without accumulation, stronger threats, unknown/dead actors, invalid/backward clocks, defensive copies and malformed/duplicate persisted records. Add minimal nonfunctional stubs only to make missing behavior testable; parent runs the RED cases.
2. Core implementation: persist the record list under world schema41, migrate older saves to an empty list, validate identity and finite/bounded fields, deep-copy on export/restore, preserve saved rates and unrelated relationship/trust state. Parent runs the entire Core suite and resolves actual migration regressions without weakening checks.
3. Native source: feed the existing accepted hostile-threat witness into this store. Preserve the witness/alarm/guard behavior. Reject stale actor/world binding, sleeping/quest-protected/inactive actors and invalid targets. No semantic text filtering and no new model calls. Session-only source IDs may be stored as provenance but never require reconnection to permit recovery.
4. Context and voice: capture a read-only acute view at current game time, render its recent-observation provenance separately from interpersonal fear, and include it in current-context freshness. Voice fear may use the maximum of acute and directed contact fear. Add no automatic trust gain or relationship write.
5. Player-contact projection: test the pure nonmutating policy across days of recovery, repeated contact, absence and saved traits. Wire only verified direct conversation contact in this slice. Keep ordinary proximity/vision explicitly open.
6. Acceptance: parent owns builds, model requests, game and service changes serially. Run meaningful Core tests, focused actual-model context cases with first raw outputs retained, RuntimeCompile/Unity and a guarded native disposable-fixture probe for witness receipt, supplied-time projection and persistence. Distinguish controlled native source tests from real combat, and supplied-time projection from hours of live simulation. Preserve failed first attempts. Review, update roadmap/evidence/Obsidian and verify a fresh recovery copy.

## Ownership

Core worker owns new acute-fear Core/test files and the necessary schema/copy wiring in `WorldState.cs`; parent owns test registration and actual runs. A separate worker may own only the pure contact-projection files/tests. Parent owns native integration, context/voice consumers, test hooks and shared build/test execution. All workers use Luna/high or lower. No source mutation during a parent build or recovery copy.

## Current limits

Implemented and installed through checkpoint136. Core1580/1580, adapter79/79, clean RuntimeCompile/Unity, isolated native actor persistence and real Talk context/stage invalidation pass. Eight first actual-model context checks are preserved with their unsupported promises and synthetic fallback-mood confound. See [checkpoint evidence and remaining acceptance](../../2026-10-21-acute-fear-and-contact.md). The bounded work packages are complete; the general perception and real combat limitations below remain separate roadmap work.

Two hours is a provisional calibration, not an empirically fitted psychological model. Loss of observation permits calming without asserting objective safety. This work does not introduce a global perception system, combat companions, generated voice samples, active-threat death tracking, or new model placement. These remain separately tracked requirements.
