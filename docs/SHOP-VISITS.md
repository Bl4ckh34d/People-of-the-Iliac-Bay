# Shop visitor activities

Development version 0.3.32 replaces fixed standing positions for usable shop visits with timed interior activities.

## Behavior

A visitor enters at the surveyed interior entrance, walks to two shelf stops, spends time browsing, walks to a merchant stop, spends time haggling, and walks back to the entrance before the daily visit ends. Each position derives from absolute game time; movement does not accumulate frame history. Walking uses the existing 1.3 metres per real second and 12 game seconds per real second conversion.

The geometry adapter discovers native ShopShelves loot objects and a static merchant. It chooses supported standing anchors near those objects. Routes connect existing surveyed floor/stair paths through their common ancestor, preserving the checked geometry instead of drawing straight lines through furniture. The visitor logically faces the shelf or merchant while stopped. Native idle art still faces the player; directional idle artwork remains unfinished.

All scheduled visitors to that shop receive stopping positions in stable population order, including visitors currently elsewhere. Different visitors reserve different stopping anchors. The allocation reconstructs daily or when the interior reloads. It does not add save fields or menu settings. Deaths or layout changes can change which routes are available. Crime reactions take precedence over shop activities.

If a shop has no identified shelf/merchant, lacks a usable route, cannot fit its walking plus at least one minute of activities into the visit, or lacks free stopping anchors, the visitor retains the existing standing fallback and leaves when the visit ends. This fallback does not extend the appointment indefinitely. Stop reservations currently cover all daily visitors, so busy shops can exhaust anchors even when appointments do not overlap. Full moving-body avoidance and counter queues remain unfinished. Haggling is an activity pose; no inventory or money transaction is implemented.

## Verification

- 272/272 core tests pass. Tests cover fractional travel, browsing/haggling/departure, reconstruction without frame history, defensive path copying, exact daily appointment bounds, disconnected paths and insufficient duration. Evidence: `work/ShopJourney-{red,green}.log`.
- `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -ShopVisitProbe` passes in the actual Gothway Garden shop fixture. A controlled generated visitor uses normal StreetPopulation updates; the probe checks rendered walking with animation, phase positions and idle states, and removal after scheduled departure. It changes a runtime appointment in the dedicated test process and does not save it. Evidence: `work/ShopJourney-live-pass.log`.
- `-IndoorNoiseProbe` passes on the rebuilt bundle: bed-origin waking, actual save/reload, physical reporting, neutral finite dispatch and guard arrival remain intact. Evidence: `work/ShopJourney-noise-regression-pass.log`.
- Unity build passes. Installed 0.3.32 bundle SHA-256: `3940B55F421B1420932A2BC26F4FDC9BFD1DB4C3727C67FD22590EBC2AD7A188`.

The live activity check uses short continuous movement plus clock jumps to inspect later phases. It does not prove uninterrupted traversal of every shop layout, actual interior re-entry, or full crowd behavior. These remain gameplay acceptance work.
