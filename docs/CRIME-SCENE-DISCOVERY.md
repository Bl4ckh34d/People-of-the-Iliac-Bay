# Unwitnessed household murder discovery

The approved rule is departure-triggered discovery: an unobserved household killing does not immediately identify the player or summon guards. Once the player leaves, finite guards investigate the house. Their arrival clears the corpse and starts six game hours of scene occupation. Returning during that window triggers custody behavior without claiming that guards witnessed the murder. Repeated reports and reloads must not renew the timer.

Implementation order:

1. Persist a scene only for a confirmed dead generated resident in their home with no murder witnesses. Persist first departure and first confirmed guard arrival independently of the short civilian alarm window.
2. Add investigation reports to the finite roster with six-hour occupation deadlines, immutable report type and normal physical approach/return routes.
3. Extend the native weapon finalizer to preserve the previous known crime for successfully concealed murders. Suppress native instant dispatch only while the complete supported replacement is active.
4. Observe real departure, dispatch existing guards against the saved approach, record arrival, clear corpse presentation and connect re-entry to native custody. Validate actual save/load, no early response, occupation expiry and prior-crime preservation.

The core state alone does not activate this behavior in the player. Until the adapter is complete, the installed 0.3.9 bundle retains its documented unwitnessed-murder fallback. Corpse visuals, stationary victims, broader layouts and household recovery remain part of the full goal.

## Verified core foundation

The incident ledger now optionally owns an `UnwitnessedScene`. Registration requires a confirmed dead resident, a known crime position in that resident's own home and no murder witnesses. An explicit location observer records first departure once, even after the normal civilian alarm has expired. Explicit guard arrival records first discovery once; `Occupied(now)` lasts exactly six game hours from that arrival. Malformed chronology, living victims and witnessed murders are rejected, and failed restore leaves prior state intact. Legacy incidents do not acquire scenes retroactively.

Finite building reports optionally use investigation policy. They keep normal physical travel deadlines, occupy for six hours from arrival, and retain report type and responder identities through repeated calls and reload. Ordinary response deadlines remain five minutes. An indoor suspect sighting cannot shorten an investigation's six-hour floor, though active combat can extend the usual search beyond it. Guards eventually retrace their normal return path.

Validation: 159/159 core tests and `tools/VerifyGuards.ps1` pass. DFU's native FullSerializer retains scene departure/discovery chronology, investigation type and six-hour deadlines. Evidence: sibling `../work/guards-persistence.log`, markers `LIVING_CITIES_SCENE_PERSISTENCE_PASS` and `LIVING_CITIES_GUARDS_PERSISTENCE_PASS`. Installed bundle remains 0.3.9, SHA-256 `C6A5D3105F03CAA727D0C205A99A6E25C6F81CE4BFACCFEE0C23748CCEB01283`.

Review hardening: null location means unknown and cannot trigger departure; explicit -1 means exterior. Investigation reports retain first actual arrival when a living responder transitions from Responding to Searching. Discovery requires that evidence, uses its original timestamp and survives late catch-up after guards have returned. Dead en-route responders cannot discover the scene. Save restoration cross-checks discovered scenes against the matching investigation report. Historical responder IDs remain in old reports after return so repeated reports cannot replenish guards; membership alone is never proof of arrival.

Next adapter details: native WeaponDamage assigns Murder and calls SpawnCityGuards before SetHealth(0). A before-hit hook alone cannot confirm death or restore the prior known crime. Use a scoped finalizer and verify normal/exception/nested restoration. Detect departure before clearing town state. Call discovery only on an actual living responder's arrival; an empty roster must never fabricate arrival. Keep investigation guards virtually inside while their building is unloaded, materialize them through the surveyed entry on re-entry, and preserve the original absolute occupation deadline. Arrest interaction and corpse presentation require actual-player probes before publishing the feature.

Arrival validation also retains a per-responder scheduled-arrival ledger. Restore rejects invented arrival flags that contradict active travel or deaths before arrival, while preserving completed evidence after guards return or take later assignments.
