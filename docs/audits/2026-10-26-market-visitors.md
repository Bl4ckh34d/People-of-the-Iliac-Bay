# Source-backed market visits and stage viewing

Status: source implementation prepared on 26 October 2026. The focused CPU checks pass. Native acceptance has not run. This is not an installed-build or gameplay acceptance claim.

## Verified source

The installed external-mod inspection identifies **Lively Cities 1.3.4** as the source of populated `MARKAA00.RMB` and `MARKAA01.RMB` layouts. **Beautiful Cities of Daggerfall 0.5.0** supplies different `MARKAB00.RMB` and `MARKAB01.RMB` replacements. The inspected Beautiful Cities market layouts contain no exterior NPC records. The catalogs must use the effective loaded block from `ContentReader.GetBlock`, rather than assume that both mods modify the same records.

Native asset semantics come from DFU's `Assets/Game/Addons/RmbBlockEditor/Persisted/DefaultFlatsCatalog.json` and `DefaultModelsCatalog.json`. Examples:

| Native asset | Verified meaning |
| --- | --- |
| `211.8`–`211.11`, `211.41`, `211.42` | Fish and fish fillets |
| `211.40` | Meat |
| `211.31` | Bread |
| `213.0`, `213.1` | Orange and apple |
| `200.0`–`200.6`, `205.11`–`205.16` | Cups and bottles |
| `182.37`, `182.50`, related catalog musician/jester entries | Appearance evidence for performers; insufficient alone to establish a show |
| `437xx` | Building signs, not market tables or goods |
| `41815`–`41834` | Crates, not proof of their contents |

Faction `520` is `People_of_Betony`, not a merchant role. It is not used as sales evidence.

The read-only audit in `work/market-layout-audit/audit_geometry.py` reads the extracted replacement JSON and the actual native `ARCH3D.BSA` table vertices. It writes `source-geometry-report.json`. It applies the native transformations independently: exterior billboards use the unrotated subrecord offset from `RMBLayout.AddExteriorBlockFlats`; model geometry uses the rotated subrecord and model matrices. Applying the model rotation to exterior billboards would bind people to the wrong props.

The inspected `MARKAA00.RMB` subrecord `0` supports these seven street-level sales posts:

| Flat Position | Table goods | Native table model |
| --- | --- | --- |
| `7434` | Fish | `41109` |
| `7442` | Household goods | `41130` |
| `7462` | Household goods | `41112` |
| `7489` | Meat | `41109` |
| `7512` | Weapons | `41110` |
| `7514` | Weapons | `41112` |
| `7516` | Bread | `41109` |

The raised musician/jester group is `7430`–`7432`. `MARKAA01.RMB` subrecord `2` also has table-backed fish, meat, bread and fruit evidence, plus musicians `6425`, `6442`, `6443`. Some of its displays include cutting implements classified as weapons in the native flat catalog. The source report retains those individual assets instead of inventing a unique profession from a reused person sprite.

## Implementation boundary

`NativeMarketCatalog` retains source IDs, actual `StaticNPC` bodies, root-local post and goods positions, and native table bounds. Sales-role inference is deliberately limited to the two inspected market subrecords. Raised terrace patrons beside cups do not become vendors. A show outside those sales records still requires actual raised collision geometry and a nearby group of placed performer appearances.

The catalog reserves clear, reachable standing positions in front of table goods and a separate payment position opposite the seller. Stage candidates require lower ground than the authored stage base, clear body space and a sight line. The public side is selected from the real reachable candidates. Native screenshots must still confirm that the selected side is the stage front. The catalog creates no stage, table, goods or seller.

`StreetPopulation.MarketVisits` uses existing eligible routine residents. A passing resident can make a short market interlude; a closed-shop visitor uses the existing return budget. The resident walks to the nearest free table, physically dwells there, then selects another unvisited nearby table. Ordinary visits admit two to four stops. A show visit occupies one viewing place. Identity/day seeds vary the stays. Physical arrival starts dwell; leaving the slot resets dwell. A blocker can cause another reserved position at the same table. Standing attention remains on goods or performers, without the free-standing player-watch behavior.

The visitor physically returns to its saved continuation point. Live context and valued scene memories distinguish the reason, current activity, goods or show, intended return, and actual purchase outcome. Active visit phases are currently scene runtime state. The existing inventory and scene-memory stores persist completed ownership and memories; this change does not add an independent economic state or background production.

`StreetPopulation.MarketPurchases` uses `NativeVendorTradeAdapter.TryOpenMarketStock` and `TryNpcPurchase`. Buyer choice reads the real vendor stock, wallet, native price calculation and encumbrance. Payment requires physical arrival at the seller's separate slot and a short negotiation stay. The existing native inventory bridge commits the exact item and both wallets together. A failed purchase does not claim ownership. Bought items remain in the buyer's normal native inventory.

## Integration hooks

The main integration owner wires:

- `MarketVisitState(plan, seconds, source)` around the ordinary scheduled state.
- `PlaceMarketVisit(plan, actor, state, seconds)` before ordinary closed-shop placement.
- `HasMarketActivity(id, now)` into the real-activity check when a closed shop opens.
- `TryMarketVisitContext(actor, now, out context)` before ordinary closed-shop context.
- `ResetMarketVisits()` when the current town/actors are cleared.
- `POTIB_ROADMAP_PROBE=market-visit` to `yield return streets.RunMarketVisitNativeProbe()` in `NativeNavigationTrial`.

## Verification and remaining native checks

`dotnet run --project tests/MarketVisits.Regression/MarketVisits.Regression.csproj` passes **5/5**. The initial behavior run failed **0/5**. Checks cover native asset semantics, nearest eligible slot selection, arrival-owned dwell, departure reset, and stable varied durations.

The isolated full RuntimeCompile run uses `outputs/market-visitors-compile/obj` and `/bin`. It passes with **zero warnings and zero errors**, including the parallel household, commuter and vendor-trade source changes available at that check. This compiles source; it does not build or install a Unity mod.

`RunMarketVisitNativeProbe` is prepared but unexecuted. It refuses to pass without an actual serving fish post, goods table, distinct browse/payment positions and at least four stage-front candidates. It stages only the starting positions of four existing resident bodies. Production code must then perform browsing, goods-facing with a nearby player, counter approach, payment, exact vendor stock decrement, exact buyer/seller wallet changes, retained recipient UID and serialized item type, physical return, and four separate viewing arrivals. The fixture checks valued item/seller memory as well. No replacement market, invented seller, free item or fake wallet is used.

Native screenshots from the fish stand and stage are authorized but remain pending the serialized game slot. Also pending: natural encounter frequency, additional layouts, interruptions, time skips, scene reload during an active interlude, and broad routine integration. CPU checks and source geometry are not substitutes for those checks.
