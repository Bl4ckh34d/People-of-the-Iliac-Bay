# Roadmap completion implementation plan

**Goal:** Implement and verify the active roadmap and mindmap, including smooth loading and stable play, meaningful static NPC roles, and substantially larger merchant inventories.

**Authority:** The user requested implementation of the existing roadmap, explicitly prioritizing stalls, static roles and loading stalls. `docs/ROADMAP.md` remains the complete scope; this plan orders work without reducing completion to the first slice. Main agent only.

**Architecture:** Extend existing native identity, role, inventory and commerce adapters. Native metadata establishes professions; proximity alone does not turn every passer-by into a merchant. Preserve authoritative saved inventory after first creation. Do not scan every NPC or generate shop stock on each frame. Add an elapsed-time ceiling to the existing actor creation budget, preserving progress under load. Keep quest actors and native shop-service inventories separate.

**Tech stack:** Existing C# 7.3 Core/runtime, Unity 2019.4.40f1 and DFU 1.1.1; current Gemma/Breeze contracts.

## First integrated slice

- [x] Add failing Core regressions for native merchant role evidence, varied deterministic merchant stock, and count/time-limited actor creation.
- [x] Share the existing exterior witness identity calculation with dialogue and inventory ownership; register static exterior people lazily at conversation entry.
- [x] Project verified native merchant metadata into dialogue. Preserve specific keeper/royal/quest facts and unknown roles.
- [x] Initialize merchant-owned stock once with native item templates; reuse the persisted ledger thereafter. Keep ordinary residents' small personal inventories unchanged.
- [x] Enable the existing personal-trade path for eligible exterior static merchants, with live identity, scene and inventory rechecks.
- [x] Add elapsed-time budgeting to resident body creation, with at least one creation opportunity to avoid starvation.
- [ ] Compile and run relevant existing suites, then exercise actual static trade and loading in an owned disposable native fixture. Record measured limits, not global performance claims.
- [ ] Update roadmap and mindmap to distinguish implemented source, verified native flows and remaining work; capture reusable results.

## Remaining full objective

Track each active requirement in `docs/ROADMAP.md` and the linked mindmap: ordinary/family routines and dense towns; free conversation and mixed barter; Enter/Wait, repair, training, Shield and third-person handover; health and guard pursuit, real-cell arrest and release; additional interiors/dungeons, jumps/climbing/moving platforms; memory, relationships and promises; companion travel/pay/progression; later visible groups, relevant local consequences and release setup. Removed hunger/economic/world-history subsystems stay removed. No item is complete until its claimed scope has current evidence. Native tests, regular-play observations and load/frame measurements are required for their corresponding requirements.

Mindmap source located at `C:/Users/Daniel/Documents/Codex/2026-10-22/wir/outputs/dfu-npc-mindmap-2026-10-23.html`; inspect its current data before updating it.

## Current evidence and user steering

The first source implementation compiles without warnings/errors. Core passes 2084/2084, the static-role/stock harness 1247 assertions, decision adapters 89/89 and barter 9/9. Native verification is still required, including actual market-placement coverage, static window entry, stock persistence and measured frame times. The current merchant classification uses resolved native merchant metadata; geometry-based assignment for an otherwise unclassified stall attendant is not yet implemented. Existing saved stock is not replenished on reopening.

The user reported four trade icons in one drawn slot. Commit `116dd37` fixes the enhanced-list/artwork mismatch by using four single-column cells. On 26 October the user selected **Keep the game running for now**. Do not replace the running bundle or terminate their process. Continue independent source work; installation and native acceptance await a closed game. This constraint does not pause or complete the full roadmap goal.

Later on 26 October, the game was closed and the expanded trade package was built and installed as `A4460B010E2C`. The user then began a playtest. Subsequent bank-clerk movement, cash priority, family rendezvous and waiting-context changes remain source-only. Current evidence is in `docs/TESTING.md`; the broad roadmap and native acceptance remain open.
