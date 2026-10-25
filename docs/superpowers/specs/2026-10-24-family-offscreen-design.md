# Shared household offscreen lifecycle

This extends the authorized family implementation and the accepted schema71 physical boundary. It does not replace the full family outcome with a projection-only feature.

## Ownership and evidence

Keep `WorldState.HouseholdJointProgress` as actual body evidence. Add one bounded `HouseholdJointSimulation` per household, with an immutable copy of the last real progress, explicit `world-time-projection` provenance, its own revision, `StartedAt`, `AdvancedThrough`, phase/timing and two virtual route cursors. Never call `Observe` with calculated positions. Beginning simulation consumes no unseen movement before the ownership handoff. A consumed simulation retains its anchor/projection as the latest bounded reconstruction evidence until the next handoff replaces it; repeated handoffs within one event continue increasing its revision.

The store validates the exact household day/source, current actual sequence and both participants before handing ownership to simulation. Physical writes cannot compete with an active simulation. Reconstructing bodies requires an explicit store operation using the canonical stored source, two actual current positions near the projection and current priority eligibility. It closes the simulation and records the new actual observation atomically. Ordinary schedule fallback must not move a reserved participant.

## Travel and time

Advance along saved routes analytically, using their measured `WalkSeconds`/`ExitWalkSeconds`. Both parents use the slower normalized pace and a leader waits when already ahead. World-time pauses and either unavailable parent consume a held interval without movement or later catch-up. Retain exact event transition times; a large time jump and small steps agree. An arrival at an unloaded Inn is explicitly simulated and cannot create physical entry, purchases, seating or dialogue evidence.

## Complete lifecycle consumers

Unloaded Inn progression needs a surveyed, source-bound keeper/menu/price/service catalog and real NPC wallet transactions with replay protection. Existing room/reception surveys do not supply food/drink products. Add a service visit contract distinct from an actual `ObservedVisit`; use the same transaction/receipt rules for physical and simulated service without inventing counter contact. Refusal/capacity/conflict needs an explicit return/recovery policy. Shared dwell and return consume only the time remaining after confirmed service or refusal. Simulated home arrival and actual home reconstruction remain distinguishable.

The global runtime advances only households whose actual scene owner has yielded. Scene entry reconstructs before individual schedules run. Alarm, appointments, actions/tasks, care, absence, death/protection, missing route proof and failed physical writes preempt or block this owner. Pending actual body positions from a failed write must survive teardown through durable recovery, rather than being projected from an older checkpoint.

## Acceptance

Core RED/GREEN covers separate physical/simulated evidence, measured pace, asymmetric parents, paused intervals, chunking, source/sequence/replay isolation, migration, ownership exclusion and atomic reconstruction. Native acceptance must cover scene changes and save/load, then a complete live household event with actual/unloaded segments, two real purchases, shared table, return and childcare/door behavior. Keep live joint generation gated until every required consumer and recovery path works.
