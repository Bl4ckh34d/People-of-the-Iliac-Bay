# Native mobile 381 running candidate

Eight visible bearings and four phases per bearing are saved in `candidate-v1`. The authoritative record is `candidate-v1/manifest-reviewed.json`.

There are 20 generated source poses and 12 exact horizontal mirror derivatives. Bearings 5, 6 and 7 derive from 3, 2 and 1 under the verified native MobilePersonBillboard mapping. The green belt details follow those native view-dependent reflections; these are not 32 independent generations. Both review pages were inspected locally after packing.

The front opposite contact, back opposite contact, back bare-leg flight, and rear-left counterarm contact/flight were regenerated individually. The initial rear-left counterarm repair did not switch the visible low hand and remains rejected. Generated right-side strips were excluded because their arm alternation was unreliable. Thirteen generation calls and their raw output hashes are recorded in `generation-provenance.json`.

Technical checks confirmed 32 distinct RGBA frames, atlas cell equality, unclipped alpha128 silhouettes, ground-aligned contacts, airborne clearance, and exact mirror correspondence. Alpha is retained rather than eroded. Native references preserve archive, palette, record, frame and decoder hashes.

Canvas is 160 by 176 pixels. The mean contact silhouette is 110 pixels high. The foot pivot is (80,156) from the top left. These do not directly specify world height. Native record0 is 50 by111 pixels with scale(-70,-70), producing37 by81 before GlobalScale0.025; its height is2.025 units. Other381walking bearings produce80 pixels, or2.000units. Proposed export scale currently follows record0; runtime overrides and integration are not applied.

Candidate limits: cloth volume, saturation, face detail and torso width vary. Rear-left phase3 has visibly larger cloth/thigh volume. The four-frame cadence lacks passing intermediates and still includes exaggerated stride silhouettes. Anatomical phase labels reverse under mirroring, as native renderer reflection does. No game playback, user approval, or integration is claimed.

Status: generated=true; technical_checked=true; visual_reviewed=true; integrated=false; user_approved=false.
