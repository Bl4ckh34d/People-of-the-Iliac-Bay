# Directional standing reference

`tools/Editor/StandingAssetSurvey.cs` extracts the original five directional walking records and four frames from all 24 civilian archives. The local `source` PNGs and `side-poses-*.png` sheets are inspection references, not replacement artwork or mod bundle assets.

Visual inspection confirms that frames 0 and 2 have wide strides; frames 1 and 3 are narrower passing poses. The runtime now holds frame 1 rather than frame 0 while stopped, preserving native orientation and mirrored views. This improves the wide-stride freeze but is not a complete neutral idle animation set. Several female sprites still have a bent leg in the passing pose. Dedicated directional idle frames remain future art work.

Editor verification: `work/StandingPose-red.log` fails the old held frame; `work/StandingPose-green.log` passes the new pose, native animation restoration and visible-body retention when reconstruction fails. Native shop browsing/haggling also pass the held-frame check in the movement-continuity build. Full camera-angle visual approval remains open.

## Native idle inventory, 16 October 2026

DFU source confirms mobile civilian idle uses `MobilePersonBillboard.IdleAnims`, texture record 5. Walking uses records 0–4 for front, front-left, left profile, rear-left and back; records 1–3 mirror for the other three orientations. Thus every appearance already has a genuine front idle, while four unique idle views per appearance remain absent.

`source/native-idle/` contains transparent TextureReader exports of both record-5 frames for all 24 mobile archives: 381–398 and 451–456. `source/native-idle.csv` records source dimensions and scale. Frame 0 is the first native idle stance; frame 1 is the second idle/fidget stance. They preserve each archive's original identity and palette. Reuse these frames for front-facing idle where suitable.

Built-in image-generation pilots for archives 381 and 451 did not preserve source silhouette, proportions and pixel scale well enough for production. A constrained 451 side-view edit also softened pixels and changed the skirt pattern. None is included here or accepted as art. Current trusted coverage is one native view for each of 24 appearances; requested views 1–7 still need art, using five unique records and mirrored records for eight total bearings. The extraction log is `source-idle-extract.log`; marker `LIVING_CITIES_STANDING_SOURCE_PASS` records successful read-only extraction.
