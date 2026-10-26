# Native NPC repair collection and custody

## Scope and findings

The existing adapter already implements both drop-off and later collection of the same owned UID. The old opening comment in `StreetPopulation.NativeRepair.cs` was stale. `StreetPopulation.RoutineRepair.cs` repairs missing daily outings and is unrelated to item repair.

The existing native contract remains authoritative: `FormulaHelper.CalculateItemRepairCost` and `CalculateItemRepairTime` determine the quote; only the NPC wallet pays. `ItemRepairData.LeaveForRepair` stores the native scene and time in the item payload. `IsRepairFinished` and `IsBeingRepairedHere` gate collection. Condition becomes full only during collection, which costs zero. The existing synchronous inventory transaction rolls back condition and repair data if capture or commit fails. No repair scheduler, additional receipt store, save migration, merchant inventory or new service platform was added.

Three bounded gaps were reproduced and fixed:

1. Inventory dialogue projected a repair-held item as plain personal inventory without its custody state. `NativeNpcRepairContext` now supplies pending or ready-for-collection properties, both explicitly unavailable until collection. It uses native timing and does not query the current interior or invent the shop's location. The existing read authority and paging remain unchanged.
2. Drop-off requires a single item, but collection accepted a mutable multi-item stack. Collection now preserves the same `stackCount == 1` contract.
3. Restoring an NPC auto-equipped repair-held armor, clothes and jewelry. `EquipOwnedWearables` now excludes native repair-held items, including finished but uncollected items. The existing melee selector already had this exclusion. Collected wearables still equip normally.

## Verification

The isolated regression executes the production repair predicates, quote call, effect adapter, inventory projection and wearable selector. Roslyn extracts the actual methods structurally. It also compiles the checked-out DFU `ItemRepairData.cs` directly, with controlled scene, clock, formula and item hosts. The real Core action admission, inventory transactions, action completion, cancellation, snapshot and restore paths are used. The fixture's item serializer is deliberately small; it is not a substitute for the native item decoder.

Command:

```powershell
dotnet run --project tests/NativeRepairLifecycle.Regression/NativeRepairLifecycle.Regression.csproj --no-restore --verbosity quiet
```

Observed RED: the first two changes failed as expected (`pending repair falsely appears available`, `collection accepted more than one item for one UID receipt`), with the three existing lifecycle scenarios passing. A separate RED for restoration failed with `restore equipped item still held at repair shop`. Final GREEN: **6/6 scenarios passed**.

Covered behavior:

- Pending and ready context, including an outdoor read without an interior.
- Rejection of multi-item collection.
- Restored wearable exclusion before and after the native due time, followed by normal equipment after collection.
- Exact UID, damaged condition, native due time and wallet through snapshot/restore; early and wrong-shop collection rejected; collection persists full condition and clears custody; replay cannot charge or collect twice.
- Cancellation of an accepted collection, restoration with the item still held by the shop, stale action rejection and a new explicit collection.
- Native condition and repair-data rollback on failed inventory serialization during both drop-off and collection.

Runtime compilation used isolated outputs:

```powershell
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --verbosity quiet -p:BaseIntermediateOutputPath=C:/Users/Daniel/Desktop/Code_Experiments/DFU/LivingCities/isolatedwork/repair-collection/obj/ -p:OutputPath=C:/Users/Daniel/Desktop/Code_Experiments/DFU/LivingCities/isolatedwork/repair-collection/bin/
```

Result: **0 warnings, 0 errors**. Scoped diff whitespace checks pass. No Unity or Player process was started, stopped or controlled by this work. No bundle was built or installed.

## Native acceptance remaining

`TownSmokeHarness` already dispatches `-peopleoftheiliacbay-native-repair-probe`. Use its existing owned disposable character/save flow in an open native repair shop, with an eligible nearby NPC carrying an unequipped, repairable single item and sufficient own gold. The probe requires a `PeopleOfTheIliacBayTest-` character and checks native keeper approach, exact price, duration, one payment, rejection before the due time, then explicit collection at the same keeper with no second payment. It advances time only inside this opt-in probe.

The probe now additionally snapshots into a detached `WorldStateStore` and uses the real `NativeNpcInventoryBridge.TryOpen` decoder before and after collection. It verifies exact UID, stack, condition, wallet, due time and original native shop. The success marker adds `detachedNativeRestore=true`. This probe has compiled but **has not run for this change**.

Full native acceptance must also save/reload the actual game after drop-off, confirm repair-held wearables remain unequipped, interrupt a collection before arrival, return later to the same open shop, and complete collection once. Walking, free Gemma requests, the actual SaveLoadManager reload and production repair formula execution remain native acceptance work; the controlled regression does not prove these.
