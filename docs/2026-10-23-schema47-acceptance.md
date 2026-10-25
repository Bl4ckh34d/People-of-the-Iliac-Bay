# Schema 47 acceptance checkpoint

Status: schema 47 built and installed; native healthcare, DFU serializer and conversation-command acceptance passed. The full Core roadmap remains open.

## Scope

This checkpoint adds a bounded motivation receipt tail with current-row and goal-origin anchors, native health/safety source cursors, conversation-turn admission capabilities, exact item-transfer projection markers, and retirement of expired healthcare lodging bookings. Tasks and actions are not pruned. The experience inbox remains unbounded. CORE-04 therefore remains open.

Conversation capabilities bind asynchronous callbacks to the exact player turn and close with the conversation. Generic task/action admission paths still exist; removing those paths requires classified care and task-step producers before terminal task/action retention can be enabled safely.

## Verified evidence

- The main Core suite passed 1692/1692 in Release with writable TEMP/TMP. Evidence: `work/life-core-20261023/temp/motivation-fixture-full-core.log`. Six tests now use actual saved injury observations instead of the generic motivation write API rejected by schema 47.
- The final runtime source compiled through `tools/RuntimeCompile/RuntimeCompile.csproj` with zero warnings and zero errors after integrating the native framework persistence check into the healthcare probe.
- `tools/TownTest.ps1` parsed without PowerShell syntax errors after adding mandatory schema-47 serializer and conversation command markers.
- Before native acceptance, all 13 files in `work/life-core-20261023/fixture-baseline-hashes.json` still matched their recorded SHA-256 values.
- After fixing missing latest-row anchor validation, a fresh serialized Release run passed Core 1692, goal binding 11, arbitration 14, atomic care admission 7, healthcare 15, lodging retention 6, conversation commands 10 and transfer projection 6. Logs: `work/life-core-20261023/temp/schema47-final-*.log`. These runs precede the additional copied-anchor/source-cursor consistency work and are not final evidence for that later change.

Review found that a task-pinned goal-origin receipt could incorrectly substitute for a missing latest-state receipt after the receipt window advanced. Current-row validation now reads only the tail or latest-row anchors. A dedicated safety regression covers rejection while preserving a valid old task's goal origin. Current Health source revisions must also equal their per-kind cursors; current Safety digest and phase must match the durable start/closure cursors. Copied-old-anchor variants now reject while leaving the source input unchanged. This is a consistency guarantee, not authentication of arbitrarily rewritten save files.

After these fixes, the Health Pair suite passed 8/8 and Safety passed 11/11, including 42,000-revision simulations and legacy migration with the original incident absent. The reviewed main Core run passed 1692/1692 again. Fresh Release runs also passed goal binding 11, arbitration 14, atomic care 7, healthcare 15, lodging retention 6, conversation commands 10, transfer projection 6 and health needs 9: 78 focused cases. Evidence: `work/life-core-20261023/temp/schema47-reviewed-*.log`. RuntimeCompile passed with zero warnings/errors. All agent edits were frozen and the parent reviewed the source consistency changes before starting the package build.

Native gameplay evidence is recorded separately below. The healthcare run exercises DFU FullSerializer, actual potion consumption, full recovery, goal reconciliation, physical routine return and exact baseline reload. The command run exercises real action execution and rejects both same-turn replay and a fresh unconsumed turn after session close.

## Remaining acceptance

Task/action retention, classified generic admission and their recall/projection prerequisites remain open. The proposed population level ranges are not part of schema 47.

## Installed package and native healthcare

`tools/Build.ps1` completed successfully after all agent edits froze and CPU checks passed. Distribution and installed bundles have identical SHA-256: `1EE3D182829FFDCC008496E6A08EB9C929FE3269E8C2277DE96FAD5DA9052EC9`. Build evidence: `work/life-core-20261023/schema47-package-build.log`.

The owned `PeopleOfTheIliacBayTest-9a5f197b` / `People - Gothway Garden` healthcare probe exited 0. DFU FullSerializer retained schema-47 health motivation, an accepted conversation task, issued ordinal 2 and accepted ordinal 1, and rejected a pre-load live command in the restored store. Existing knowledge, fear, service funding and speech-affinity checks also passed.

Actual native treatment healed 15 to 76 HP using five potions at native level 1; Health need intensity fell from 81 to 0. The NPC completed physical routine return, released care ownership and consumed no extra potion. Native rollback and stale replay checks passed. The exact original fixture reloaded, and all 13 baseline file hashes remained unchanged. Evidence: `work/life-core-20261023/schema47-healthcare-native.log` and `schema47-healthcare-player.log`. These probes disable model prewarm and voice only in their owned game process.

## Native conversation commands and transfers

The same owned fixture passed `-DailyItineraryProbe -PhysicalDailyRoutine -DailyInteriorOnly -NpcActionProbe -NpcConversationCommandProbe` with exit 0. The typed command completed its native Wait action, persisted accepted ordinal 1, rejected a fresh-ID replay from that turn, then rejected an unconsumed second turn after closing the session without advancing accepted ordinal 1. This uses the production StreetPopulation adapter and a controlled command session; it does not claim a live Gemma/UI conversation test.

The run also verified physical MoveTo arrival, timed Wait, following a moved player, cancellation, real one-item handover, exact inbound player-to-NPC transfer with a single ItemReceived projection, and physical two-item delivery to another managed NPC. Logs: `work/life-core-20261023/schema47-command-native.log` and `schema47-command-player.log`.

After both runs, all 13 fixture baseline hashes still matched. All 656 deployed runtime/Core source files matched the checkout, and distribution/installed bundle hashes still agreed. An unsandboxed process check found no running DaggerfallUnity or Unity process. No automatic shutdown occurred.
