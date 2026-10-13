# Vanilla settlement bed census — 14 October 2026

Read-only scan of the installed original game assets through pinned DFU 1.1.1 readers. `BedCensus.Run` visited 3,444 cities, hamlets and villages, using 491 distinct RMB blocks and 5,780 block/interior record combinations. The editor's model catalog labels models 41000, 41001 and 41002 as beds. These counts describe model instances, not sleeping capacity or navigable sleep anchors.

| Internal building type | Distinct block/interior records used | Bed counts observed |
|---|---:|---|
| House1 | 11 | 1 |
| House2 | 2,847 | 0, 1, 2, 3, 4, 5, 6, 8, 9 |
| House3 | 253 | 0, 1, 2, 3, 4 |
| House4 | 776 | 2, 3, 4, 5, 6, 8, 9 |
| House5 | 1,307 | 0 |
| House6 | 14 | 0, 4 |
| HouseForSale | 66 | 0, 1, 2, 3, 4, 5 |
| Alchemist | 28 | 1, 3 |
| PawnShop | 13 | 2 |
| Tavern | 196 | 0, 2, 3, 4, 5, 6, 10, 13, 14, 15 |

House1–House6 are engine classifications, not bedroom or family-size tiers. Block/interior combinations can repeat the same layout, and their reuse across settlements is recorded separately in the CSV. Zero bed models is not evidence that a building is a suitable empty home: geometry, access, current NPCs, ownership and quest use still need inspection. Rest markers (texture 199, record 4) are counted separately; DFU notes that these sometimes substitute for entrance markers, so marker count is not a reliable bed count.

Gothway Garden contains 143 House2 candidates (0, 1, 2, 3, 4, 5 or 9 beds), six House3 candidates (0 or 1), and thirteen House5 candidates (zero). Its three taverns have 3, 4 and 6 beds. The earlier 162-house population census therefore cannot yet be treated as 162 valid residential households.

The scanned vanilla alchemist/pawn-shop interiors do contain bed models. This does not invalidate the requirement: any shop whose effective layout lacks suitable living quarters, including modded layouts, sends its owner to a separate assigned home after closing. No visual or accessibility audit of every shop is claimed.

Allocation follows the user's clarified policy: beds and floor/room size are weak preferences; couples and children can share beds; larger homes favor larger families; maximum household size is five. Do not enforce one NPC per bed or use this table alone to allocate residents.

Reproduce with `tools/Editor/BedCensus.cs` deployed to an ignored Unity Editor folder and Unity `-batchmode -nographics -quit -executeMethod BedCensus.Run`. Evidence: `docs/data/bed-census.csv`, local `../work/bed-census.log`. Original game archives remain unchanged. City-overhaul replacement layouts need their own live census.
