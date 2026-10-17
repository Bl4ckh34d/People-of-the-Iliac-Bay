# Observed visit continuity

## Reproduced problem

Loaded shop movement previously sampled absolute schedule time, while collision avoidance moved the body separately. A delayed body could target a later shelf through an intervening obstacle. On scene changes, `physicalShopVisits`, `shopJourneys` and `shopDepartures` are cleared, and bodies are reconstructed from the original clock. A visible delay therefore disappears from the simulation.

## Required invariant

A person has one authoritative location and remaining journey. A player observing a delayed person, leaving the building, then immediately returning must find the person at a physically reachable point along that remaining journey. Outside representation cannot begin until the interior exit has been reached. Reloading must preserve the same rule. Offscreen movement may advance analytically along surveyed routes, without simulating every frame.

## Work sequence

1. Replace loaded clock sampling with actual waypoint arrival, followed by arrival-based browsing/service dwell. Share this with local and visiting shoppers and common-room patrons. Verify entire rendered visits without phase jumps.
2. Persist observed activity state: resident identity, settlement/building, original visit identifier/window, copied validated remaining routes, active waypoint, actual local ground position, remaining dwell, observation timestamp and activity target. Include both ordinary visits and physical departure. Capture before native scene destruction and before save; do not rely only on the subsequent controller update.
3. Analytically advance that checkpoint while unloaded. Preserve interior-local coordinates until the doorway, then switch to the saved exterior route. Resolve the next actual itinerary stop, or the return-home route for the final stop. Existing cancellation-to-home `VisitDeparture` cannot represent this unchanged.
4. Make local and foreign activity queries consult the continuation before original schedule time. Apply the delayed handoff consistently to destination selection, presence, arrival and later visits; do not merely keep a rendered body while the authoritative person is elsewhere. Reconcile closing hours and missed appointments by explicit cancellation/rerouting, never teleportation.
5. Rehydrate the physical follower from the analytically advanced checkpoint. Reject duplicate bodies and conflicting continuations transactionally. Death, crime response and explicit service rejection supersede shopping, but retain physical exit progress.
6. Test blocked movement, interruption during browsing/payment/departure, immediate exit/re-entry, time elapsed outside, save/load, multi-stop outings, foreign visitors, doorway handoff and cancellation. Each test must check actual identity and reachable position, not merely actor count or a passing schedule query.

The loaded movement fix alone does not complete this requirement. Household-history work remains deferred until this regression has been addressed.

## Implementation checkpoint

ObservedVisit now stores copied remaining legs, dwell, original completion time and return-home deadline. Version 19 saves accept the additive optional ledger. The runtime captures before native transitions and saves, restores at the analytically advanced interior position, and removes the body only after physical doorway completion. Local schedules and foreign trip queries consult the continuation. Displacement from a reached activity pauses its dwell and requests a route back.

Core suite passes 553/553 and Unity build/integration succeeds. Native serialization and same-door exit/reentry tests are now running; these changes are not yet accepted as a complete fix. Missing activity routes, missed opening windows after delays, and overnight-room movement remain separate unresolved cases. The native test must also verify that no stale scene objects or fixture schedules invalidate its evidence.
