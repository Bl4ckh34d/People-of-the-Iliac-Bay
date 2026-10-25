# Conceptual goal heldout Core preflight fixture v2

This fixture extends `conceptual-goal-heldout-v1.json` with manually reviewed reference bindings for its eight `expected.grounding == "plan"` cases. The bindings let the direct runner submit reference proposals to actual Core before model inference. Candidate catalogs remain synthetic evaluation inputs; this fixture asserts no live availability, consent, or native action.

## Integrity

- Cases: 28; plan cases: 8.
- Source fixture file SHA-256: `484e1f9d8fe2d2a0493a0f8867dd11247f20119aa032e46842710c4666bdefd6`.
- Cases array SHA-256: `e8ca7c7d7cb0131097bdeb1dca6ba2afb24a09be271020e26fea8872efeb48a8` (recomputed with `run_native_route_replay.cases_sha256`).
- Parity check: removing only each added `expected.groundingPlan` and restoring source `casesSha256` yields the original parsed suite exactly. All `playerText`, histories, snapshots, candidate ordering and fields, conceptual gold, other expected values, and top-level review provenance remain unchanged.
- Model-facing prompts and projections are unchanged; bindings stay under expected/gold data.
- No model request, build, native process, or Core preflight ran during fixture preparation. Parent must run actual Core preflight before inference.

## Reference binding rationale

- `cg_wait_seal_35`: Step 0 -> `wait_here`: only candidate is Wait at current position; matches current-spot request and 35-second duration.
- `cg_follow_trail_marker`: Step 0 -> `follow_player`: candidate follows the player, matching the shoulder-follow request and 45-second duration.
- `cg_interact_gate_clerk`: Step 0 -> `gate_clerk`: candidate interacts with the named gate clerk.
- `cg_transfer_owned_key`: Step 0 -> `give_brass_key`: candidate transfers the brass key to Iven, matching the requested recipient and quantity.
- `cg_buy_then_give_vial`: Step 0 -> `buy_vial_31`; step 1 -> `give_vial_31`: purchase source and transferred item ID match, with Neris as recipient and order preserved.
- `cg_moveto_then_interact`: Step 0 -> `cistern`; step 1 -> `cistern_keeper`: building destination and keeper interaction match the two ordered steps.
- `cg_wait_default_duration`: Step 0 -> `wait_here`: candidate is Wait at current position; duration is unstated and remains null in conceptual gold.
- `cg_corrected_current_request`: Step 0 -> `follow_player`: corrected current request is to walk beside the player; stale orchard route is excluded.

## Actual Core preflight blockers

The parent ran the direct runner's actual Core reference-plan preflight before any model call. Six reference plans passed Core contract validation. Two failed because original synthetic candidate identifiers do not match Core's native item-identity contract. This is a fixture/Core representation mismatch, not a grounding-reference mismatch.

- `cg_transfer_owned_key`, step 0 bound to `give_brass_key`: Core returned `Invalid NPC item transfer target or quantity.` Target `ItemId` is `item:brass-key`. `NpcActionExecutor.Validate` in `src/PeopleOfTheIliacBay.Core/NpcActions.cs` requires a nonzero unsigned decimal UID (`ulong.TryParse` with `NumberStyles.None`). `Quantity=1`, `Kind=Person`, recipient `person:iven`, and `TransferDirection=NpcToCounterparty` satisfy the other checks. `ownedItemObserved: true` is fixture metadata; it does not turn the symbolic ID into a Core-valid native UID or establish native inventory ownership.
- `cg_buy_then_give_vial`, step 0 bound to `buy_vial_31`: Core returned `Invalid NPC shop purchase.` Target `ContainerId` is `container:apothecary` and `ItemId` is `item:vial-31`. `NpcShopCommerce.ValidateService` in `src/PeopleOfTheIliacBay.Core/NpcShopCommerce.cs` requires both fields to parse as nonzero unsigned decimal identifiers. `QuotedGold=4`, `Quantity=1`, `Kind=IndoorPoint`, and seller `person:merchant` meet the corresponding shape/value checks. The symbolic container and item IDs fail the native identifier checks; the following transfer shares the same nonnumeric item ID.

Therefore the original expected `plan` gold can express the intended synthetic sequence, but it cannot serve as a Core-valid reference plan with these candidate fields. The JSON and gold remain unchanged to preserve the frozen corpus. Do not count these rows as Core-preflight passes or infer a narrower green score. A future fixture decision must either provide complete Core-compatible synthetic candidates while preserving the intended request, or classify this corpus as interpretation-only; this report does not choose or apply that repair.

The failed preflight is retained in the parent's work artifact. No model request, build, native process, or fixture correction ran here.
