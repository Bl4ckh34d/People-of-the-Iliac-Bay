# Static personal trade: native inventory payload mismatch

## Reproduction and cause

The owned Gothway Garden fixture reproduces the refused static Talk-to-trade transition in `static-trade-01` through `03`. The conversation is open, the exact native merchant identity matches, the service is available and the NPC is willing. Inventory binding succeeds at revision 0, but `NpcInventoryReadAuthority.Matches` rejects the reconstructed items.

The first differing item is template 83, `UselessItems1.Glass_Bottle`. `NpcMerchantStock` still generated this native potion template as ordinary junk. Its saved `potionRecipe` is 0; DFU's `DaggerfallUnityItem.FromItemData` converts that legacy empty recipe to 221871 when the detached inventory is reconstructed. The same UID therefore has a different payload. The native diagnostic captures both payloads in `work/playtest-2026-10-26/editor/static-trade-03/editor.log`.

## Repair

Remove template 83 from the separate merchant starting-stock pool. Other mundane containers remain, and merchant stock still contains 10–14 deterministic distinct items. Actual potions must be constructed with a valid native recipe. Do not relax the exact inventory-authority check, overwrite an existing saved ledger or substitute shop-shelf stock.

The existing native fixture also used the same invalid bottle as disposable transaction stock. It now uses `Glass_Jar`. An explicit `ProfileInEditor.ps1 -ValidateStaticTrade -FixtureFolder SAVE0` path runs that fixture in an owned editor with copied data, at noon while the real shop is open. It does not modify ordinary user settings or fabricate a mobile resident for the static speaker.

## Verification

- New core regression fails before the repair (2120/2121) and passes afterwards (2121/2121). It checks merchant stock across 100 world-seed/identity combinations.
- Existing production-linked static-role and merchant-variety regression passes 1,247 assertions.
- `static-trade-04` completes the native window transition, preview/cancel, purchase and sale for `npc:206728581:267:52`.
- Controlled two-item stack UID 33554507 remains unchanged during preview/cancel. Buying one creates recipient UID 33554508 and transfers exactly 126 g; selling it back transfers exactly 81 g. Mercantile is set to 20 in the disposable fixture and the ordinary tally is checked.
- Shop-shelf stock is unchanged. After the round trip, the player's item collection returns to its original contents. The saved personal ledger is reopened through ordinary native Talk between cases.
- Runtime compilation has zero warnings/errors. Tool syntax, tooling protocols, source policy and whitespace checks pass.

This verifies one real shopkeeper's personal inventory and a newly established valid ledger. Free LLM trade wording, other static roles, mixed barter and already-malformed development inventories are not covered by that native run. Existing saved inventories are not regenerated or silently converted.
