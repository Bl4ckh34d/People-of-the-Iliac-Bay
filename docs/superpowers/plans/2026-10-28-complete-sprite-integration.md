# Complete NPC sprite integration

User scope: integrate completed sprite sets; omit incomplete sets. All generation agents remain interrupted. Work is performed in the existing checkout because it contains the current actor and traversal integration; unrelated changes are preserved.

Architecture: export a pinned, provider-qualified catalogue of complete eight-direction actions. A single optional resident visual reads actual actor states and preserves native body size, foot placement, concealment and sleep/seat ownership. Existing sleeping and split-portrait loaders receive complete additions through their established formats. Missing actions retain existing visuals. No new movement or combat mechanics are introduced.

- [x] Verify selected frame hashes, directions, phases and excluded repairs; produce reproducible package plus selection receipt.
- [x] Test catalogue completeness, source-provider matching, state priority and phase selection before implementing runtime policy.
- [x] Add lazy atlas loading and one resident visual, wired to existing movement, attack, climb/jump and local dungeon water state.
- [x] Export complete DET sleepers into the existing sleeping catalogue.
- [x] Promote complete portrait art to the existing catalog/package without changing voice assignment.
- [x] Run focused regressions and compile all mod sources.
- [x] Build/install serially when no Unity/game process is running; verify installed bundle and packaged resources.
- [x] Record exact inclusion counts, exclusions, verification and remaining visual acceptance limits in a durable integration receipt and Obsidian.

Outcome: 91 body action sets / 2,000 frames; 68 new sleepers; 12 new portraits. Native running and all new portraits passed their Player probes. Exact receipts and remaining visual limits: `art/production-20261028/RUNTIME-INTEGRATION.md`. Temporary-file cleanup was rejected by automatic approval review; no bypass attempted.

Files: `tools/BuildCompletedNpcSprites.py`, `src/PeopleOfTheIliacBay.Core/NpcSpritePolicy.cs`, `mod/Scripts/CompletedNpcArt.cs`, `mod/Scripts/CompletedNpcPose.cs`, actor lifecycle/movement and native combat hooks, `tools/Deploy.ps1`, portrait catalog and new self-contained portrait art folders. Tests: `tests/CompletedNpcSprites.Regression` and exporter validation. Generated package: `mod/Textures/npc-complete-*`; source art remains unchanged.

Constraints: no regex; no agents; no new generation; no destructive cleanup of existing assets; no invented provider mappings; no build concurrent with Unity or the game. Static quality review and package verification do not claim in-game visual acceptance.
