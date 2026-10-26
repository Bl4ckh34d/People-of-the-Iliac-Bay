# Inn purchase recollections — 26 October 2026

## Gap and scope

The existing physical `TryBuyNpcInnConsumption` path persisted the wallet debit, service receipt and consumption cursor. The existing `HouseholdInnService` path did the same for admitted world-time services. Neither published a recollection. The ordinary shop purchase memory in `StreetPopulation.ShopPurchases` did not cover tavern menu service.

Successful Inn receipts now feed the existing `Shopping` scene memory. The buyer recalls the actual menu item, keeper and paid gold. The stationary keeper recalls the customer, item and actual payer. A distinct funded partner recalls their own payment for the buyer. Other family members and bystanders receive no facts merely because they belong to the same household or are nearby.

The text describes receiving or serving the purchased item. It does not claim eating, drinking, enjoyment, hunger relief, a gift motive or a further inventory transfer. The service receipt remains the authority. There is no keeper stock, new hunger/economy model, appraisal event type, schema change or permanent memory ledger.

## Commit and recall boundaries

- Candidate recollections are normalized and validated before a wallet commit. Unrepresentable optional memory identities do not invalidate the payment or truncate identity keys.
- Recollections publish only after a new successful receipt is committed. Rejected, refused, unavailable and unaffordable attempts create no purchase memory.
- A stable `inn-purchase:<VisitId>` event key joins buyer, keeper and payer perspectives. Replay, checkpoint capture, reload and materialization do not emit another event or renew its age. Old receipts are not backfilled if the memory has already expired or was never recorded.
- Projected household services use their actual committed counter time and the same receipt identity. Their later physical reconstruction adds neither a second debit nor another recollection. This records an existing admitted service transaction; it does not simulate additional actions.
- `Shopping` uses the existing neutral evaluation, importance, randomized deterministic lifetime and 32-record bound. No extra mood or relationship write occurs. A full set of more important memories can suppress this incidental recollection without preventing payment.
- Existing compact scene recall exposes the result to mobile and stationary conversation context. No new prompt or retrieval mechanism is required.

## Verification

`work/inn-memory-red.log` records six missing-memory failures before implementation. Two negative cases already passed. The final pre-integration Core run reports **2138/2138, zero failures** in `work/inn-memory-green.log`.

Seven new Core cases cover participant facts, stationary compact recall, unchanged keeper stock, replay and serialized restore, no replay backfill, rejected/failed service, ordinary retention and trauma saturation, normalized native product text, projected funded service and physical materialization, and pending/refused admissions. The existing physical family payer test additionally checks five correctly distributed memories and excludes partner omniscience.

`dotnet build tools/RuntimeCompile/RuntimeCompile.csproj -c Release --no-restore` passes with zero warnings/errors (`work/inn-memory-runtime.log`). `NpcInnConsumption.Regression` passes all 20 cases, including native FullSerializer and fractional campaign-time receipt identity (`work/inn-memory-consumption.log`).

These results predate registration of the separate dungeon trajectory agent's intentional RED tests. They are not a final combined-suite claim for concurrent work.

## Native acceptance still required

No game, editor, model request or package deployment was started for this change. The main agent owns native acceptance and integration. After a real counter payment, inspect buyer, keeper and funded payer recall, then reload/re-enter and confirm unchanged event IDs, times, wallet and memory counts. An interrupted journey before payment must have no purchase recollection. Actual free-form Gemma wording and a complete normal family Inn visit remain unverified here.
