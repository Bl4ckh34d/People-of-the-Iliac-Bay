# Roadmap implementation checkpoint — 23 October 2026

Three bounded roadmap slices are implemented in the current working tree: guided portable-save transfer, a separate commerce opt-out, and model-selected personal inventory reads. This checkpoint is source and CPU verification, not a newly installed game package or general roadmap completion. Six Astra/high agents covered implementation, source audit and independent reviews. The concurrent model-test and portrait chats retain their GPU/game responsibilities; no model, TTS, Unity or DFU process was started or stopped for this work.

## Guided portable saves — IMM-20

`tools/MemoryArchiveGui` adds a Windows Forms helper beside the existing `MemoryArchiveTool`. Export selects an existing SAVE folder and a new ZIP. Import selects a package, the configured Saves folder and an unused slot. Both show resolved paths before confirmation; Cancel starts no subprocess. The GUI invokes the bundled CLI with structured arguments and shows its errors and recovery guidance. Success requires a zero exit code and the expected output artifact.

The existing CLI retains archive/checkpoint validation, no-overwrite publication and the running-game guard. A failed import may leave unreferenced immutable chunks; it does not promise rollback of those chunks. No player save was opened. `tools/MemoryArchiveGui/Publish.ps1` builds both self-contained win-x64 executables and help text into `work/MemoryArchiveGui/win-x64`. Keep both executables together.

Verified: 19 workflow checks, including real temporary-fixture export/import, a slot occupied after review, missing history, exact spaced/Unicode process arguments and cancellation. Sixteen offscreen UI checks cover both tabs, destination display, occupied-slot rejection and minimum-size layout; exported PNGs were inspected. The GUI Release build and self-contained publish pass. The later standalone published-backend smoke attempt correctly refused to run while DFU was active. A clean-machine run, manual native file-picker interaction and loading an imported save in DFU remain open.

## Separate commerce opt-out — P04/E02

`Dialogue.DispositionCommerce` defaults to true, preserving existing installations when no local override exists. Turning it off bypasses personal price adjustments and mod-added commerce refusal for the supported merchant, shelf, sale, repair, identification, inn and paid guild-service paths. Conversation tone, hostility and persisted relationships are unchanged. Existing `ConversationDisposition`/global gates retain their earlier behavior; this is a separate opt-out, not a redesign of all module dependencies.

The runtime adapters and the native pricing hook both consult the switch. Hook-level gating matters: an unresolved keeper could previously trigger refusal before reaching a runtime callback, and the food hook could still clamp a price. Both paths now pass through to native behavior when commerce is off. The original eight-argument hook constructor remains available; the runtime factory supplies the new enable delegate.

Verified: 10 regressions compile and exercise the actual runtime partial and native pricing hook, including the reflection factory, live flag changes, ownerless trade, food prices, state preservation and shipped defaults. The actual quest-hook assembly builds with zero warnings/errors. Existing displayed quotes can retain captured totals until their window is reopened. Native quote/payment/refusal acceptance for this new setting remains open.

## Personal inventory lookup — bounded IMM-03 slice

The existing ordinary social-dialogue selector can now choose NONE, one public knowledge topic, or one read of the current speaker's inventory. There is no second selector loop or keyword routing. Inventory queries allow at most 64 characters for the name filter, 40 for the category, page zero and 12 result rows. Selector output is capped at 128 tokens. With no eligible public topic or captured inventory source, the selector is skipped and context explicitly reports inventory unavailability.

Managed, living, non-quest-protected speakers require the exact current native inventory binding. Detached native payloads must agree with the saved owner and revision. Reading no longer calls `TryCapture`, which could commit items or refresh a binding. It cannot authorize transfers or read shop stock, other people or containers. Static/unsupported speakers return unavailable. Public-topic disclosure filtering remains intact.

A selected inventory result retains detached evidence tied to the request's immutable binding. The final stage checks live request/speaker/scene/save authority plus native binding identity, saved revision and exact payloads before publishing. Same-request successor snapshots retain the dependency. Inventory-derived responses are buffered and bypass delayed text reveal, so speech/text cannot appear before that check. Other social replies retain streaming behavior. Full inventory validation runs at stage consumption rather than per-frame pending-indicator polling.

Verified: 73 focused assertions cover parsing, bounds, topic isolation, paging/filtering, pure read authority, mutation during generation, same-revision payload changes and request/owner/scene/save provenance. Existing public-knowledge regressions pass 42 assertions. Runtime compilation passes. Live model selection quality, full native inventory conversation and latency remain unverified; `ReadPlan` and `RecallMemory` are still unavailable through this tool adapter. This does not complete IMM-03.

## Combined verification and evidence

- `dotnet run --project tests/PeopleOfTheIliacBay.Tests -c Release`: 1650/1650 passed, zero failures.
- `dotnet run --project tests/DecisionAdapter.Tests -c Release`: 86/86 passed, zero failures.
- `dotnet run --project tests/DispositionCommerce.Regression -c Release`: 10/10 passed.
- `dotnet run --project tests/NpcConversationLookup.Regression -c Release`: 73 assertions passed.
- `dotnet run --project tests/NpcKnowledgeTool.Regression -c Release`: 42 assertions passed.
- GUI workflow/rendering: 19/16 checks passed respectively.
- `dotnet build tools/RuntimeCompile -c Release --nologo` and the actual quest-hook build: zero warnings/errors.

Root verification logs and pre-edit file copies are under `work/roadmap-20261023`. The working tree contains extensive earlier uncommitted development, including the project rename; this work neither resets nor commits that shared state. The installed bundle, model choice and portrait assets were not changed.

## Remaining roadmap gaps found in current source

1. **Semantic memory compaction:** immutable archives and month eligibility exist; summaries, source-linked publication, retrieval integration and an idle scheduler do not. Start with versioned summaries and raw fallback while preserving every referenced branch; do not delete raw chunks yet.
2. **Autonomous goals:** typed goals/needs already persist, but ordinary runtime producers and replanning are absent. `TryCommitNpcMotivationUpdate` currently has probe callers. Implement one native event-backed motive and verified existing action/outcome before general planning.
3. **Static actor coverage:** ordinary interior actors already receive partial identity/death/awareness support; merchant housing exists. Broader institutional beds, role-specific mobility and reviewed quest adapters remain incomplete. Use the census to select a narrow service-preserving role.
4. **Module acceptance and STT:** free text, planner and voice settings exist; the full failure/toggle matrix needs native acceptance. Planner changes remain restart-only. STT has no chosen production backend.
5. **Companions:** actor stats, inventories, contracts and task actions provide foundations. Recruitment/party ownership, paid lifecycle, travel persistence, runtime skill progression and specialized assistance remain missing or incomplete. They require a dedicated feature delivery rather than treating Follow as a complete companion system.

Unchecked historical roadmap entries are not proof of absent source. Keep source implementation, CPU proof, model quality and native gameplay acceptance distinct when selecting the next slice.
