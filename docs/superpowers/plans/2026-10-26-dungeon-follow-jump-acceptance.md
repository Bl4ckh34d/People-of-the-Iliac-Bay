# Dungeon Follow and bounded jump acceptance

**Goal:** Finish the existing native dungeon Follow acceptance and make short real jumps usable by its existing physical movement owner.

**Scope:** The existing roadmap authorizes dungeon navigation. Preserve the existing jump adapter, native doors, actual floor collision, resident identity and consent. Do not add climbing or platform boarding without their actual native object and geometry evidence. No Unity build or Player run without the root agent's serialized slot.

1. Read current Follow/jump source, roadmap, worklist and Obsidian evidence. Run the current Core suite and runtime compilation.
2. Make the existing jump probe runnable against real installed dungeon geometry. Reuse the existing native Privateer's Hold entry helper, sample actual static NavMesh floor candidates, and refuse missing/disconnected/unreachable geometry honestly. Test setup placement stays separate from physical movement acceptance.
3. Reproduce short disconnected-floor Follow arrival: the existing `<=1.8m` air-distance rule incorrectly considers a near floor across a gap reached. Extend the native probe to exercise the production Follow destination seam before launch.
4. Correct only that target selection after a failing native or offline behavior test: native complete short walking route allows standing; otherwise the existing jump helper must prove a real clear landing. Do not aim a jump at the player's occupied capsule.
5. Run focused native jump and existing Follow round-trip/Stop probes when the shared slot becomes available. Record exact geometry, native motion, failures, identity and evidence boundaries.
6. Run Core/runtime checks, write the scoped audit and capture verified reusable findings. No commit or push.
