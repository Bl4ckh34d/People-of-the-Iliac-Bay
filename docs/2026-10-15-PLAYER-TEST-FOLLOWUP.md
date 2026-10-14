# Player test follow-up, 15 October 2026

## Sleeping alignment

Three screenshots and the eight-second recording show a sleeper rotating and sliding across the mattress. `SleepingPose.LateUpdate` faced the camera and rescaled the drawing. The old regression checked visibility, not bed alignment. The player explicitly requests no camera-facing rotation or view swapping.

The new drawing uses one existing complete face/pillow/green-blanket composite on a fixed horizontal bed-aligned plane. This is an interim flat representation; volumetric artwork is not complete. Editor regression failed before the change (`work/SleepAlignment-red.log`) and passes afterward. Both male and female live tests pass fixed alignment from multiple camera positions, physical mattress bounds, wake/toggle/collider restoration, raycast interaction, death and real save/load (`work/SleepAlignment-runtime-pass.log`). Native keeper response, reserved-bed sleep, service blocking, wake/collider restoration, death and reload also pass (`work/SleepAlignment-keeper-pass.log`). Core tests pass 227/227. The first female run exposed an obsolete assertion requiring the whole mattress to fit the screen even at close range; it is replaced by physical mattress bounds.

## Indoor hearing and waking — pending implementation

Source foundation now implements `NoiseAwareness.WakeChance` and saved `HearViolence` checks, with explicit hearing/awakening flags separate from direct visual witnessing. Violence has the highest wake chance; higher Stealth and distance reduce it. Negative checks persist without reroll, observers arriving more than five game seconds later cannot hear an old attack, and legacy incidents receive no invented hearing. Hearing-only recipients flee and cannot relay to more NPCs. Core237/237 and DFU native FullSerializer/legacy-field checks pass (`work/IndoorNoise-pass.log`, `work/IndoorNoise-native-pass.log`). This is not connected to live actor noise or guard reporting yet; installed0.3.26 remains unchanged. The next integration must extend scene ownership: current reporting assumes direct witnesses and charges Murder, while unidentified scene registration currently requires zero recipients. Do not wire hearing into that path without neutral investigation support. Plan: `superpowers/plans/2026-10-15-indoor-hearing.md`.

The player can kill occupants in separate rooms without alerting the others. Confirmed source cause: `CivilianResponses.Murder`, `BeginKeeperHit` and `ObserveKeeperViolence` require visual line of sight and skip sleepers. Add an audible violence stimulus within the same building. Awake occupants should investigate, flee to report, or fight according to capability. Keep physical exit/report journeys and finite guard travel time; never instantly spawn guards downstairs.

Sleeping occupants have a chance to wake from noise and subsequently report. Use native **Stealth** for quiet movement and theft noise. Weapon hits, struggles and shouting should greatly increase wake chance. Account for noise intensity, distance and sleep state. Roll once per discrete event, not every frame, and preserve waking/reaction state through save/load. Exact probabilities remain to be implemented and tested.

Hearing a disturbance does not identify an unseen attacker. Preserve the agreed crime-scene policy, lawful self-defense and bounded outdoor two-person relay. Newly awakened occupants should investigate, with sight establishing direct attacker identification.

## Shop occupants and unkillable elder — investigation pending

Reported shop: woman named Kinghart, another person named Hearthfield, and a service keeper whose name is not visible. Inspect saved ownership, activity, current building and native hash before deciding whether the differently named person is a shopper or an incorrectly housed resident. Keeper families share the keeper's surname; resident capacity remains one per actual bed. Expose the keeper's full identity while retaining native services.

The screenshot shows an elderly green-robed native flat that cannot be killed. Current registration only includes merchant-social-group NPCs in shops. Damage handling requires a registered identity and excludes native quest/action ownership or actors with their own entity. Unregistered generic static flats retain vanilla damage behavior. The exact screenshot actor is not yet identified; do not claim its specific exclusion is proven. Add a live identity/damage regression before widening coverage. Ordinary stationary NPCs need identity and mortality; preserve main-quest/castle placement.

## Daggerfall city — requested extension, pending

Add Daggerfall city alongside Gothway Garden, with distinct settlement state and disposable test saves. Native curse quest `S0000977` uses global variable **LiftedCurse**, table ID **0**, and ghost danger hours **19:00–05:00**. Unresolved haunting suppresses night patrols and late civilian activity; exposed guards flee toward verified reachable refuges rather than fighting ghosts. Solving the curse restores supported normal behavior. Full tavern nightlife and traveler lodging remain separate unfinished systems. See `DAGGERFALL-HAUNTING.md`.
