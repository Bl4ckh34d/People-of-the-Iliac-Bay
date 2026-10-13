# Physical town slice

Continue the approved goal through the first playable population slice. Use the existing tested core, preserving static quest actors and saved identities. Do not infer gameplay success from a menu startup marker.

Checkpoint, 14 October: step 1 is verified, including real inside/outside saves and fresh-process reloads. Step 2 has an initial actual-town adapter (156 anchors, 17 rejected door candidates, 11 sampled routes); full connectivity/actor traversal remains pending. Step 3 now has fractional-second core evaluation with three new tests (60 total). Steps 4–5 physical actor work remain pending. Read the amended family, child, bed-sharing and tavern rules in `docs/MILESTONES.md` before continuing; the original three-person population cap is superseded in scope but still present in code.

1. Add an explicit command-line-only test harness that creates a uniquely named disposable character and loads Gothway Garden using actual game data. Inspect real building doors and navgrid dimensions. Preserve all normal user saves and settings on disk.
2. Add a settlement adapter that maps stable building keys to valid exterior entrance anchors. Snap an entrance only through a short unobstructed segment to a walkable cell; unreachable entrances get a reported conservative fallback. Use sorted keys and stable selections.
3. Add route/schedule planning and smooth sub-minute evaluation with behavior tests first. Travel duration derives from distance and walking speed/time scale. Use deterministic work destinations and real opening hours; no-route residents stay home. Separate routes from rendered actors.
4. Add distance-based actor activation with hysteresis, stable identity bindings, classic sprites and guarded Unity random state. Keep actors keyed by resident ID. Disable the vanilla random civilian pool only while a supported settlement adapter owns it; restore it when disabled/unloaded. Retain stationary quest actors and vanilla guards until their dedicated replacement is ready.
5. Exercise actual town loading, actors, route boundaries and world-origin coordinates. Then save/load and leave/return with a real save slot, recording identity/death continuity and checking duplicate actors. Report limitations for interiors, anchors and quest actors explicitly.
6. Review changes, fix meaningful findings, update milestones and Obsidian with verified results. Keep the overall goal active.

Relationships and future inventory extensions use the same persistent identities; see `docs/RELATIONSHIPS-AND-FUTURE-INVENTORIES.md`. City-overhaul adapters follow vanilla acceptance.
