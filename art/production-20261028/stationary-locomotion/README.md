# Stationary locomotion production study, 28 October 2026

Native shopkeeper 182/0 now has eight walking and eight running directions, each with four authored phases, as review candidates: 64 selected frames for one identity. All selected derived native review frames passed the bounded mechanical checks in `walk-182-0/walk-verification.json` and `run-182-0/run-verification.json`. Parent reviewed the full walking sheet and the front-right running pilot; this agent inspected the full running sheet locally. These are not production-approved. Source/palette, asymmetric pouch, body proportions and texture consistency remain open; front/front-left running contact heights differ by6pixels, rear-right by4pixels.

`manifest.json` records the central inventory snapshot, provider-qualified scope, original texture/reference hashes, exact generated-image hashes, prompts, mechanical evidence and visual findings. `audit.py` regenerates that manifest from unchanged images and the current shared inventory. It does not synthesize or edit pixels.

## Results

- `cycle-review-4fps.gif` is the assembled review loop; `cycle-review-sheet-4x.png` shows its four chronological phases; `cycle-review-atlas-native.png` is the derived native-size review atlas. `cycle-review.json` records source hashes, phase order, common scale and placement. This is the selected review candidate.
- Contact near leg, support near leg with far leg passing, contact far leg, then support far leg with near leg passing form the loop. The generation labels are not chronological order. No phase is a mirror or copied duplicate. The two passing images have different foreground-leg occlusion, and near-arm positions alternate at contact.
- The native-sized sheet was inspected locally. Head and left-profile camera remain stable. Body height is 76–77 pixels; the baseline varies by at most one pixel. The red trousers are still more saturated than the native source, and contact-phase thigh depth needs review. No user playback approval or gameplay acceptance is claimed.
- `flat-182-0-walk-left-pilot-v1.png` is the unmodified four-frame candidate. Complete left-facing figures, opposing arm movement at contact phases, and narrower passing silhouettes are visible. Correct leg ownership through the entire cycle, original proportions, native pixel structure and temporal consistency remain unproven.
- `flat-182-0-walk-left-pilot-v2-alpha-repair.png` is an unsuccessful alpha-only edit. It changes551733 RGB pixels in the shared foreground and does not produce binary alpha. It is not the selected candidate.
- `flat-182-0-walk-left-passing-keyframe-v1.png` fails its requested forward passing pose: the lifted boot still trails behind the supporting leg.
- `flat-182-0-walk-left-passing-keyframe-v2.png` is the best isolated pose candidate. Its bent knee and raised boot now pass in front of the supporting leg; the visible arm is more vertical. Supporting-foot and head placement shift relative to the input, so it is not a registered production frame or proof of a cycle.

Exact prompts are preserved in the `prompt-*.txt` files. The generated keyframes and local assembled sheet were visually inspected. `pack_review.py` renders the derived review using one common nearest-neighbor scale, one common canvas anchor and the existing 0.5 billboard alpha cutoff. It does not edit source PNGs, synthesize limbs, normalize every pose separately, interpolate motion, mirror poses or invent duplicate frames. No runtime integration or game execution occurred.

## Alpha and source corrections

The current billboard uses an alpha cutoff of0.5. Body alpha252/253 is close to opaque and is not by itself a reason to reject the art. A locally nearly black `view_image` rendering initially caused an incorrect transparency interpretation; direct pixel and histogram inspection disproved it. The exact historical alpha-repair prompt retains that mistake for provenance. Do not repeat its near-invisibility claim.

The native nearest-neighbor reference contains84 fully opaque RGB colors and no partial-alpha pixels. The four-frame candidate contains1545 fully opaque RGB colors and627938 partial-alpha pixels. These are inspection data, not automatic visual acceptance or rejection criteria. Source palette, contour under the actual cutoff, anatomy, camera consistency and temporal stability matter.

The reference is376×608 pixels, corresponding to47×76 logical pixels at8×. The first prompt inherited a historical38×77 silhouette approximation; the manifest explicitly corrects it. No generated output is claimed native-resolution exact.

## Coverage and route

The shared inventory initially supplied332 known stationary identities:226 vanilla,56 RMB Resource Pack,43 Elves & Beastfolk Resources,6 Lively Cities and1 Elves & Beastfolk Replaces. These are catalogue identities, not proof that each is an applicable humanoid actor. DET billboard classification and further provider discovery remain in the parent audit. Always use the counts in the latest generated manifest.

The central inventory later added 77 DET references, bringing the working catalogue to 409 provider-qualified stationary source entries. These include special and unclassified records. This is not an exact count of eligible humanoid NPC appearances. Multiplying catalogue entries by two actions and eight views gives a reconciliation matrix, not a validated production requirement. Eligibility must be resolved before claiming an exact remaining NPC count.

Accepted new production coverage remains zero. Current walking candidate coverage is vanilla 182/0, eight views and 32 frames; exact selected images and hashes are pinned in `walk-182-0/walk-review-manifest.json`. Mirror copies and native keeper offer/wiping frames do not count as gait coverage. The central applicability classification excludes 17 TEXTURE199 editor markers; the remaining 392 source records still include special anatomy and are not an exact humanoid NPC count.

The parent reviewed the eight-view walking sheet and found the distinct body directions and corrected contact arm changes usable as a review candidate. Quarter-view heads remain profile-like; native palette fidelity, saturated trousers and contact-phase depth remain disclosed review criteria. Running uses the exact native identity and separately authored contact/flight keyframes. The existing generated 385 run row supplies posture/energy guidance only, not purported native running art. The candidate is not a claim that every production quality gate has passed.

Original inputs, shared manifests, queues, runtime code and older art remain unchanged. No source archive or bulk texture export was duplicated.
