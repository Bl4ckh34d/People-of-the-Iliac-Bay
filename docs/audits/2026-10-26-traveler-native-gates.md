# Traveler routes through native city gates

## Reproduced causes

The production Daggerfall NavMesh loaded all 64 modules and 2,755 seam links, but every Inn's traveler survey returned zero routes. This was not demand starvation. The focused baseline `work/traveler-routes-posts-player.log` also proves all eight actual outside/inside guard-post pairs returned `PathPartial`. The existence of two inside and two outside posts per gate did not establish connectivity.

Two independent defects were confirmed:

1. The shipped gate geometry was captured with closed native leaves. Comparing the source vertices with the live daytime capture shows the leaf rotation. The opening center ray passed through the 0.15 m crack between the closed leaves; this was a false clearance signal for a 0.30 m radius body. A ray displaced 0.20 m laterally hits the shipped leaves at 10.0/10.4 m and does not hit the open live mesh (`work/traveler-navsource-diff-offset.jsonl`).
2. Entrance selection preferred the lexicographically first admitted doorway even if its native polygon was disconnected. Inn 197386 has three native doors. Door 12 at `(388.2,325.8)` was selected and disconnected; door 13 at `(392.6,333.4)` was reachable from the inner city street. Door 11 was also disconnected. The geometry itself was unchanged between the old and live source captures.

## Changes

`NativeCityGateNavigation` supplies one shared aperture calculation to native links, guard-post placement and traveler approaches. It preserves the existing native frame offset and guard geometry. A native bidirectional link bridges only the physical aperture when a full body capsule finds the current gate clear. The native gate changes its mesh at its ordinary closing/opening time; the runtime removes/recreates the link accordingly. No NavMesh data was replaced and no wall frame was removed.

Native movement checks the current city-gate collider before accepting a step. A cached aperture bounds check avoids physics calls away from gates; near gates, fixed NonAlloc buffers avoid per-frame allocation. Native off-mesh traversal advances at walking speed with a maximum 0.5 m visible step. The earlier general `Warp`/`NavMesh.Raycast` clamp rewound the actor onto the departure polygon while crossing a gap; the first physical regression reproduced that failure even after path queries passed.

A later planning guard also clips native path segments against cached closed-aperture bounds. This prevents an open-state future bake from admitting a path through the closed current gate even when removing the extra link cannot disconnect the baked mesh. Its focused regression supplies explicit test-only native links while native gates are closed: raw NavMesh paths must be complete while production guard routes remain rejected. This additional check has not yet run at this audit checkpoint.

The entrance survey prefers native doors connected to a sampled inner-gate street point, retaining existing geometric fallback when no such candidate exists. Total Daggerfall entrance count remains 532. Traveler routes use the same corrected aperture and selected Inn door.

Inn traveler origins now use the existing Basic Roads search when that provider is available. Preparation examines at most 32 nearby settlements, keeps up to 16 connected origins, bounds each search at 4,096 expanded nodes and advances 64 work steps per frame. Scene/store invalidation cancels publication. Vanilla behavior remains available when Basic Roads is absent. This validates regional connectivity; it does not add continuous physical travel across unloaded map pixels or invent an onward destination.

## Verification

- Baseline: 0/19 Inns, all eight actual guard-post crossings partial, both first Inn doors disconnected.
- First corrected topology: all 19 Inns have four gate routes; all eight actual guard-post crossings complete; guard outer-gate-to-Inn routes contain 20–32 points; 532 entrances retained. Evidence: `work/traveler-routes-gate-links-player.log`.
- First physical regression failed because the existing native mover rewound off-mesh traversal. The failure was preserved before changing movement.
- Final native run passes: all 19 Inns have four routes, all eight actual guard-post crossings are complete, and all four native bodies cross 13.23–13.31 m. The largest observed frame step is 0.417 m. Advancing the real native clock to 18:01 produces zero traveler and guard crossing routes; advancing to 06:00 restores traveler routes. All 16 prepared origins have verified Basic Roads paths and every generated proposal checked by the probe uses one. Evidence: `work/traveler-routes-final-player.log`, `work/traveler-routes-final-run.log`.
- Unity build and integration checks pass; isolated RuntimeCompile passes with zero warnings/errors. The tested installed bundle SHA-256 is `D9BAEBB108F979ACF60DA74A606D9976BE8384C074CC21ED460DD8BACE9939E2`. No commit or push was performed by this worker.

The topology probe uses a controlled native navigation body to isolate movement. Ordinary traveler admission, a visible traveler missing closing time, and retained physical departure are separate acceptance checks owned by the parent integration task. This audit does not claim those checks passed.

## Observed departure retention

The parent native `traveler-departure-red` run reproduces a visible traveler
vanishing at its planned `ExitAt`, before reaching the physical outer endpoint.
The runtime now retains an actually observed departing body until that endpoint,
uses its real body position for visibility, and prevents a later visit from
replacing it. Reactions and player actions retain their existing movement priority.
Dialogue can describe the still-pending departure, except when an action or
reaction currently owns the activity.

`traveler-departure-green` passes: the same body remains after the scheduled exit
(1.673 m of ordinary movement during the check), survives 18:01 and resumes
movement after 06:00. This fixture uses a real recorded check-in refusal. It does
not prove paid lodging admission, a complete outbound walk, or that the body was
standing directly against the closed gate. The observation flag is local to the
loaded scene; unobserved/offscreen journeys keep the existing abstract schedule.
Evidence: `work/traveler-departure-{red,green}-player.log`.
