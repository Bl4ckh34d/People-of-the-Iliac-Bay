# Daggerfall daytime population — high-priority investigation

## Verified evidence

The user reports that Daggerfall's market, streets and Inns are much less lively around noon to 14:00 than in an earlier build. The archived actual Daggerfall log `work/player-before-town-8d7300fc1b8e4c4eaffbc075e9e93de7.log` confirms 290 scheduled residents for map 1291010263. It includes deferred old-route reconstruction warnings. Those warnings preserve the prior routines; they are not evidence of deletion. The most recent healthcare test log is Gothway Garden and must not be presented as this Daggerfall session.

Read-only inspection of SAVE18, `People - Daggerfall day 2`, finds schema 30, 290 saved routines, 221 multi-stop itineraries, six single-destination fallbacks and 63 without outdoor routes. The route-less residents include 29 adults and 34 children. Children's lack of adult errands is not itself a defect.

The current compiled Core, applied to the 221 saved itineraries without changing the save, gives these citywide planned street counts (Walking, Leaving or WaitingForOpening):

| Time | Saved timing | Hypothetical published Spread timing |
|---|---:|---:|
| 10:00 | 62 | 16 |
| 12:00 | 27 | 21 |
| 13:00 | 12 | 20 |
| 14:00 | 5 | 13 |
| 16:00 | 0 | 15 |
| 18:00 | 0 | 4 |

At 14:00, 204 of those 221 saved itineraries have finished; at 16:00 all have finished. These are analytic citywide itinerary projections, not measured rendered NPCs at the market. They exclude the six fallback routines, route-less people, travelers, native static actors, guard actors and runtime interruptions. Evidence: `work/daggerfall-save18-schedule-audit.json`.

The corresponding saved local tavern projections are 43 patrons across 16 Inns at noon, 20 across 14 at 13:00, five across four at 14:00 and zero at 16:00. Hypothetically publishing all eligible Spread changes yields 40/14, 26/13, 32/15 and 13/9 respectively (patrons/Inns). These numbers reinforce the early-finish issue and show why a better citywide distribution does not guarantee a busy individual Inn. They do not certify actual counter, wallet or seat execution.

## Source explanation

- `StreetPopulation.FinishBuild` hides vanilla mobile civilian passersby once replacement plans exist. Crowd density therefore depends on the mod's actual schedules.
- `DailyDepartureSeconds` creates initial departures between 08:00 and 10:00. A bounded sequence of errands finishes, then the resident stays home. It does not create repeated afternoon market or evening tavern visits.
- `DailyOutingDistribution.Spread` spreads existing departures within service hours. It neither extends the number of visits nor supplies an outing for a route-less resident. On this save it improves afternoon coverage but reduces simultaneous morning/noon traffic; it cannot by itself recreate the former lively market.
- Saved routine changes publish only while both old and candidate effective schedules place the resident at home. Route reconstruction, personal-lane preparation and duties can delay publication. This protects continuity, but a loaded old save does not immediately show every planned timing improvement.
- `RepairMissingAdultOuting` attempts real routes for eligible route-less adults; unavailable routes defer rather than creating fake population. Children and native/quest-owned actors remain excluded.
- Local taverns are `ResidentVisitPurpose.Social`; `Inn` is not the correct counter for these local social visits. Inn crowd analysis must use the actual tavern building and the counter/purchase/seat/exit lifecycle, not conclude there are zero tavern visits from the overnight-purpose count.

## Remaining work

Household calculation on the same untouched SAVE18: 214 inhabited households, 290 living residents (256 adults, 34 children). 227 adults have a saved exterior outing: 88.7% of adults and 78.3% of all residents. 193 of 214 inhabited households have at least one such outing (90.2%); 170 have outings for every living member; 21 have none. The remaining household records are not automatically evidence of occupied homes or missing people. Evidence: `work/daggerfall-save18-household-outing-audit.json`. These are planned daily outings, not simultaneous presence outside.

Find the precise historical regression using comparable saved time, location, routes and package evidence. The current inspection establishes a scheduling/coverage deficit; it does not identify a single change that caused every aspect of the perceived regression. Measure live market and Inn counts at 10/12/14/16, including every suppression reason. Prioritize useful recurring activity blocks, local destination choice, ordinary adult route coverage and native Inn purchase/seating acceptance. Retain exact identities, finite resources, higher-priority duties and physical continuity. Do not compensate by blindly raising population caps or enabling duplicate vanilla civilians.
