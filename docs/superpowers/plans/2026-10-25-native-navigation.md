# Native navigation delivery

**Goal:** Ship precomputed native Unity NavMeshData and use native agents for ordinary residents. Keep existing schedules, activities, doors and seat reservations.

**Approved scope:** User requested implementation and native game tests on 25 October. No additional background simulation.

- [ ] Capture actual static scene geometry during development; bake reusable native assets in the Editor and bundle them. Normal play loads assets, never bakes a city.
- [ ] Provide offline content baking for exterior/interior/dungeon modules. Preserve geometry variant identity and connect adjacent modules.
- [x] Expose stable travel destinations from the existing schedule. A native agent owns ordinary displacement; existing short reserved seat transition remains.
- [ ] Verify loaded assets, normal residents, stair ascent, seats, doorway transitions and unloaded scene cleanup in the actual game. Record measured scope and remaining gaps.

Use the current dirty workspace. Preserve unrelated changes. Test saves are disposable. Do not build while editing runtime sources.

## Verified checkpoint,25 October

Package `6BAE2C07F4A6…` ships exact Daggerfall city64 modules and2755 links without runtime baking. Three ordinary native walkers, physical doorway arrival, and one-hour visit/travel catch-up pass in the installed game. New route/timing queries use native corners; native mode skips old corridor lanes. See `docs/TESTING.md` for evidence and boundaries.

All920 classic RMB source templates exported successfully (`work/navmesh-classic-catalog-complete-player.log`), including recovery from missing model43756 in Beautiful Villages. These raw templates use flat ground and are excluded from normal bake/load paths. Next: assemble actual native location terrain/climate variants before accepting template reuse; capture/bake actual interior and dungeon scenes; test simultaneous doorway arrivals and live Gemma outdoor grounding. No full-world acceptance yet.
