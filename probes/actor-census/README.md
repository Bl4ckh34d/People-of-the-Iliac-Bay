# Read-only actor census

Core regression command:

```powershell
dotnet run --project probes/actor-census/ActorCensusProbe.csproj
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore
```

The runtime entry point is `ActorCensusProbe.Run(WorldStateStore)`, started explicitly by the owning mod coroutine. It has no automatic startup or update hook. The caller must pass its existing store, not call `PeopleOfTheIliacBayMod.GetSaveData()`: that save method captures gameplay state and is not read-only.

Reports are UTF-8 TSV files under `DaggerfallUnity.Settings.PersistentDataPath/ActorCensus`. `ActorCensusProbe.LastPath` and `ActorCensusSurvey.LastReport` expose the latest result. The report includes enabled-mod order/readiness, source identity, appearance and provider evidence, role/building, quest binding, service routing, recorded home/bed claims, exclusions, unknowns, collision flags, and sampled timing. Inactive scene actors are included. Exact texture asset-name matches are candidates only; they do not establish the actual source of an already-created renderer or world-data override.

The scan waits 30 frames for entry handlers, then slices ledger reads and scene traversal at 32 steps or a 2 ms target. Individual Unity/native calls are indivisible, so 2 ms is not a guaranteed upper bound; inspect the measured maximum. Caps are 100,000 scene nodes, 100,000 claim steps, and 8,192 actor observations. A load or observed scene/context transition aborts the scan. Claims and actors are observed over multiple frames, not an atomic world snapshot. Reports mark truncation, errors, and incomplete coverage.

Static building actors use their native context/map/building/hash/nameSeed tuple. Original static identity survives temporary quest binding. Custom quest actors use quest UID/resource symbol; existing People of the Iliac Bay actors retain their person/guard IDs; native enemies with nonzero LoadID retain that native ID. Unidentified mobiles, custom/dungeon actors, and decorative NPC flats receive explicitly nonpersistent session keys. Two representations sharing an identity are retained and flagged, never silently merged. Repeated observations of the same representation do not duplicate it. No name or sprite-only identity exists.

## Native sampling and reload acceptance (not yet run)

1. At a settled Daggerfall exterior, invoke the census twice. Save both files. Inspect the market and the castle exterior, including inactive NPC flats. Check zero read errors, no truncation, stable native keys, separate identical-sprite actors, and retained collision evidence.
2. Repeat inside a castle, guild, temple, shop, and inn. Check each service classification against native interaction, each quest UID/symbol against its actual resource, and each known bed/home against the existing ledger. Do not infer a dwelling from a workplace or a room booking from an individual bed.
3. Re-enter each sample and reload its save, invoking the census again after providers settle. Compare stable keys and `signature` columns. Runtime instance IDs may change. Session-only entries cannot certify persistence. Investigate missing/new identities against provider creation, quest state, visibility, and streaming rather than assuming a census defect.
4. Repeat in a village, a walled settlement, and a non-walled settlement. Include active Quest Pack 1 actors when present. Record map/building keys, mod order, quest names, and actual provider evidence. Changing mod order must use a separate controlled test setup and retain the same fixture where possible.
5. Verify the report's synchronous read-only checks for transforms, active state, `StaticNPC.Data`, and both shared random states. Core tests separately prove claim enumeration leaves serialized `WorldState` unchanged. Check native services, quest progress, mortality, homes, and actor counts before/after invocation; do not use a save capture method as a read-only checksum.
6. Inspect maximum slice timing and total frames at a large city and inn. Repeat during entry and deliberate reload to verify explicit abort behavior. Do not certify full provider completion from a 30-frame settling window or `Mod.IsReady` alone; rerun after late callbacks.

No Unity editor, player, build/deploy, or native sampling was run for this source checkpoint. The full I01 location matrix remains unaccepted. Batched humanoid props without scene actor components, unloaded scenes, arbitrary provider registries, exact world-data provenance, and undisclosed ownership APIs remain unknown.
