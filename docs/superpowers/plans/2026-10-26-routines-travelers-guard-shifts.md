# Routine timing, traveler gates and guard shifts

**Goal:** Resolve early shop queues and disconnected traveler routes; provide real day/night guard coverage with a documented staffing policy.

**Architecture:** Reuse native service hours, surveyed doors, Basic Roads and the existing guard roster. Route and timing changes must preserve physical ownership, native collisions and active journeys. Do not add replacement population or speculative navigation systems.

**Authorization:** The user explicitly requests parallel work by two Astra/high agents on 26 October, overriding the earlier no-agent request for these two assignments. The main agent owns routine timing and integration. Native builds and runs are serialized, initially owned by the traveler agent.

## Routine timing (main agent)

- [x] Inspect actual destination histograms, planner admission, native hours and retained itinerary timing.
- [x] Confirm the copied SAVE82 contains early clothing-store arrivals in multi-outing plans and `DailyOutingDistribution.Spread` skips every such plan.
- [x] Add a regression for multiple outings after service-hour/path-duration changes, including following outings and closing/home deadlines.
- [x] Correct departure timing using existing feasible windows; retain seeded variation and avoid changing occupied journeys.
- [ ] Compare 09:10, opening and post-opening native observations. Report fresh plans separately from retained test plans and count actual arrival versus clamped entry.
- [ ] Observe all shop entrances, then diagnose post-time-skip doorway congestion using actual distance and navigation state; preserve wall/door identity.

## Traveler paths (traveler_gate_routes)

- [x] Run the existing RED diagnostic on actual four guard posts and all target-Inn doors.
- [ ] Fix reachable entrance selection and prove the distinct gate disconnection cause.
- [ ] Verify open/closed gates, both directions, normal traveler admission and guard compatibility.
- [ ] Reuse Basic Roads connections for onward travel; document supported behavior and remaining gaps.

## Guard staffing (guard_roster_shifts)

- [x] Audit current counts, fixed posts, patrols, bows and actual wall quarters.
- [ ] Allocate distinct day/night staff with posts and patrols in each shift, maintaining emergency precedence.
- [ ] Use real accessible wall positions and beds where the installed geometry supports them. Do not substitute invented positions for missing routes.
- [ ] Verify shift changes, night torches and rest; report before/after counts by settlement type.
- [ ] Extend civilian guard coverage to farms, temples, taverns and wealthy homes; exclude poor homes, cults, covens and dungeons. Keep resident population admission unchanged.

## Integration

## Newly reported defects and remaining main-agent work

- [ ] Reproduce the actual Inn 263174 crowd with all actor sources, especially pending traveler admissions. The earlier local-resident-only observer cannot establish this behavior.
- [ ] Correct native room partition/marker ownership and verify real multi-bed accommodation, admission, physical entry and an occupied interior. Do not report an observation-only probe as a fix.
- [ ] Temporarily unload stationary exterior NPCs without households at night and restore them in the morning; preserve their identity/state and exclude actors already managed by households or guard duties.
- [ ] Make curse-owned haunting enemies target outdoor NPCs and guards through native combat; verify separately from guard shelter policy.

## Final integration checks

- [ ] Review each change, run focused regressions, Core and runtime compilation, then native tests in sequence.
- [ ] Update README/roadmap only for delivered behavior, preserve test limitations and capture reusable findings in Obsidian.
- [ ] Build/install with the game closed, verify package, commit and push the reviewed changes; provide a concise test checklist and guard-count table.
