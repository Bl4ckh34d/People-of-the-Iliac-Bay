# Public guard posts and doorway pursuit

Installed development version:0.3.34. Bundle SHA-256: `89A2F556FA8848B7C1F68C38EC6FDE6833175B383357CA6239CE2DD06D0FEF82`.

The player's Daggerfall report exposed doorway post selection: guards were assigned resident commute starts at house entrances. A stationary post must instead oversee a road junction, public square, gate approach or reachable wall position, with a clear entrance and street-facing stance.

## Current source changes

The exterior survey samples paved junctions/open paved areas and road cells near native city gates. Static capsule clearance and route validation reject obstructed positions; posts remain at least five metres from building doors and six metres apart. Gate approaches receive priority. Paved-space edges are preferred over their centers. This is a native tile/geometry heuristic, not semantic identification of every market square. Elevated wall posts are not surveyed yet.

Existing idle saved guards keep identity, appearance, health and mortality while walking surveyed relocation routes from old posts to available public posts. Guards on active patrols, investigations, pursuits or refuge journeys retain that journey and relocate when safely posted again. Recruitment at an obsolete inherited post is handled by the same deferred migration. Post changes invalidate approach caches and resurvey routes; active response paths remain intact. Exterior surveying waits until the exterior is active after an indoor reload and completes without recreating active guard actors.

Stationary guards face the longest clear street direction instead of their last approach to a door. The static direction is cached per post, avoiding four physics casts per guard per frame.

## Shop exit pursuit

The previous exterior controller skipped every guard with an interior record. Thus a guard pursuing the player inside could remain trapped in that simulation state after the player left.

On a building-exit event, an actively hostile indoor pursuer retraces its recorded interior trail to the doorway. The same guard then emerges at the reported exterior entrance and resumes native pursuit/search. Health, identity, incident and last known exit are preserved. A distant guard does not instantly teleport outside. A cleared crime uses existing stand-down behavior. The optional saved `ExteriorPursuit` field is validated against the report's building entrance; ordinary older saves keep a null field. A guard killed during doorway travel remains dead and loses the pursuit marker.

## Verification status

286/286 core tests pass (`work/GuardPostMove-green.log`), covering public junction classification, identity/health-preserving post reassignment, relocation travel/reload, active patrol preservation, delayed doorway exit, pursuit save validation and ordinary return. Unity build passes. Native Gothway shop checks verify15 public posts, indoor alarm/save/reload/stand-down and the same wounded guard physically pursuing outside (`work/GuardPosts-door-motion-pass.log`). After the final migration correction, the combined Daggerfall haunting/patrol/refuge/city/save/movement and regional suite passes (`work/GuardPosts-city-regression-pass.log`):611 Daggerfall residents and49 guards; initial city setup11.62 seconds, not isolated post-survey or frame-time profiling. Elevated wall posts, full legacy-save migration coverage, other shop/storey layouts and broader city profiling remain open.
