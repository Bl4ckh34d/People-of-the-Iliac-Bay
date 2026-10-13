# Quest-to-household binding implementation plan

Use executing-plans and test-driven-development within the existing isolated mod repository. The user has authorized implementation and testing of this compatibility requirement.

**Goal:** Keep native and mod-added destination quests consistent with persistent households before player-visible names are published, while preserving fixed identities and existing quest contracts.

**Architecture:** A standard-mod adapter at native quest parsing supplies stable person/site bindings before macro expansion. A persistent binding policy must distinguish procedural residents, fixed names, guests, captives and changed quest destinations. Native resource IDs and tasks remain authoritative. No engine edits or replacement game files.

**Current evidence:** QuestListsManager.LoadQuest calls QuestMachine.ParseQuest for both disk quests and mod TextAssets. Parser.Parse returns fully constructed resources before guild offer text is expanded. Partial parses create only header metadata. Person serializes displayName separately from nameSeed; Place serializes site details. This is source evidence, not an implemented binding system.

## Sequence

Checkpoint: the interception probe and initial syntactic inventory below passed on 14 October. The probe was first observed failing without the hook, then passed actual native prompt labels, partial/fixed/unrelated exclusions, changed dialog destination, FullSerializer reconstruction, unchanged population snapshot and unpatch. The runner removed the temporary package. Inventory: 265 quests, 125 NPC placement actions. Representative end-to-end coverage selection and semantic classification remain open; no production binding policy is enabled. See `docs/QUEST-IDENTITY.md` for boundaries.

- [x] Prove parser interception in the shipped player using a separate opt-in probe package. First observe an unchanged recipient failing the expected household-name assertion, then attach a Harmony postfix to Parser.Parse. Scope the probe to its synthetic quest, use an existing household resident name at the quest-selected home, preserve UID/symbol/nameSeed/site coordinates, skip partial previews and fixed individuals, and verify native message macros plus quest resource serialization. Remove all patches and the temporary package after verification. Files: probes/quest-binding, editor build helper, opt-in town harness, build/test scripts.
- [ ] Inventory shipped quest construction/placement paths and mod quest-loading paths. Keep syntactic census distinct from semantic residency classification. Select actual main/guild/side quest cases and expose unsupported paths explicitly.
- [ ] Implement persisted binding policy with provenance, pre-publication reservation versus accepted commitment, concurrent quest contracts, explicit role/adaptor support, and old-save reconciliation. Write behavior tests before implementation. Never infer permanent residency solely from a generated home field or rename fixed/already-published contracts.
- [ ] Integrate actual person/site binding, household actor identity and native activation without duplicates. Verify real offer acceptance/rejection, journal/rumors/building labels, target presence and hand-in through actual main/side/mod-added quests and save/load. Public behavior remains disabled until these contracts agree.
- [ ] Review, update canonical compatibility documentation and Obsidian with exact evidence, then commit. Broad quest compatibility remains incomplete until the coverage matrix passes.
