# Sparse streets: native measurements, 26 October 2026

The player's report is reproducible in the copied SAVE82 town. The actor cap is
not the demonstrated bottleneck. The original native population pool is replaced
by persistent managed residents; their schedules therefore determine street life.

## Controlled measurements

`tools/ProfileInEditor.ps1 -RunName street-density-02 -CountersOnly -MeasureStreetDensity`
keeps the same saved observer location in Daggerfall (map 1291010263), advances the
clock to each test time, and allows 30 real seconds for physical reconstruction.
All 286 plans are considered. The stable samples are:

| Requested time | Scheduled outdoors, whole town | At home | Visiting interiors | Active bodies near observer | Standing |
| --- | ---: | ---: | ---: | ---: | ---: |
| 09:10 | 33 | 197 | 56 | 16 | 3 |
| 10:15 | 47 | 145 | 94 | 10 | 0 |
| 12:00 | 17 | 234 | 35 | 8 | 0 |
| 15:00 | 3 | 128 | 155 | 0 | 0 |

At each settled sample, cap deferrals, spawn-budget deferrals, family preparation,
simulated-family ownership and action ownership are zero. Immediately after a
jump the bounded spawn budget defers bodies, then catches up. The limits remain
80 actors, 140 m spawn radius and 165 m retention radius. Active bodies are not a
camera visibility count; guards, scenery and unrelated mods are not included.
The editor exits with `complete` and the density completion marker.

This supports investigating schedules and activity/location distribution. It
does not justify increasing the spawn cap, prove a city-wide deficit at every
time, or prove that all missing street activity has one cause.

## Timing experiment

`DailyActivityWindows.Create` previously starts every optional slot within at most
15 minutes of its common boundary. The experiment spreads seeded departures over
the available window while retaining at least 45 minutes for travel/visits and the
existing 20-minute home break. Native opening, childcare and return checks remain
in the planner. The 512-seed regression fails before the change and passes after
it; the complete Core suite is 2129/2129 and RuntimeCompile has no errors/warnings.

Only newly prepared additions use these windows. Existing persisted outings and
occupied physical routes retain their times. The experiment must not be described
as a complete density fix. The identical `street-density-03` probe completes:

| Requested time | Scheduled outdoors, whole town | At home | Visiting interiors | Active bodies near observer |
| --- | ---: | ---: | ---: | ---: |
| 09:10 | 34 | 197 | 55 | 17 |
| 10:15 | 47 | 145 | 94 | 10 |
| 12:00 | 18 | 233 | 35 | 9 |
| 15:00 | 6 | 134 | 146 | 1 |

The change is small and does not solve the sparse afternoon. These are single
runs of a live simulation, not a statistical density guarantee. The next step
is reviewing real outdoor activities and the distribution of existing daily
plans, including home gaps and interior dwell. Do not inflate body limits to
hide a planning problem.

## Single-destination planning repair

The copied source contains 64 adult routines with a route but no itinerary. They
receive only a short `DayVisit`. The optional outing planner deliberately rejects
that unconverted shape, so these residents never receive additional daily goals.
This fallback is also produced by ordinary route generation, not only old saves.

The native preparation now gives an ordinary adult's verified destination and
personal route to the existing bounded itinerary planner. It retains native
service hours, preserves the actual outbound/reversed return route, and schedules
social visits no earlier than 16:00. Native-owned and protected people are excluded.
The normal expansion can then add later reachable activities. Publication still
requires a safe home state in both schedules and existing childcare/appointment
checks. No body cap, actor duplication or in-progress route replacement is added.

Two failing Core cases first expose the absent conversion path. The final tests
verify native-hour admission, social daypart, insufficient time, detached route
ownership and subsequent expansion; all 2131 Core tests pass. RuntimeCompile has
no warnings/errors. `street-density-04` uses the same native probe and ends with
`complete`, its completion marker, and no exception entries. All 64 candidates
are prepared, 56 gain additional outings and 57 have published at home by the end
of the run; the remaining candidates are not forced into active bodies.

| Requested time | Scheduled outdoors, whole town | At home | Visiting interiors | Active bodies near observer |
| --- | ---: | ---: | ---: | ---: |
| 09:10 | 34 | 197 | 55 | 17 |
| 10:15 | 52 | 129 | 105 | 11 |
| 12:00 | 25 | 229 | 32 | 12 |
| 15:00 | 11 | 122 | 153 | 4 |

The afternoon improves from the original three outdoors/zero nearby to eleven
outdoors/four nearby. It remains sparse. Additional varied outdoor activities and
the balance of home gaps/interior dwell remain open. These sample points do not
prove smooth continuous foot traffic or acceptance in other settlements.

Unity package verification/build succeeds and installs bundle SHA256
`4F4349E44CF4D677C0B5FD632E468B4885D414E40879EEDC624ADF4ADCD85D90`
at 11:05. Distribution and Player hashes match. The bundle also contains the
previous source checkpoint's shop-wait and planned family funding changes; native
morning family-bank acceptance remains open.

## Separate performance observation

`street-density-01` runs the normal native 1h/3h loiter profile. The 3h skip peaks at
1607 ms, while the following 20 seconds peak at 247 ms. This is an Editor measurement;
it does not establish that the Player has the same cost or resolve the separately
reported 4–6 second Player freezes. Existing climate-lookup investigation remains
applicable. No GPU computation or population-limit change was made.

The installed-source follow-up `street-density-05-perf` completes the same 1h/3h
native loiter sequence, exits its owned Editor and has no exception entries.
Existing Expanded Textures material errors still appear during startup.

| Phase | Baseline p50 / p95 / maximum ms | Follow-up p50 / p95 / maximum ms | Baseline / follow-up gen0 collections |
| --- | --- | --- | --- |
| After 1h | 20.67 / 47.26 / 272.13 | 24.74 / 54.96 / 272.80 | 7 / 13 |
| 3h skip | 11.57 / 19.36 / 1607.00 | 11.77 / 19.52 / 1720.89 | 1 / 1 |
| After 3h | 20.68 / 48.60 / 246.98 | 24.68 / 56.99 / 219.15 | 8 / 14 |

The final active body count rises from 12 to 14. These are single Editor runs,
not proof of performance equivalence or a causal attribution to the new planner.
Typical post-skip frame times and collections are higher despite a lower final
maximum. During the large skip, measured mod-update peaks at 3.31 ms; the remaining
1.72-second whole-frame peak is not explained by that section. Performance remains
open, including repeatable Player measurements and further section-level profiling.
