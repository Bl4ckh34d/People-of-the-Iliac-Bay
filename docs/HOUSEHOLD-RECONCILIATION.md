# Household reconciliation and entrance navigation

## Verified 0.3.15 checkpoint, 14 October 2026

The three-bed residential fixture at Gothway Garden building 196864 contained two untouched generic native StaticNPCs as well as its three generated residents. These were extra occupants, not intentionally scheduled house guests. HousingProbe first failed the total occupancy check. HouseholdOccupants now suppresses unclaimed faction-zero, building-context static flats in ordinary mapped family residences while the authoritative saved family is represented. Active quest resources, actions, faction listeners and nonzero factions are excluded; shops, taverns, castles and guilds are outside this rule. Original objects and active states are restored on room change, Clear and destruction. This is a population reconciliation checkpoint, not completed original stationary appearance/identity conversion. That conversion must reuse family/service ownership without reintroducing duplicates.

Enter markers in this layout led the interior graph to the attic, placing awake residents at local y=4.2 while native downstairs occupants stood at y=1.0. Using a lower ray origin alone did not resolve it: the marker itself was upstairs. InteriorGeometry now tries the actual interior exit door and inward normal before enter-marker fallbacks, following the native player's landing method. The live fixture has three residents at local y=1.0 and a validated route from each to the exit. Full navigation across stacked storeys/stairs remains unfinished; this check establishes correct entrance-floor placement, not a complete multilevel solver.

The female family member Elyzyssa Hawkcroft opens native dialogue with female portrait record 33; a rendered screenshot was inspected. The exact target in the user's original female-flat/male-portrait pair was not conclusively identified. Do not treat this one-person check as exhaustive portrait/activation coverage. Artifact: the local test player's `LivingCities-female-dialogue.png` under the standard Unity persistent-data directory.

## Reporting and controls

Managed generated civilian weapon hits no longer fall back to an immediate guard report because an escape path is unavailable. Active indoor direct witnesses retry route preparation every half real second. InteriorEscapeProbe removes the graph for the native hit, proves no guard report, restores it, then verifies autonomous preparation, physical escape, delayed finite response and real save/load. Finite guards now default on in the supported town; explicit user settings can still disable them. Disabled or unsupported guard behavior remains native.

An active guarded crime scene uses the agreed ten-second warning instead of the family lock/burglary rule. It releases the routine lock during the one-hour investigation window. CrimeSceneProbe verifies re-entry warning, subsequent Trespassing, native surrender, save/reload and expiry. Existing crimes retain their normal arrest behavior.

The local portable player used `WeaponSwingMode = 0`, the native hold-and-drag mode. It was changed to mode 1, native click-to-attack, for the user's testing. This is a local preference change and is not shipped as an engine or mod input patch. Hardware click responsiveness remains a manual acceptance check.

Evidence: 182 core tests; native build/integration; `work/HouseholdOccupants-0315-pass.log`, `work/EscapeRetry-pass.log`, and `work/CrimeScene-0315-pass.log`. Focused read-only review found no confirmed important defect in reconciliation, restoration and retry behavior. Broader quest/access and stationary-service acceptance remain open.
