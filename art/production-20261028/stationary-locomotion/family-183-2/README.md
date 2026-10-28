# Stationary 183/2 locomotion source family

The authoritative native source is `../../sources/native/183_2-0.png` (69×159). The independently extracted records 334/15 and 357/12 are byte- and pixel-identical. `source-family.json` pins all three originals without duplicating them.

This family has a brown bearded face, gray mail and steel armor, a blue/pale-gray tabard, and an anatomical LEFT-hand spear. It is not a recolor or mirrored equivalent of 183/3, whose spear belongs to the other hand. The original tabard has two columns and four rows: anatomical right is blue, gray, blue, gray; anatomical left is gray, blue, gray, blue. The native image remains the appearance authority; animation templates supply movement and camera only.

The first left-profile walking pilot repeated its leading leg and switched spear arm ownership in its contact poses. A bounded strip repair restored the opposite contact; an individual ImageGen phase0 repair placed the spear into the correct near left hand. The selected left-view cycle now contains four static review candidate frames. Spear visibility/length, brighter face and beard, and small width differences remain limitations. Four unique pixel hashes alone do not establish a valid gait.

All anatomy changes use the built-in ImageGen tool. JSON receipts retain exact prompts, generated output locations, and reference inputs. Packaging performs nearest-neighbor scaling, alpha cutoff 128, and rigid contact registration only. No limbs or pixels are painted by a script, and no mirrored stand pose is counted as an animation. Production acceptance remains zero.

Use the existing family packer and verifier with this directory as their argument. Their location is `../family-182-5/pack_family.py` and `../family-182-5/verify_family.py`. Original source, generated sources, selected packed frames, and local previews remain separately traceable.
