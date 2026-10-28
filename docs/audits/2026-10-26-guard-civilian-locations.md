# Guard admission at native civilian locations

The user requested guards at civilian locations and explicitly excluded Cult/Coven for now. A later correction admits standalone HomeWealthy but excludes standalone HomePoor. This extends municipal guard admission without changing resident-family simulation or ordinary traveler-origin classification. It does not exclude poor neighborhoods from city-wide patrols.

| Native location type | Previous guards | New guards | Guard scale |
| --- | --- | --- | --- |
| 0 TownCity | Yes | Yes | City |
| 1 TownHamlet | Yes | Yes | Town |
| 2 TownVillage | Yes | Yes | Hamlet |
| 3 HomeFarms | No | Yes | Hamlet |
| 5 ReligionTemple | No | Yes | Hamlet |
| 6 Tavern | No | Yes | Hamlet |
| 8 HomeWealthy | No | Yes | Hamlet |
| 11 HomePoor | No | No | Excluded |
| 4, 7, 10 dungeon categories | No | No | Excluded |
| 9 ReligionCult, 13 Coven | No | No | Excluded |
| 12 Graveyard, 14 player ship, 65535 None | No | No | Excluded |

`SettlementClassification.SupportsPopulation` remains limited to 0–2. New `SupportsGuards` and `GuardScale` functions serve only guard admission and census sizing. The added categories use the existing hamlet quota and two-shift allocation, which yields a minimum of six guards, three per shift, even with an actual zero-household census. No household or resident is invented to obtain that minimum.

`GuardPopulation.SetTown` no longer requires an unused resident-routine object. The host can start guard surveys at these sites once native CityNavigation is ready. Existing populated towns retain their previous street-readiness gate, avoiding earlier sizing against a partially recovered town census.

Guard posts and patrols still require the existing physical surveys. Admission does not manufacture positions or promise coverage where native geometry cannot provide supported posts and routes.

## Verification

Two focused tests failed before the change: civilian type3 was rejected and its guard scale threw. The complete isolated Core suite then passed **2173/2173**. Tests cover the explicit admission/exclusion matrix, unchanged family admission, six guards across two shifts at a real empty census, and absence of inserted households. Evidence: `work/guard-location-red.log`, `work/guard-location-green.log`.

The later HomePoor correction has its own observed RED (`Unexpected guard type 11`) before removing type11 from guard admission. Evidence: `work/guard-location-poor-red.log`; the corrected full-suite result is recorded in `work/guard-location-poor-green.log`.

`GuardCivilianLocationProbe`, dispatched as `guard-locations`, is prepared but has not yet run. It discovers actual native Farm, Temple, Tavern, Dungeon and HomePoor locations in the fixture's current region and uses ordinary native streaming. It checks production guard creation, surveyed native home keys, census-derived quota, both shifts, reachable posts and actual patrol templates, then verifies that the dungeon and HomePoor site acquire no municipal roster. It inserts no households, guards, posts or routes.

Native runtime compilation, site-specific coverage and the six-body minimum on those three actual maps remain pending. No commit or push was performed by this worker.
