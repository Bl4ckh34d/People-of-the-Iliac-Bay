# Theft witness memory — 26 October 2026

## Source findings and bounded repair

The native shop receipt adapter already observed successful transfers and tainted the exact player item UID. Its memory callback evaluated visibility after the transfer and read the current item name at that later point. Private-property transfers had no scene-memory producer. Native `DaggerfallInventoryWindow.AttemptPrivatePropertyTheft` runs when inventory closes, after items have already moved; observing that later method would admit people who arrived or woke after the taking.

The existing `NativeTheftHooks` now captures a bounded memory callback immediately before native `DoTransferItem`. It accepts only the actual current house-owned, non-player-owned loot collection transferring an item into the current player's inventory. It rejects household taking allowed by the existing consent decision. Publication requires the exact source item to leave the source and the expected destination identity/quantity to appear, including a merged stack. Gold requires the exact native wallet increase. A split stack uses its newly created source item and transferred quantity; the untouched remainder is not reported. Native cancelled split prompts do not call `DoTransferItem`.

The existing shop hook similarly captures witnesses and the item's native displayed name before `DoSteal`, then publishes only after its existing exact transfer checks succeed. The legal-provenance receipt remains unchanged and independent. The older post-transfer theft-memory branch is removed, preventing two memories from the same producer. Publication is consumed once per native observation; each new successful transfer receives a new scene event occurrence, including another transfer into an already stolen stack. Neither inventory closing nor scene polling publishes theft memories.

No theft probability, charge, guard response, inventory ownership policy or native transfer behavior is changed.

## Knowledge boundary

- Mobile residents and ordinary stationary people use the same `NativeNpcSceneObserver.Sees` path. Its existing current-world, alive, active, awake, facing, range and native LOS checks remain authoritative. Invisible players are not identified.
- A private-container witness must see both the player and the source container. The source-container collider and its own children are excluded as the observed target; other hits, including walls, remain blockers. The raycast loop checks all remaining hits, independent of `RaycastAll` ordering. Existing perception callers omit the new optional target and retain their prior behavior.
- The observer IDs, time, settlement, building key and building label are captured before transfer. Publication rejects a replaced world store or invalidated world epoch. Later turning, waking, loading or changing location cannot add witnesses or rewrite the occurrence's location.
- The native transfer supplies item quantity and `LongName`. Native `ItemHelper.ResolveItemName` returns the generic localized template label for unidentified items; `ResolveItemLongName` returns that result before identified-only naming. The adapter does not inspect magic effects or identify an item. Missing names remain explicitly unknown.
- The player is the responsible actor only because the admitted witness saw the player at the actual native transfer. The property owner's identity is not supplied by these transfer callbacks, so `SubjectId` remains null. Motive remains unknown. An absent owner, invisible thief, noise, report or later missing item does not become an eyewitness record.
- Theft uses the existing `RememberSceneEvent` default evaluation and retention. This preserves bounded scene recall and deterministic forgetting without adding mood writes, a separate archive, or a new crime-report system.

Native shoplifting semantics matter: `Dice100.FailedRoll(chanceBeingDetected)` is the successful transfer branch. The detection probability is not a per-NPC visual observation result. A confirmed transfer seen by an admitted witness is remembered even when the native roll does not raise a charge. Conversely, a charge or failed attempt cannot grant sight or identity to an unseeing person. This change records completed theft only; a detected attempt without a transfer is not falsely described as stolen property.

## Focused verification

`tests/NativeTheftMemory.Regression` compiles the production `NativeTheftHooks`, `NativePlayerShopProvenanceHooks` and `StreetPopulation.TheftMemories` sources and uses the real Core `WorldStateStore`. Unity, native item collections, Harmony installation and perception dependencies are substituted. These are producer-boundary tests, not native raycast, patched-engine or gameplay acceptance.

The first RED run failed with `Shop witnesses were not captured before native transfer.` After adding the shared runtime capture test, the second RED failed with `Mobile/stationary producer did not preserve the event-time witness set or restore boundary.` Review regressions then reproduced loss of native provenance when the item label, optional scene capture, or scene publication failed. Missing native name data now remains unknown; optional memory failures cannot invalidate independent exact transfer receipts. Final **21/21** passes cover:

- event-time sight retained despite later visibility changes;
- exact captured item label and quantity;
- missing/throwing native label data without invented identity or lost provenance;
- independent provenance when scene capture or publication fails, including another item in the same basket;
- unsuccessful transfers, replaced player inventory and publication replay;
- private consent rejection, exact native gold increase, merged destination and split source quantity;
- mobile/stationary identity parity, invisibility exclusion, captured location and restore-epoch rejection;
- existing scene evaluation and retention.

Existing player-item provenance regression passes **13/13**. QuestHooks compiles with **0 warnings and 0 errors** against the installed native assemblies. Isolated commands:

```powershell
dotnet run --project tests/NativeTheftMemory.Regression/NativeTheftMemory.Regression.csproj --artifacts-path work/theft-memory-tests
dotnet run --project tests/PlayerItemProvenance.Regression/PlayerItemProvenance.Regression.csproj --artifacts-path work/theft-memory-provenance
dotnet build src/PeopleOfTheIliacBay.QuestHooks/PeopleOfTheIliacBay.QuestHooks.csproj --artifacts-path work/theft-memory-hooks --nologo
```

Logs: `work/theft-memory-red.log`, `work/theft-memory-scene-red.log`, `work/theft-memory-name-red.log`, `work/theft-memory-callback-red.log`, `work/theft-memory-publication-red.log`, `work/theft-memory-green.log`, `work/theft-memory-provenance.log`, and `work/theft-memory-hooks.log`. An earlier full RuntimeCompile attempt was blocked by concurrent unfinished dungeon-follow source; that attempt establishes no theft compilation failure or full-runtime success. The main agent owns the coordinated final RuntimeCompile and integration.

## Opt-in native probe — not run here

The launcher accepts `POTIB_ROADMAP_PROBE=theft-memory` with `POTIB_NAVMESH_INTERIOR=131343` through the existing owned navigation-trial route. `StreetPopulation.VerifyTheftMemories` requires the actual Daggerfall shop and an existing ordinary stationary person plus resident body. It temporarily positions the resident in the shop. No extra resident identity is created.

The **22-case** fixture runs actual native `DoSteal` through installed Harmony hooks with a controlled basket. A selected Unity random seed produces the real native success draw; the original random state, crime state, skill-use counters and Thieves Guild counters are restored. Each observer kind tests visible theft, backward-facing and distant player, invisibility, controlled sleep, inactive body and a real wall. Assertions require the actual item transfer, exact stolen-item provenance, correct memory count, actor/item/location facts and existing retention.

Additional private-transfer cases invoke actual native `DoTransferItem` through installed hooks with a staged house-owned container and real collider. They test visible taking, sleep, source behind the observer and an intervening wall. **That container is staged inside the shop. These cases prove the adapter and perception boundary only; they do not prove ordinary private-home ownership or household consent gameplay.** State and body changes do not yield while active. Memory/provenance results use detached world snapshots; injected items and fixture objects are removed.

Expected completion marker: `PEOPLE_OF_THE_ILIAC_BAY_THEFT_MEMORY_PASS`. This agent did not launch Unity, the Player, deployment or the native probe. Full native acceptance remains pending, including actual private-home containers/consent, natural waking, native save-file reload, ordinary split/cancel UI, victim discovery of missing goods, pickpocketing and corpse theft. Those last two producers are outside this bounded transfer repair.

## Root native acceptance, 26 October16:47

Installed bundle `A5524687F1FBB85CFD0E062ADB6E123CF3473FFA14EA6827A966373A1D1917C3`
passes all22 native cases through actual installed Harmony hooks. The player
starts from owned SAVE0 `People - Gothway Garden` and the existing warmup enters
native shop131343. Both observer kinds pass actual shop `DoSteal` transfer,
provenance, memory content and visibility/state cases. Both also pass the four
staged private-container cases, including its actual collider and an intervening
wall. The final marker and outer TownTest PASS are present; process and launcher
terminate successfully. Evidence: `work/native-theft-memory-01-player.log`
(final marker line946) and `work/native-theft-memory-01-launch.log`.

Root independent checks:21/21 producer regressions,2151/2151 shared Core,
RuntimeCompile with0 warnings/errors and successful Unity package verification
(`work/eleven-*-root.log`, `work/eleven-build-unity.log`). This upgrades the
specific controlled native cases above, not private-home consent gameplay,
natural encounters, full disk reload or the unimplemented producers listed above.
