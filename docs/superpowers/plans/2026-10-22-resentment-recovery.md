# Independent resentment recovery

The approved identity framework requires resentment to fade with uneventful game
time, more slowly than fear. This bounded slice extends the existing saved
relationship projection; it does not alter trust, friendship, event attribution,
model prompts, defaults or native action authority.

## Design

- Add an independent saved resentment anchor, half-life and fractional remainder.
  Use a provisional14-game-day base half-life, double the existing7-day fear
  calibration, with the same stable personality/severity modifiers. Store the
  chosen rate when an event establishes it; reads never reroll or re-anchor it.
- Timed detached views project both fear and resentment. Untimed raw views stay
  explicit for persistence and fingerprints. Keep the existing fear-only API
  compatible and move current time-aware consumers to the combined projection.
- Apply signed resentment changes against the appropriately aged baseline.
  Positive contributions must not revive previously cooled resentment or erase
  the age of unrelated old evidence. Signed reductions preserve fractional
  accuracy. Trust-only/fear-only events cannot restart resentment's clock;
  resentment-only events cannot restart fear or heal trust.
- Advance world schema41 to42 and directed relationship/ledger format versions
  as required. Preserve existing fear recovery exactly. Legacy resentment with
  no trustworthy timing remains unanchored; do not invent historical dates or
  silently erase old grievances. A subsequent real nonzero resentment event may
  establish a conservative forward clock for that aggregate.
- Keep evidence history, identity, exactly-once receipts, revision/CAS guards,
  save-branch isolation and current native disposition ownership unchanged.

## Work and verification

1. Core worker writes meaningful failing tests first and freezes for parent RED.
   Cover stable personality rates, faster fear, timed-read purity, signed/delayed
   updates, fractional values, unchanged trust/other clocks, replay and legacy
   schema40/41 migration. Parent exclusively runs builds/tests.
2. Implement Core policy, row/view/ledger persistence, timed store/appraisal
   projections and schema migration. Preserve existing public APIs where used.
3. Runtime worker audits consumer reads and prepares an opt-in owned native
   persistence/context probe. Use supplied game times and native serialization;
   never claim that supplied time is actual days of gameplay. No new audio.
4. Parent reviews sources, runs the full Core and relevant adapter checks,
   builds/deploys and executes owned native acceptance. Verify saved rates and
   fresh conversation/TTS context, stale views and restore. Preserve failures.
5. Update canonical scope/status, Obsidian and a verified source/save/evidence
   recovery copy. Broad roadmap goal remains active.

Workers own disjoint files and freeze before parent builds. No model calls,
test runs, services, game sessions, commit or cleanup by workers.

## Verified result

Checkpoint148c implements this bounded slice. Core1589/1589, adapter84/84,
factor self-test, RuntimeCompile, Unity742 and owned native cold-Talk/serializer
acceptance pass. See [implementation and limits](../../2026-10-22-resentment-recovery.md).
Sixteen days are supplied detached-store times, not elapsed gameplay. Preserved
native failures identified serializer timestamp precision in the probe. The
broader roadmap and model behavior work remain open.
