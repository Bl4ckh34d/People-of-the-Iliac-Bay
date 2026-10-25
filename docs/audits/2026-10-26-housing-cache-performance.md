# Housing survey stall — 26 October 2026

## Measured cause

The earlier `population-families` timing combined housing geometry reads with family creation/restoration. Separate counters in `housing-baseline` attribute 3,087.91 ms to 34 repeated block reads, 0.74 ms to 93 model-footprint reads and 0.31 ms to the family store. The city already had its native block cache; `HousingSurvey` nevertheless decoded the JSON-overridden blocks again.

`HousingSurvey` now reads the matching native cached blocks when it uses DFU's actual content reader. The existing `IncrementalLocationBlocks` cache lookup is shared rather than creating another cache. The operation only reads cached records and keeps the normal block-read path for native invalidation, absent/mismatched cache data, or a different reader. Bed counting, footprint calculation, house ownership and seeded population inputs are unchanged.

## Native regression and comparison

`ProfileInEditor.ps1 -CountersOnly -ValidateHousing` verifies a real loaded city. `housing-red` fails with `Housing survey redecoded 34 blocks already in DFU's cache`. `housing-green` performs zero repeated block reads, then clears the native cache in the owned fixture and confirms normal reading resumes. All 408 sorted home profiles match exactly: building key, beds, footprint and availability. The native cache contents are restored before the fixture returns; no save is written.

The full `housing-baseline` and `housing-after` runs use the same copied SAVE82, mod order/settings, native navigation and player-equivalent DFU location cache. Both have identical timing instrumentation and no binary profiler capture. The production behavior change is native cache reuse; the after-run does not invoke the parity fixture or clear the cache.

| Startup scope | Before | After |
| --- | ---: | ---: |
| Housing survey | 3,097.02 ms | 8.74 ms |
| Family store | 0.31 ms | 0.18 ms |
| Combined original scope | 3,097.35 ms | 8.92 ms |
| Repeated housing block reads | 34 | 0 |

The separate green parity run measures 8.34 ms for the combined scope. These results remove this approximately three-second synchronous stall; they do not prove that every reported four-to-six-second gameplay freeze has the same cause. Allocation volume during startup was not measured by these counters.

## Boundaries

Both full runs complete one-hour and three-hour native loiters without exception entries. Before the first loiter and after one hour, no mobile resident bodies are active; after three hours twelve are active in each run, at the same game time. Existing unrelated DET custom-material startup errors remain in the editor logs.

| Separate three-hour loiter measurements | Before | After |
| --- | ---: | ---: |
| Maximum frame during skip | 1,723.21 ms | 1,674.27 ms |
| After-skip frame p50 | 20.10 ms | 20.55 ms |
| After-skip frame p95 | 48.81 ms | 48.32 ms |
| After-skip maximum frame | 285.28 ms | 250.13 ms |
| After-skip gen-0 collections | 8 | 7 |
| Active residents at sample end | 12 | 12 |

The after-skip frame distributions are effectively similar; this change removes a startup stall, not the separate approximately 1.7-second time-skip stall. Single-run frame differences are not evidence of a general frame-rate improvement. Full measurements are in the runs' `frames.csv`, `sections.csv` and `summary.json`.

The native cache remains owned and invalidated by DFU. Uncached remote-town surveys still decode their blocks and may need further work distribution. Broader native city-light/season updates and the reported four-to-six-second gameplay stalls remain on the worklist.
