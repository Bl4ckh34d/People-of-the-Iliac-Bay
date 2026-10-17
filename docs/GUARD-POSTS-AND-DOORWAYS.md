# Public guard posts and doorway pursuit

## 16 October gate and pursuit correction

Latest user requirement supersedes the standing destination below: exterior sentries end their shift in city-wall quarters and sleep in assigned beds. The inner on-duty pair remains posted. Current inner return points are only the intermediate gate-crossing destination; wall-quarter assignment, interior travel, visible sleeping and morning departure are not implemented by this checkpoint.

Scheduled recall now lives in the persistent core roster (`GuardPostRecall`). The open-gate survey supplies the return path and a departure/arrival deadline before closure. Core advancement evaluates that journey without exterior geometry, including time jumps and save restoration. Emergency reassignment, death or a changed post invalidates the routine recall. Completed routine movement reports are pruned; they never establish knowledge of player crimes. Core coverage includes JSON round-trip, intermediate position, delayed advancement, emergency/death, invalid deadlines and thirty repeated shifts with bounded report storage.

Daggerfall City's four native gates now receive two inner sentries and two outer sentries from the existing finite garrison. Posts flank the actual doorway and face away from the wall. Gate assignments are excluded from ordinary patrol/post migration. Outer sentries follow surveyed routes to separate inner posts before native 18:00 closure; deployment requires time for both outbound and return travel. A player crime no longer suspends recall of otherwise idle sentries.

Three placement/navigation causes were identified: valid gate-side ground has navigation weight 6, the 25.6-metre gate mesh includes the wall frame rather than only the doorway, and native navigation can disconnect the open crossing. Native models 446/447 have an 8.525-metre aperture centered at local X=-3.1875 metres. The survey uses these measured dimensions, a bounded nearby-cell search, collision clearance, and an explicitly swept aperture bridge. Closed gate geometry still blocks that bridge. This is native-model support; replacement gate meshes need separate compatibility validation.

Verification: 381/381 core tests, Unity build and the full live Daggerfall gate probe pass. All four gates have four distinct assignments; actual actors at the observed gate match positions/facing, and all eight outer sentries finish their simulated return paths before closure. Evidence: `work/GateGuard-runtime-pass.log`, `work/GateGuard-core.log`, `work/GateGuard-posts.png`. The image visually confirms the inner pair beside the doorway. The return check samples roster movement for all gates, not rendered continuous movement of all eight actors simultaneously.

Further verified checkpoint: 385/385 core tests and the final Unity build pass. The extended native test re-deploys eight exterior guards on the next day, enters a real inn, advances across dusk while remaining inside, and confirms all eight saved recalls completed (`work/GateRecall-runtime-pass.log`, `LIVING_CITIES_GATE_INDOOR_RECALL_PASS`). JSON recall persistence and bounded thirty-shift storage pass core tests. Full native gate-duty save/load coverage, other settlements/replacement meshes, elevated wall posts, and wider performance profiling remain open. The user's subsequent [wall-quarter sleeping requirement](GUARD-QUARTERS.md) remains unimplemented beyond these recall foundations. Castle coverage remains a separate requirement in [activity and gate follow-up](ACTIVITY-AND-GATE-FOLLOWUP.md).

The separate chase-reacquisition regression also passes: known wanted players reacquire pursuit from returning and posted guards when actually visible. See [guard response](GUARD-BUILDING-RESPONSE.md) and `work/GuardReacquire-runtime-pass.log`.

## Earlier checkpoint

Installed development version:0.3.34. Bundle SHA-256: `89A2F556FA8848B7C1F68C38EC6FDE6833175B383357CA6239CE2DD06D0FEF82`.

The player's Daggerfall report exposed doorway post selection: guards were assigned resident commute starts at house entrances. A stationary post must instead oversee a road junction, public square, gate approach or reachable wall position, with a clear entrance and street-facing stance.

## Current source changes

16 October gate-shift update: outer sentries now continue from the pre-close crossing into reserved wall interiors and patrol until dawn, then return to staging for daytime deployment. Inner posts remain staffed. Beds/sleeping are deferred by the user's latest decision. See [GUARD-QUARTERS.md](GUARD-QUARTERS.md) for verified scope and limitations.

The exterior survey samples paved junctions/open paved areas and road cells near native city gates. Static capsule clearance and route validation reject obstructed positions; posts remain at least five metres from building doors and six metres apart. Gate approaches receive priority. Paved-space edges are preferred over their centers. This is a native tile/geometry heuristic, not semantic identification of every market square. Elevated wall posts are not surveyed yet.

Existing idle saved guards keep identity, appearance, health and mortality while walking surveyed relocation routes from old posts to available public posts. Guards on active patrols, investigations, pursuits or refuge journeys retain that journey and relocate when safely posted again. Recruitment at an obsolete inherited post is handled by the same deferred migration. Post changes invalidate approach caches and resurvey routes; active response paths remain intact. Exterior surveying waits until the exterior is active after an indoor reload and completes without recreating active guard actors.

Stationary guards face the longest clear street direction instead of their last approach to a door. The static direction is cached per post, avoiding four physics casts per guard per frame.

## Shop exit pursuit

The previous exterior controller skipped every guard with an interior record. Thus a guard pursuing the player inside could remain trapped in that simulation state after the player left.

On a building-exit event, an actively hostile indoor pursuer retraces its recorded interior trail to the doorway. The same guard then emerges at the reported exterior entrance and resumes native pursuit/search. Health, identity, incident and last known exit are preserved. A distant guard does not instantly teleport outside. A cleared crime uses existing stand-down behavior. The optional saved `ExteriorPursuit` field is validated against the report's building entrance; ordinary older saves keep a null field. A guard killed during doorway travel remains dead and loses the pursuit marker.

## Verification status

286/286 core tests pass (`work/GuardPostMove-green.log`), covering public junction classification, identity/health-preserving post reassignment, relocation travel/reload, active patrol preservation, delayed doorway exit, pursuit save validation and ordinary return. Unity build passes. Native Gothway shop checks verify15 public posts, indoor alarm/save/reload/stand-down and the same wounded guard physically pursuing outside (`work/GuardPosts-door-motion-pass.log`). After the final migration correction, the combined Daggerfall haunting/patrol/refuge/city/save/movement and regional suite passes (`work/GuardPosts-city-regression-pass.log`):611 Daggerfall residents and49 guards; initial city setup11.62 seconds, not isolated post-survey or frame-time profiling. Elevated wall posts, full legacy-save migration coverage, other shop/storey layouts and broader city profiling remain open.
