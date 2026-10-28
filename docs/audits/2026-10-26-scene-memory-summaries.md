# Compact recall of actual NPC experiences

Source-only implementation, 26 October 2026. No Unity build, Player run, or local model request was performed by this assignment. Native acceptance remains open.

## Scope and behavior

The existing `ConversationMemoryCompaction` remains an extractive selection cache over archived dialogue. It stores selected original conversation rows, rather than summaries of native scene experiences. This change adds a small deterministic projection to the existing `RecallSceneEvents` path. It does not add an archive, world queue, model family, or model call.

At an actual NPC recall, the store examines that observer's existing maximum of32 raw scene memories. A still-retained event with salience at least3 and an age of at least one native day can enter a persisted summary. Each summary stores one original observation text and common kind, affected identity, known responsible identity, settlement/building, and signed valuation. Only exactly matching facts merge. Every retained occurrence keeps its original event ID and native timestamp. Different victims, places, responsibility knowledge, valuations and observation text remain separate.

The limit is16 summary groups per observer, with up to8 sources per group. Stronger and newer groups take priority; repeated facts keep their newer sources. These are bounds, not a promise to retain every event. No summary broadens a fact into a causal inference or general personality claim. An observed report remains a report; player conversation is never promoted to a witnessed event.

Every source uses the existing `NpcSceneMemory.ExpiresAt`, including its stable random retention and signed-valuation salience. No new minimum lifetime is introduced. A source disappears at its own boundary, even when another source in its group remains. Repeated recall does not move event time or renew expiry. Short neutral company events remain outside this projection and expire normally.

Summary text receives at most half the scene-recall budget. The own-memory tool gives scene recollections at most half its total budget, leaving room for queried conversation. Existing current activity context remains separate. Raw observations covered by an emitted summary are not printed twice. If a summary cannot fit, eligible raw evidence remains available. An empty scene result emits no heading by itself.

At the existing32-record raw admission limit, a raw copy already preserved exactly in a summary can be replaced before rejecting a newly observed event. This prevents old trauma copies from excluding today's player trade. Late native responsible-actor refinement preserves the original event time, even after raw eviction. Accepted appraisals use the existing atomic scene-memory candidate to return a retained source for reprojection; lowered salience retires an obsolete summary rather than retaining an earlier high valuation.

## Persistence and runtime bounds

`WorldState` schema83 stores `SceneMemorySummaries`; snapshot/restore performs detached validation and copy. Invalid times, duplicate source identities, excess sources/groups and future fields under an older schema are rejected. No old-save migration was added. The ordinary native FullSerializer round-trip is prepared for acceptance.

Consolidation happens only on actual recall. Final source-receipt validation renders without consolidation or persistent mutation. Work is bounded to the observer's32 raw records and16×8 summary sources, apart from the same lazy per-world index construction pattern already used by scene memories. There is no per-NPC per-frame consolidation, archive scan, I/O, or asynchronous model work. Source checks and admission also use those existing per-observer bounds.

## Verification

- `tests/NpcSceneSummary.Regression`:41 assertions pass. REDs reproduced missing persistence, old-trauma raw capacity rejecting today's trade, lost native actor refinement after raw eviction, obsolete appraised valuation with and without a raw copy, and a stale read receipt after appraisal before another consolidation. Coverage includes exact source IDs/times, unknown/known actor separation, mobile/static identity parity, claims, group/source bounds, receipt freshness, save validation, retention and dialogue budget.
- Current Core suite:2182/2182 pass,0 failures.
- `NpcMemoryRead.Regression`:34 assertions pass.
- `MemoryReadTools.Regression`:19 assertions pass against the production gateway.
- Existing `ConversationMemoryCompaction.Regression`:276 assertions pass.
- Isolated `RuntimeCompile`:0 warnings,0 errors.
- PowerShell parser accepts `TownTest.CurrentAssertions.ps1`.

Logs: `outputs/scene-summary/{summary,core,read,gateway,conversation,runtime}-verified.log`. All builds use isolated `--artifacts-path` directories under `outputs/scene-summary`.

## Prepared native acceptance

`StreetPopulation.SceneSummaryProbe.cs`, selected by `POTIB_ROADMAP_PROBE=scene-summary`, stages visible actual player health changes for one awake mobile and one awake stationary observer. It invokes the production `NativeNpcSceneObserver.Update`, consolidates the resulting real observation with a controlled later recall timestamp, round-trips through native `SaveLoadManager.Serialize/Deserialize`, and checks the original source retention boundary. The actual world clock is not advanced. Each body's player position, health, concealment and observer binding are restored. The probe does not claim normal elapsed-day behavior or a disk save/load cycle.

After the coordinator grants the native slot and builds/installs current source, run against the owned disposable exterior fixture:

```powershell
$env:POTIB_NAVMESH_TRIAL = '1'
$env:POTIB_ROADMAP_PROBE = 'scene-summary'
./tools/TownTest.ps1 -OutdoorNavigationProbe -FixtureCharacter 'PeopleOfTheIliacBayTest-9a5f197b' -FixtureSaveName 'People - Gothway Garden'
```

Restore the caller's previous environment values afterward. Required output: exactly two `PEOPLE_OF_THE_ILIAC_BAY_SCENE_SUMMARY_BODY_PASS` receipts and one `PEOPLE_OF_THE_ILIAC_BAY_SCENE_SUMMARY_PASS`. The launcher checks these markers. This probe has been compiled but has not been run. Free dialogue quality, natural repeated events, full native disk save/load and broad performance acceptance remain open.
