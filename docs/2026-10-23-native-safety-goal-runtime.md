# Native Safety goal runtime

`CivilianResponses.Threat` creates one observer-owned `Safety` goal after a living, awake, non-quest-protected resident directly witnesses an active hostile-threat incident. The bridge verifies the incident and witness are the live objects held by `WorldStateStore`, and checks persisted resident liveness/quest protection as well as current runtime gates. It stores only a hashed episode reference as `NativeObservation`; it receives no culprit or player identity. Existing civilian movement remains the action path.

`StreetPopulation.Update` completes the goal only after native refuge arrival has set `ReachedRefuge` and `RecordShelterArrival` has sealed that arrival. The production route selects an accessible nearby entrance (shop, tavern, guild hall, or palace) or the resident's accessible home; `CivilianResponses.Move` sets the arrival flag only within 0.2 m of the final door/home route point, with a nonnegative building key. A route start or the indoor room-level retreat is not marked as a refuge and cannot fulfill the goal. Every two seconds, reconciliation examines at most 16 native plans in round-robin order and abandons active goals when their exact source alarm expires, their resident dies, or the resident becomes quest-protected. Large populations may delay that check across multiple batches. A cheap fixed-row check avoids copying accepted-update history for residents with no active Safety goal. An unrelated newer alarm for the same resident does not prolong that source episode. Reconciliation needs no rendered actor and runs only while `ReactionsEnabled` is true. Threat polling, movement, and duplicate phases do not append receipts.

The per-resident row ID is fixed. Episode identity hashes the owner, native incident ID, and incident start time; the last field distinguishes a fresh incident after the same hostile ID expires. Accepted operation receipts prevent replay from reopening fulfilled or abandoned episodes. New incidents replace the same row, and stale arrivals cannot fulfill the current episode.

The V1 ledger keeps every accepted-update receipt. A safety episode needs a start and terminal receipt; repeated episodes therefore grow `AcceptedUpdates` even though Goal rows stay fixed. The 4096-receipt limit remains authoritative and causes updates to fail closed; this slice does not prune replay evidence or claim indefinite lifecycle capacity. Existing motivation-context capture and formatting project the persisted goal.

Focused regression command:

```powershell
dotnet run --project tests/NpcSafetyMotivation.Regression/NpcSafetyMotivation.Regression.csproj --artifacts-path C:\Users\Daniel\Documents\Codex\2026-10-23\bit\work\safety\artifacts
```
