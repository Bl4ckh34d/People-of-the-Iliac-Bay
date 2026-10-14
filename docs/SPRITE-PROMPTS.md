# Native sleeper sprite generation

Generated with the built-in imagegen tool on 14 October 2026. Final assets are copied into this repository unchanged, preserving alpha; no external paid API or CLI fallback was used. These are individual complete sleeper composites, not separated heads/blankets. Effective pixel scale and final in-game appearance still need broader visual acceptance.

## Native flat182/0

Reference: work/sleep-source/flat-182-0.png, extracted from the installed classic game files. Match by native TextureArchive/TextureRecord, not NativeHash. Peryrick Kinghart's fixture has hash44 but sprite182/0.

Saved asset: mod/Textures/sleep-flat-182-0.png.bytes. Original generated output: C:/Users/Daniel/.codex/generated_images/01a135ac-12fe-7041-9e19-3dbc601bd8cb/exec-5d6f22c9-26e5-4cd4-b430-ce8b711383c1.png. Inspection confirms real alpha and five separated opaque row groups. Canvas1145x1374; the sprite uses coarse pixel blocks. Imported without resizing.

Final prompt:

> Classic 1996 Daggerfall low-resolution limited-palette pixel-art sprite atlas of the EXACT male merchant in reference: middle-aged fair-skinned man, short brown/reddish hair, short reddish-brown beard and moustache. NO HOOD, NO HAT. Same face and classic sprite style. Asleep with closed eyes lying HORIZONTALLY, body fully covered to chin by thick DARK GREEN blanket, one WHITE pillow under head. No bed/frame/props. Five rows vertically with large TRANSPARENT gaps and margins, each row ONE contiguous complete sleeper composite including head+pillow+blanket. No separate head layers. Rows top to bottom: (1) head-end view, (2) three-quarter between head-end and broadside, (3) pure broadside head LEFT and feet RIGHT, (4) three-quarter from foot-end, (5) foot-end straight view. Consistent camera eye height from a person beside bed, never overhead. Broadside horizontal and widest; end views narrower and foreshortened. Single same merchant sleeping peacefully in each of FIVE frames. Hard crisp pixel edges, minimal classic colors, roughly120x32 effective pixels per broadside,50x35 end views. Transparent outside each sprite, absolutely NO background, glow, shadow aura, grids, text, labels, extra faces, standing people, props or frame. Head and blanket form ONE connected sprite per row. Green blanket and white pillow identical all rows.

## Native flat182/44

Reference: work/sleep-source/flat-182-44.png. This is the hooded elder appearance, not the Peryrick fixture. Saved asset: mod/Textures/sleep-flat-182-44.png.bytes. Original output: C:/Users/Daniel/.codex/generated_images/01a135ac-12fe-7041-9e19-3dbc601bd8cb/exec-99293d05-d54e-4bf7-909d-ab1edb0ddef0.png. Canvas1024x1536; alpha and five opaque row groups verified. This appearance has asset coverage but no dedicated live native-keeper fixture acceptance yet.

Final prompt:

> Create a classic 1996 Daggerfall pixel-art game sprite atlas for this EXACT elderly hooded male shopkeeper, matching the reference face, weathered pale skin, grey beard and grey hood. He is peacefully asleep lying horizontally on a bed, body entirely covered up to chin in a thick DARK GREEN blanket, white pillow under his head; only face/hood visible. Do NOT draw the bed or frame. Each sprite is a COMPLETE contiguous blanket+pillow+head composite, not separate pieces. Transparent background, no text, no grid, no labels, no extra props or staff. Five separated rows from top to bottom, each row contains exactly ONE complete sleeper: row1 view from head end; row2 three-quarter view between head end and broadside; row3 pure side profile broadside, head LEFT feet RIGHT; row4 three-quarter from foot end; row5 straight foot-end view with face foreshortened beyond blanket. All cameras at modest eye height like a person standing beside a bed, NOT overhead. Consistent single-person identity, blanket color, length, camera height and scale. Broadside is widest, end views narrower, shallow horizontal shapes, never standing figures. Plenty of transparent margins and vertical gaps between rows. Crisp limited-palette authentic low-resolution pixel art, each broadside around 120 pixels wide and 32 pixels high, end around 50 pixels wide and 35 pixels high. No smoothing or painted gradients. Match classic reference style precisely. Output vertical five-row atlas.

## Coverage boundary

SleepingArt.ForNative only returns these two exact archive/record assets. Other native faces are never substituted. Existing two mobile assets retain their previous identity restrictions. Full native/mobile/child idle, walk, run, combat, casting, sleeping and slain-in-bed art requirements remain open where assets do not yet exist.
