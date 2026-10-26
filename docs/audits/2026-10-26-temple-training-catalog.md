# Native temple training: complete skill selection

Scope: the roadmap item “Vorhandene Aktionsadapter fertig prüfen”, specifically the existing temple-training action. This change does not claim native gameplay acceptance.

## Existing behavior checked

`NativeNpcTempleTraining` already checks a real temple trainer, native non-member access, the temple's native skill list and maximum, the resident's native level, and the resident's own inventory revision and wallet. The existing action pays once, waits three game hours without advancing the player's clock, and applies the selected resident skill through an action-effect receipt. Existing Core tests cover the receipt-bound timer and training contract states.

Repair drop-off and collection also already exist: they bind an owned item UID, native repair cost/duration and the actual repair shop, with free collection only after native completion. Neither service needed a second implementation.

## Confirmed gap and repair

`OfferNativeTempleTrainingAction` previously requested the first eligible native skill and published exactly one candidate. Other skills offered by the same temple were therefore absent from semantic selection.

The catalog now publishes one eligible, reachable, affordable lesson per native skill, bounded by the existing candidate limit. Each candidate retains its exact trainer, skill, quantity and native quote. Preparation and execution retain their existing consent and native revalidation paths. Exact-skill lookup cannot continue into a different skill after an approach failure.

The existing native probe now selects the last available lesson, so a fixture with multiple eligible lessons exercises a non-first skill. It logs the offered-lesson count and selected skill, and requires acceptance to retain that exact skill and trainer. Its payment, elapsed-time, saved/live skill, and unchanged-player checks remain in place.

## Verification

The new `tests/NativeTempleTrainingCatalog.Regression` project links the production catalog, preparation and execution adapter. Controlled scene and native-service doubles isolate this adapter; they do not prove native guild pricing, pathfinding, payment or skill awards.

- Before the production change: **4/6 passed, 2 failed**. Failures were the missing second skill and inability to select that skill.
- After the change: **6/6 passed, 0 failed**. Cases cover all eligible skills; non-first selection through preparation/execution dispatch; unavailable selected skill; semantic/legacy candidate limits; closed, unreachable and unaffordable services; changed price and trainer rejection.
- `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj -c Release --no-restore`: **0 warnings, 0 errors**.

Reproduce the focused checks with:

```powershell
dotnet run --project tests/NativeTempleTrainingCatalog.Regression/NativeTempleTrainingCatalog.Regression.csproj --no-restore
```

The first run of the new test project needs restore, so omit `--no-restore` on a fresh checkout.

## Remaining acceptance

Run the existing opt-in temple-training native probe in its owned fixture after the main agent installs the verified build. Confirm `offeredLessons` exceeds one when the actual resident is eligible for multiple skills; inspect the selected skill, exact one-time NPC payment, delayed result and unchanged player gold/skills. A one-skill fixture cannot prove native non-first selection.

No game/editor process, package build, deployment, model call or native acceptance was performed for this slice. The full Core run is left to the coordinating main agent because another authorized slice had intentional red tests in the shared tree during this verification.
