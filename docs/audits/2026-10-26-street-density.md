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

## Separate performance observation

`street-density-01` runs the normal native 1h/3h loiter profile. The 3h skip peaks at
1607 ms, while the following 20 seconds peak at 247 ms. This is an Editor measurement;
it does not establish that the Player has the same cost or resolve the separately
reported 4–6 second Player freezes. Existing climate-lookup investigation remains
applicable. No GPU computation or population-limit change was made.
