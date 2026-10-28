# Stationary guard source family 183/3

Native records 183/3, 334/16, 346/20 and 357/13 have identical 66 × 159 RGBA pixels. `source-family.json` pins each original path, PNG hash and pixel hash. These stationary identities are distinct from mobile guards. Original files remain in the shared native-source directory and are not duplicated here.

The source has a warm brown clean-shaven face, chainmail hood and mantle, gray armored limbs, a green/tan checker tabard, an upright right-hand polearm and a dark sword/scabbard on the left hip. The checker pattern has two rows on the torso and repeats below the belt. It is more specific than a plain two-color quartered tunic.

The first left walking strip preserves much of the equipment and costume but does not clearly alternate near and far gray legs. It also carries the polearm too high. That strip remains unselected diagnostic material. A bounded follow-up uses the reviewed jester left-walk cycle solely as a color-coded motion guide. The native guard remains the appearance reference. Its passing phases have different near/far overlaps; a targeted phase 2 edit recesses the leading far right leg and brings the trailing near left armor into the foreground. The resulting left cycle is a local motion-review candidate, with body heights of 157–158 pixels and stable contact baseline. Gray armor makes leg ownership less obvious than the jester's two-color legs, so the depth interpretation remains an explicit review point. Palette, face and armor detail are not declared exact.

The second generated strip has unequal cell spacing. Its first foot crosses the nominal quarter boundary, so the packer initially stopped. Explicit source rectangles now cut only through inspected transparent gutters; no figure pixels are removed. Selection hashes are pinned in the manifest. The verifier rejects a stale manifest and writes an INCOMPLETE record before checks, preventing a previous PASS from masquerading as verification of a changed selection.

`selection.json` defines selected sources and their review status. No production acceptance is claimed. To reproduce the review from this directory, run:

```powershell
python ../family-182-5/pack_family.py .
python ../family-182-5/verify_family.py .
```

The shared local family packer reads this family's dimensions, 192-pixel cells and baseline 180. It preserves all raw generations and only performs nearest-neighbor scaling, rigid whole-frame contact alignment and alpha cutoff 128. It writes standalone frame PNGs, atlases, contact sheets and four-frame GIFs. The verifier checks original hashes, generation reference pins, selected source hashes, derived crop parity and packed geometry. A passing mechanical check does not establish a correct gait or exact original fidelity.

All creative changes use builtin `image_gen`; prompts and reference paths remain beside generated PNGs. No gameplay, Unity, build or installed runtime files are changed.

## Current eight-bearing walk review

All eight bearings have four selected frames. Views 0, 1, 2, 4, 6 and 7 are static review candidates (24 authored frames); rear-left 3 remains `needs-pose-scale-review` and rear-right 5 remains `needs-contact-depth-review` (8 additional selected diagnostic frames). Rear-left eventually gained crossed legs using unchanged native appearance plus one selected jester motion frame, but the resulting body proportions and spear changed. A bounded style correction retains the pose but is still smaller than its companion frames. Rear-right still does not demonstrate reliable opposite hip-to-knee connections. They are excluded from candidate counts. All 32 selected frames pass mechanical provenance/geometry/alpha checks; none is production accepted.

The near right spear arm in right-profile view 6 originally changed ownership across phases; v3 keeps it fixed. Front and back passing phases needed isolated opposite-leg repairs. Front-left contact 0 and front-right contact 2 were authored separately to show crossing leg connections. Front-right replacement retains a visible scale/proportion mismatch (extent160 versus152�154px); front passing1 is6px shorter than contact frames. Rear tabard checker rows are simplified in some bearings. These limits are recorded in `visual-review.json`, alongside root static checks and artifact hashes.

The left run pilot uses this guard's selected left walk appearance plus the same-bearing reviewed jester run as a color-coded motion reference. This is a generated-reference chain pinned back to unchanged native guard source; it is not native animation evidence.

The first selected left run has four actual authored phases with alternate near/far support and flight knees. Two isolated arm edits preserve the depth-corrected legs. Native-scale review gives full extents156/151/156/155px and alpha-bound ground gaps0/5/0/4px including the carried spear. No flight is introduced by packing. The selected run is a static candidate; face, arm and body detail vary. All eight run bearings now have four static review-candidate frames. The full run and walk sheets were visually inspected locally; root review backlog remains separate.

## Current checkpoint

64 selected frames:32 run candidates,24 walk candidates and8 held walk frames (rear-left pose/scale/prop, rear-right contact depth). All64 pass mechanical checks. These are56 authored candidate frames reusable across4 verified identical provider-qualified native identities, not224 independently authored images. No production acceptance, temporal playback approval, installed runtime or game change is claimed. All heel/arm/depth changes are ImageGen edits with raw receipts; no painted pixel changes or guard flight-Y offsets. Full appearance, palette, camera and temporal validation remains required.
