# Physical town slice

Continue the approved goal through the first playable population slice. Use the existing tested core, preserving static quest actors and saved identities. Do not infer gameplay success from a menu startup marker.

Checkpoint, 14 October: step 1 is verified, including real inside/outside saves and fresh-process reloads. Step 2 has an actual-town adapter (156 anchors, 17 rejected door candidates, 11 sampled routes). Step 3 includes fractional-second visits and persisted routes; 66 core tests pass. Steps 4–5 now have Gothway physical actors, walking animation, matching interior presence, re-entry deduplication, living/dead indoor save/load and schema migration verified in the actual player. Whole-town route traversal, origin shifts and cross-map continuity remain open. Review found and helped fix the House1 destination bug. Read the amended family, child, bed-sharing and tavern rules in `docs/MILESTONES.md`; the original three-person cap remains in code pending family allocation.

1. Add an explicit command-line-only test harness that creates a uniquely named disposable character and loads Gothway Garden using actual game data. Inspect real building doors and navgrid dimensions. Preserve all normal user saves and settings on disk.
2. Add a settlement adapter that maps stable building keys to valid exterior entrance anchors. Snap an entrance only through a short unobstructed segment to a walkable cell; unreachable entrances get a reported conservative fallback. Use sorted keys and stable selections.
3. Add route/schedule planning and smooth sub-minute evaluation with behavior tests first. Travel duration derives from distance and walking speed/time scale. Use deterministic work destinations and real opening hours; no-route residents stay home. Separate routes from rendered actors.
4. Add distance-based actor activation with hysteresis, stable identity bindings, classic sprites and guarded Unity random state. Keep actors keyed by resident ID. Disable the vanilla random civilian pool only while a supported settlement adapter owns it; restore it when disabled/unloaded. Retain stationary quest actors and vanilla guards until their dedicated replacement is ready.
5. Exercise actual town loading, actors, route boundaries and world-origin coordinates. Then save/load and leave/return with a real save slot, recording identity/death continuity and checking duplicate actors. Report limitations for interiors, anchors and quest actors explicitly.
6. Review changes, fix meaningful findings, update milestones and Obsidian with verified results. Keep the overall goal active.

Relationships and future inventory extensions use the same persistent identities; see `docs/RELATIONSHIPS-AND-FUTURE-INVENTORIES.md`. City-overhaul adapters follow vanilla acceptance.

## Immediate continuity extension, 14 October

The user observed a resident disappear on entering a house and a frozen walking sprite. Prioritize functional milestones over appearance; defer collision-checked curved paths and other visual polish. Fix the animation-state reset with an actual-frame regression check. Then render the same scheduled resident in the currently loaded destination/home interior, using collision-checked reachable floor anchors and the same identity/name/appearance/death record. Remove the street representation during interior transitions and never duplicate vanilla quest/service identities. Interior activity poses, full room navigation, furniture assignment and sleep placement follow after presence continuity works.

Acceptance: observe walking frame changes; advance a sampled resident to arrival; enter the matching real door; find that resident with unchanged name/appearance; exit and re-enter without duplicates; persist a death and verify save/load prevents respawn. Test fixtures must reproduce the game's load-button behavior by returning to the HUD; a loaded but menu-paused game cannot test actor updates.
