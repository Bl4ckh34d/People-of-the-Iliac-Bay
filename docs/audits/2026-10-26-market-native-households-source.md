# Native market people: household and shared body foundation

Status: source implementation and Core checks only. Native market placement, walking, sleeping, commerce and actual combat acceptance remain open. No Unity/game instance was started by this packet.

## Exact identity and household

`WorldStateStore.AssignExteriorNativeBed` assigns an existing exterior `NativePerson` to a surveyed, available spare bed in an existing residential household. It keeps the native person ID, inserts that same person as a household member, adopts the household surname and preserves unrelated members. It does not invent a spouse or parent relationship. Unsurveyed homes, mismatched capacities, unavailable homes, occupied beds and recovery reservations are rejected.

The existing native-person validator and WorldState restore now recognize a native member at any household position. `Resident.NativeIdentity` aliases the exact restored native record. The normal population planner already skips native person IDs, so membership does not authorize a second mobile body.

`NativeExteriorNpcIdentity.Anchor` must run before the authored flat first moves. Its position-derived identity then stays fixed through travel and reparenting. `TryRead` accepts this anchored identity in the same town's actual home interior; a different location or dungeon cannot reuse it.

## Shared internals

`ResidentActor.InitializeStationary` attaches the normal `ResidentActor` internals to the original `StaticNPC` GameObject. It creates a native `CivilianEntity` when needed and uses the existing inventory owner, vitals, stats, skills, conditions, spellbook and poison bindings. It preserves the native billboard and does not create a `MobilePersonNPC` or second person.

`IsStationary`, `DisplayName`, `BodyOffset` and `GroundPosition` expose rendering-independent actor facts. Stationary placement uses the native billboard's actual half-height. `ReleaseStationary` releases shared bindings and subscriptions before a restored save branch reuses the same authored sprite. Writes from an obsolete world epoch remain rejected.

The shared `NativeNpcStartingBelongings` initializer supplies full outfits, footwear, jewelry chances, personal belongings and a verified property key. A live stationary inventory and the static trade owner share the same `NativeNpcInventoryBinding`, avoiding an independent stale collection.

## Actual finite market goods

`NativeMarketStock.EnsureInitialInventory` initializes only a missing ledger. Existing sold or lost items are not replenished. The exact catalog post and its observed goods flats determine the initial merchandise. Supported scene goods are whole fish, fish fillets, meat, bread, oranges, apples, cups and bottles; observed weapon stalls use native iron weapons. No native livestock sale, eating mechanic, production or supply chain is claimed.

The local authoritative semantic source is DFU's `Assets/Game/Addons/RmbBlockEditor/Persisted/DefaultFlatsCatalog.json`. No profession is inferred from a reused human sprite. Food and household goods use one registered native custom template, `0x4C430100`, with the exact archive/record stored in the native `message` field. DFU's native stack lookup already compares that field. Saved native UIDs and full item payloads remain authoritative.

The main initializer must call `NativeMarketGoodsItem.Register()` before stock materialization. The custom item template is included in `mod/ItemTemplates.json`.

## Verification and remaining acceptance

- Core red/green regression covers exterior work timing, exact post position, actual route duration, household membership, shared surname, restored native alias, separate bed claims, shared death state and atomic rejection of corrupt bed overlap.
- Latest packet Core result: `2187/2187 passed; 0 failed`, in `work/market-households-core-tests.txt`.
- Combined runtime compilation succeeds with zero errors and one independent `MarketVisits.Purchase` unassigned-field warning. Evidence is `work/market-person-runtime-compile.txt`. This is not a Unity package or native market acceptance.
- `NativeMarketPersonProbe.Verify` is prepared to inspect a genuinely bound native market person: original body, shared entity/inventory, native stats, equipped outfit, house key, actual bed, household alias and exact native item roundtrip. It has not been run in Unity.
- Main integration still owns actor lookup, actual attack/defense consumers, death/corpse hooks, dialogue and vendor routing. The commuter adapter owns physical work/home movement, actual bed use and interruptions. This packet alone does not prove those flows or map placement.
