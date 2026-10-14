# Unwitnessed household murder discovery

The approved rule is departure-triggered discovery: an unobserved household killing does not immediately identify the player or summon guards. Once the player leaves, finite guards investigate the house. Their arrival clears the corpse and starts one game hour of scene occupation. Returning during that window triggers custody behavior without claiming that guards witnessed the murder. Repeated reports and reloads must not renew the timer.

Implementation order:

1. Persist a scene only for a confirmed dead generated resident in their home with no murder witnesses. Persist first departure and first confirmed guard arrival independently of the short civilian alarm window.
2. Add investigation reports to the finite roster with one-hour occupation deadlines, immutable report type and normal physical approach/return routes.
3. Extend the native weapon finalizer to preserve the previous known crime for successfully concealed murders. Suppress native instant dispatch only while the complete supported replacement is active.
4. Observe real departure, dispatch existing guards against the saved approach, record arrival, clear corpse presentation and connect re-entry to native custody. Validate actual save/load, no early response, occupation expiry and prior-crime preservation.

The 0.3.10 adapter connects confirmed generated-resident home deaths to finite investigation. The scoped completion preserves previously known crime. Confirmed transition events record departure immediately; off-screen travel advances independently of actor ownership. Investigators remain virtually inside their destination after arrival and appear through the surveyed entry on re-entry. Seeing the player there sets Trespassing and starts native guard engagement, without labeling the player as the known murderer.

Current occupation policy is one game hour, overriding the earlier one-hour request on 14 October 2026. Existing preview saves with an exact one-hour active occupation floor are normalized in memory; files are not rewritten by loading. Active pursuit deadlines and completed history are retained. Corpse visuals/cleanup, stationary victims, broader layouts and household recovery remain unfinished.

## Verified core foundation

The incident ledger now optionally owns an `UnwitnessedScene`. Registration requires a confirmed dead resident, a known crime position in that resident's own home and no murder witnesses. An explicit location observer records first departure once, even after the normal civilian alarm has expired. Explicit guard arrival records first discovery once; `Occupied(now)` lasts exactly one game hour from that arrival. Malformed chronology, living victims and witnessed murders are rejected, and failed restore leaves prior state intact. Legacy incidents do not acquire scenes retroactively.

Finite building reports optionally use investigation policy. They keep normal physical travel deadlines, occupy for one hour from arrival, and retain report type and responder identities through repeated calls and reload. Ordinary response deadlines remain five minutes. An indoor suspect sighting cannot shorten an investigation's one-hour floor, though active combat can extend the usual search beyond it. Guards eventually retrace their normal return path.

Validation: 159/159 core tests and `tools/VerifyGuards.ps1` pass. DFU's native FullSerializer retains scene departure/discovery chronology, investigation type and one-hour deadlines. Evidence: sibling `../work/guards-persistence.log`, markers `LIVING_CITIES_SCENE_PERSISTENCE_PASS` and `LIVING_CITIES_GUARDS_PERSISTENCE_PASS`. This core prerequisite originally left installed 0.3.9 unchanged; runtime verification is recorded below.

Review hardening: null location means unknown and cannot trigger departure; explicit -1 means exterior. Investigation reports retain first actual arrival when a living responder transitions from Responding to Searching. Discovery requires that evidence, uses its original timestamp and survives late catch-up after guards have returned. Dead en-route responders cannot discover the scene. Save restoration cross-checks discovered scenes against the matching investigation report. Historical responder IDs remain in old reports after return so repeated reports cannot replenish guards; membership alone is never proof of arrival.

Native integration constraints: native WeaponDamage assigns Murder and calls SpawnCityGuards before SetHealth(0). A before-hit hook alone cannot confirm death or restore the prior known crime. Use a scoped finalizer and verify normal/exception/nested restoration. Detect departure before clearing town state. Call discovery only on an actual living responder's arrival; an empty roster must never fabricate arrival. Keep investigation guards virtually inside while their building is unloaded, materialize them through the surveyed entry on re-entry, and preserve the original absolute occupation deadline. Arrest interaction and corpse presentation require actual-player probes before publishing the feature.

Arrival validation also retains a per-responder scheduled-arrival ledger. Restore rejects invented arrival flags that contradict active travel or deaths before arrival, while preserving completed evidence after guards return or take later assignments.

## Native damage completion prerequisite

`NativeCivilianHooks` now accepts a per-hit begin callback returning an `Action<Exception>` completion. The completion runs after native WeaponDamage, with its own target still available. Nested hits restore the enclosing target; native and prefix failures also restore scope. Completion errors propagate when native damage succeeded, but cannot replace an existing native exception. The original before-hit constructor remains compatible.

`tools/VerifyGuards.ps1` exercises the actual Harmony-patched native method with inert targets, nested hits, a null-target native exception, prefix failure, completion failure and the legacy constructor. Marker: `LIVING_CITIES_CIVILIAN_SCOPES_PASS`. This validates the hook contract, not actual murder concealment: the runtime adapter still must capture the prior crime, confirm the generated victim died, register the scene, and suppress immediate response only for a fully supported investigation.

## Runtime verification

`TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -CrimeSceneProbe` exercises a native kill in a surveyed one-resident home, no premature report, preservation of an existing Theft crime, synchronous departure observation, finite physical dispatch, arrival discovery, re-entry custody trigger, real SAVE33 reload and occupation expiry. SAVE33 is `Living Cities - crime scene investigation`. The player test accelerates later travel and occupation deadlines. It validates the custody trigger, not every arrest-dialogue outcome. Generated dead actors still disappear immediately; persistent corpse rendering and timed visual cleanup are the next missing part of this requirement.

Verified 0.3.10 bundle: SHA-256 `A7FC5B219225F4F0EA1E88B1E1371E34355BE4400EDF1EB9437E44A426D24BDE`. Core suite: 159/159. Actual-player crime-scene and indoor-witness regression logs are `work/CrimeSceneProbe-0310-pass.log` and `work/InteriorEscapeProbe-0310-pass.log`.
