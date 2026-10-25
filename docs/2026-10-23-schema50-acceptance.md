# Schema 50 poison source admission

Status: reviewed, built, locally installed and accepted by the owned native poison probe. This is a checkpoint toward the full roadmap, not completion of inbox retention or the mod.

## Changes

Poison observations now require a one-use native assignment ticket tied to the store, restore epoch, resident, binding owner and exact active bundle. Disable/removal revokes tickets; rebind baselines existing effects. The old generic capture API rejects new claims.

Each person retains issued/projected sequence counters, one exact pending onset, cumulative omitted-event count/time bounds and an exact unprojected omission count. This preserves source identity independently of retained inbox detail. Exact accounting rejects a lost pending event even when an older historical gap exists. A later exact pending event remains valid after earlier omissions. Sequence exhaustion records degradation without reusing an identity. Gaps do not invent mood effects or responsible actors.

Migration preserves existing detail and derives legacy cursors only from validated canonical poison receipts. Runtime maintenance retries saved pending events with a bounded candidate count. Inbox retirement itself remains disabled.

## Verified evidence

- Focused source admission: 11/11 passed, `work/life-core-20261023/temp/schema50-poison-final.log`.
- Main Core suite: 1695/1695 passed, `work/life-core-20261023/temp/schema50-core-final.log`.
- RuntimeCompile: zero warnings/errors, `work/life-core-20261023/temp/schema50-runtime-final.log`.
- Independent review checked source accounting, atomic publication, migration, ticket revocation and native binding. The discovered pending-loss cases were corrected before packaging.
- All 13 owned baseline save files match the earlier baseline hash manifest before packaging.
- The last accepted schema49 bundle was preserved at `work/life-core-20261023/schema49-accepted.dfmod`, SHA256 `FBB35EAE1B06E9ECF3063ACDBCB29C253F169910A93229877C8D66CAAFAB1D37`.

## Native acceptance

Unity build/install exited0 (`work/life-core-20261023/schema50-package-build.log`). Dist and installed bundle SHA256 both equal `54DED4A21C27C3AE6C4B20E0A865331D2F2EEB02CD41D2A72C7CDB7D153ECFF1`. All 666 top-level Core/runtime source files match the deployed Unity source by hash. The portrait cache reused 20 entries and 2160 textures.

The first native attempt stopped before launching the game because the harness unnecessarily required dialogue settings for the model-free poison probe (`schema50-poison-native.log`). The harness now excludes that probe from dialogue-setting prerequisites, like the healthcare probe; user settings were not changed. PowerShell parsing passed.

The corrected run exited0 (`work/life-core-20261023/schema50-poison-native-retry.log`; archived game output `schema50-poison-player.log`). It observed three actual native Nux Vomica assignments with sequence1..3, retained history across streaming and disk reload, rejected stale epoch, ignored baseline/incumbent replay and restored the named baseline. All 13 baseline save-file hashes remained unchanged after the run.

The required projection marker confirmed cursor3 survives detached detail retirement and the actual DFU FullSerializer. A controlled detached pending state derived from the last real source projected exactly once. This does not claim a live queue-pressure failure, combat-inflicted poison, symptom simulation, active-effect persistence across streaming, or crash-atomic capture between the engine and Core.

Speech-source replay remains a separate confirmed regression awaiting schema51 work. No new model or voice quality claim is made by this checkpoint.
