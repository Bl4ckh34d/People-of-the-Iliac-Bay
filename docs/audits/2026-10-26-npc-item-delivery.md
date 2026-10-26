# NPC item delivery: committed recipient evidence

## Existing path and missing slice

The repository already supports actual shop purchases, acquisition-bound task steps, named loaded NPC recipients, physical approach, recipient availability/capacity checks, and exact native inventory transfer. `NpcNamedAcquiredHandover` binds the recipient identity and `ItemSourceStep`; `ResolveNpcTaskTarget` binds the actual acquired UID, including native split stacks. This work does not add a second delivery system.

The missing slice was recipient evidence. `NativeNpcInventoryBridge` created a durable transfer receipt only for player-to-NPC handoffs. The existing `NativeNpcInventoryProbe` and `NativeNamedOtherHandoverProbe` explicitly required a null receipt for NPC-to-NPC delivery. The generic action journal then recorded the event only for the giver.

## Change

- Extend the existing `NpcActionTransferReceipt` validation/factory to NPC-to-NPC delivery. The original player factory remains direction-specific. Add `NativeItemName`, captured from the actual moved native item's `LongName` at transaction time. A new NPC receipt requires a nonblank name of at most 240 characters without control characters or surrounding whitespace. A small native boundary helper collapses whitespace/control formatting, bounds long labels and uses the explicit unknown label `unidentified item` for an empty result. Display formatting therefore cannot refuse an otherwise valid NPC or player transfer. Existing unnamed player receipt fixtures remain valid. No version change or migration is added.
- Create the receipt inside the existing synchronous native transaction, before inventory commit. Preserve source, moved and retained destination UIDs, exact quantity, actual giver/recipient, time, and optional native base valuation. Existing rollback, revisions, payment, item splitting/merging and replay rules remain authoritative.
- Use the existing bounded receipt drain to enqueue neutral `ItemReceived` evidence for the actual recipient. The responsible person is the actual giver, not the player who may have requested the errand.
- Record both participants' personal journals together from that receipt, naming the actual item and quantity. UIDs remain structured receipt evidence rather than the conversational description. The captured name survives the item later leaving the inventory. Caller narration cannot add gift intent, debt, gratitude, purchase knowledge or player credit. Other people receive no journal entry. Existing action-source deduplication survives snapshot restore.
- Require the NPC recipient to share the source's loaded Unity scene. Recheck clear native line of sight as well as the existing 1.5 m range at the final handoff.
- Extend both existing native probes to expect retained-UID receipts, participant memories and replay rejection. The named-other probe also checks seller wallet conservation.

Existing boundaries remain: a loaded eligible adult recipient, actual native item and inventories, available local route, source consent and existing receiver policy. No unseen recipient search, cross-building delivery system, economic chain, language keyword classifier, or new save migration is introduced.

## Verification performed

All commands used isolated outputs under `work/npc-item-delivery`.

1. `dotnet run --project tests/NpcItemDelivery.Regression/NpcItemDelivery.Regression.csproj --artifacts-path work/npc-item-delivery/artifacts`
   - RED: 1/5 passed; four cases failed because the NPC-to-NPC receipt factory was absent.
   - Intermediate: 4/5 passed; participant test failed because only the actor received memory.
   - Initial GREEN: 5/5 passed. Covers exact recipient/giver and retained merge UID, projection once, two private journal/context views, snapshot replay, wrong person/item/quantity/time/direction rejection, and no premature memory.
   - Review follow-up RED: 4/6 passed; participant memory did not name the actual Steel Saber, and item-name recall failed after inventory departure and restore.
   - Name GREEN: 7/7 passed. Adds native-name persistence after the recipient's inventory is emptied, name validation/bounds, and exact projected-receipt name identity. Both participants' recalled text contains the actual name and omits UIDs.
   - Native-normalization RED: 7/8 passed; the actual native helper was absent. Final GREEN: 8/8 passed. The test links the production helper and exercises null/empty/control-only names, ordinary names, mixed formatting and a 500-character custom label through both NPC and player receipt validation. This is a helper test, not a native gameplay transfer test.
2. `dotnet run --project tests/NpcItemTransferProjection.Regression/NpcItemTransferProjection.Regression.csproj --artifacts-path work/npc-item-delivery/artifacts`
   - 6/6 passed, including existing incoming receipt projection/retention failure boundaries.
3. `dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj --artifacts-path work/npc-item-delivery/artifacts`
   - 2147/2147 passed; 0 failed. Output: `work/npc-item-delivery/core.log`.
4. `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --artifacts-path work/npc-item-delivery/runtime-artifacts --no-incremental`
   - Passed with 0 warnings and 0 errors, including the updated native probes.

These are Core regression and native-source compile results. No Unity or Player process was launched, no mod package was built/deployed, and neither native probe was executed by this worker. The LOS/scene gate and actual physical delivery remain native acceptance work.

## Native acceptance procedure for the parent

1. After the parent serializes the active native runs, build the updated source and run the existing `NativeNpcInventoryProbe.Run()` through the established native harness. It now verifies a partial NPC transfer merging into the recipient's original stack, exact receipt identity/quantity/native name, both journals, native serialization including the captured name, stale revisions, denied access, and replay rejection. Fixture-owned native objects are not evidence of actual scene movement.
2. For actual scene delivery, use the established shop/loot-sale fixture with `-peopleoftheiliacbay-named-other-handover-probe`. `NativeNpcShopLootSaleProbe.RunScene` invokes the existing `NativeNamedOtherHandoverProbe.Run` when this explicit flag is present. Require a real stocked open shop, an actual managed buyer with affordable stock, and another actual loaded eligible adult recipient with capacity and a safe route. A `SKIP` marker is not a pass.
3. Observe both accepted steps: purchase from the exact shelf, then walking to the bound NPC. Require `PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_PASS`, the exact acquired/retained UID relationship, buyer debit and seller credit once, recipient/player money unchanged, no item remaining in the giver's inventory, both participant journal entries, and recipient-only `ItemReceived` evidence. The probe rejects a repeated completed handoff.
4. Repeat with the receiver behind an actual blocking wall or unavailable before arrival. No handoff, recipient receipt or recipient memory may be produced. The item stays with its current owner; any already-completed purchase remains paid exactly once. Do not turn this into a second purchase attempt.
5. In a disposable save, save/load after delivery and verify inventory UID/count, native item name and receipt/memory deduplication. Move the delivered item out of the recipient's inventory using an actual later native transfer, then save/load and ask each participant what was delivered. Both must still name the original item. The receiver can know the handoff but cannot claim to have seen the purchase or to know the player's earlier request. A third resident receives no event knowledge merely because the task completed.

The existing native named-other probe is an API-driven accepted task. Free semantic dialogue selection, actual observed movement, wall refusal and disk save/load need their respective native evidence before broad gameplay acceptance.
