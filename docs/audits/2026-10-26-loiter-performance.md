# Loiter performance measurements, 26 October 2026

## Reproduction and limits

The user reports four-to-six-second freezes after loitering. Measurements use
Unity 2019.4.40f1, the native rest window, a copied `SAVE82` morning fixture,
the player's mod order/settings and 64 loaded native navigation modules. Each
observation lasts 20 real seconds. The installed player bundle was not replaced
for these editor experiments. No fixture is saved.

Initial CPU captures exposed two separate costs: NPC inventory initialization
and whole-city building-data reads during passive interior surveys. The latter
was amplified by a DFU editor difference: `RMBLayout` disables its 12-location
cache under `UNITY_EDITOR`. The local profiling-only host guard enables exactly
the player's cache, including normal invalidation. Initial five-second interior
reads must not be presented as release-player measurements. With that cache,
the measured assignment takes at most 0.21 ms in the baseline below.

The overnight extension was interrupted by the native loiter flow. It is not
accepted as a successful overnight test. The completed one/three-hour phases
and the separate completed default runs remain available independently.

## Controlled source comparison

The completed runs `baseline-player-cache-compiled` and `after-player-cache`
use player-equivalent caching and **no binary profiler recording**. The only
deployed source difference between these two runs is that native starting
belongings call a scalar home-ownership query instead of creating full world
snapshots. The world save contains about 173 MB of serialized mod state.

| After three-hour loiter | Before | After |
| --- | ---: | ---: |
| Inventory initialization calls | 13 | 14 |
| Total inventory initialization | 14,205.05 ms | 318.47 ms |
| Mean inventory initialization | 1,092.70 ms | 22.75 ms |
| Maximum inventory initialization | 1,208.32 ms | 29.37 ms |
| Observed frames in 20-second phase | 236 | 787 |
| Frame p50 | 20.65 ms | 21.48 ms |
| Frame p95 | 1,138.88 ms | 49.74 ms |
| Maximum frame | 1,308.29 ms | 219.30 ms |
| Gen-0 collections | 33 | 7 |

Faster processing allows more game-time progress and one extra resident in the
same wall-time window. These are observed frame distributions, not guarantees
for every settlement or hardware configuration. The median barely changes;
the removed cost is repeated long stalls during initialization.

The earlier CPU-recorded comparison also sampled 13 inventory initializations
in each run: 15,025.22 ms before and 331.82 ms after. Native hierarchy exports
showed large temporary allocations before the fix; counters-only results above
avoid capture-file I/O as a confounder.

## Remaining work

- A later [housing survey comparison](2026-10-26-housing-cache-performance.md) removes a separate 3.1-second startup stall: housing reread 34 blocks already present in DFU's native cache. The combined startup scope falls to 8.92 ms with exact parity for all 408 home profiles. This is distinct from the inventory and time-skip measurements above.

- Native `DaggerfallLocation.Update()` still produced roughly 1.6-second spikes
  during the time skip. Its source switches city-light/season textures; this is
  separate from the corrected inventory ownership read.
- Other remaining spikes require attribution, including loaded model creation,
  guard updates and cold remote-town block reads. No compute-shader rewrite is
  justified by the measurements collected so far.
- Further source reuses DFU's current block cache, avoids repeated block decoding
  within one incremental read, and allows one travel-planning step during native
  loitering. Its final native regression is tracked in `docs/TESTING.md`.
- The later `crowd-flight-after` native fixture separates six overlapping bodies
  and rejects a sign-obstructed flight corridor. Closed-shop town-wide wandering,
  all family entrance cases and returning-greeting audio need separate acceptance.

The old shop fixture's 24-game-minute visit does not fit its 180-real-second
deadline under the current 2x game clock. That timeout is not proof of a stopped
movement system. The focused fixture now schedules three game minutes and keeps
its actual shelf, counter, payment, inventory and continuous-exit assertions.
Its old SAVE1 also logs a rejected stale lodging layout during unrelated travel
planning; no obsolete-save migration is included in this work.

## Evidence and reproduction

Artifacts are in `work/playtest-2026-10-26/editor/<run>/`: `editor.log`,
`result.txt`, `summary.json`, `frames.csv`, `sections.csv`, and optional phase
`.raw` / native hierarchy `.raw.csv` files. `tools/ProfileInEditor.ps1` starts
owned fixtures; `tools/SummarizeEditorProfile.ps1` parses literal diagnostic
fields and preserves failed-run status. See `tools/Editor/README.md` for the
two local DFU host guards, profiler limits, and exact usage.

## Loaded-Inn follow-up before the requested playtest pause

Root added opt-in native Profiler/Stopwatch scopes around loaded-Inn preparation,
native occupant block reads/claims and admission reconciliation. Normal play does
not activate these measurements. The `inn-refresh-cost` trial calls the actual
existing refresh eight times, after a two-real-second entry wait; ordinary
refresh remains enabled. It neither inserts guests nor changes the clock.

Both controlled Player runs on packageD3CA68C3D919 exit0:

| Loaded Inn | Explicit refresh calls | Total / maximum (ms) | Block-read maximum (ms) | Admission maximum (ms) |
| --- | ---: | ---: | ---: | ---: |
|197386|8|6.86 / 0.92|0.01|0.80|
|197127|8|5.17 / 0.72|0.01|0.71|

Evidence: `work/native-inn-refresh-01-player.log` and
`work/native-inn-refresh-02-player.log`, with corresponding launch logs.
Each inner scope has ten calls because two automatic refreshes also occur.
These short warm samples do not reproduce the earlier100–165ms family-run cost,
and do not contain that run's same household/admission state. They disprove neither
cold-entry cost nor state-dependent growth. No speculative block cache or
performance improvement is claimed from this result. Reproduce the earlier heavy
state using these scopes before choosing an optimization;4–6second Player freezes
remain a separate open investigation.
