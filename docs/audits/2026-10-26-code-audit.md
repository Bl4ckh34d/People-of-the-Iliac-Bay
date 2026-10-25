# Code audit and repairs — 26 October 2026

## Scope

This audit covers the current development source, executable regression projects, Python evaluation tooling, PowerShell launch/test tooling and CI setup. It combines source inventory, compilation, automated checks and focused review of transport, barter, guard cleanup, persistence fixtures and damaged scripts. It is not a claim that every code path or native gameplay scenario has been exercised.

The checkout already contained a large uncommitted rename and feature baseline. The published source checkpoint includes that baseline because the repairs depend on it. Local art-production workspaces, generated previews, model weights, game files, saves and temporary audit outputs are not published as release assets.

## Confirmed problems and fixes

1. **Local dialogue requests could follow redirects.** Validating the initial loopback URI did not constrain automatic redirects, and the ambient proxy could change where a request went. All three dialogue HTTP paths now use `LocalDialogueHttp`, which validates the endpoint, disables automatic redirects and disables the proxy. A real loopback-server regression checks normal responses, rejected endpoints and HTTP 301/302/303/307/308 without forwarding the private prompt.
2. **An unaffordable baseline quote blocked affordable haggling.** Preview preparation enforced final-payment affordability before negotiation. `PersonalBarterContract.CanQuote` now preserves item, identity, consent and revision validation while postponing payment validation. Mobile and static windows use quote validation; the commit path rereads and validates the final price with `CanBegin`. Native player capacity includes the final gold-weight change. The focused regression accepts a 100-gold quote with a 95-gold wallet, rejects payment at 100, and accepts the affordable final price; protected items remain rejected.
3. **Finite-guard cleanup could remove player allies.** Native city-watch cleanup now excludes `MobileTeams.PlayerAlly` bodies and `AlliedToPlayer` spawners. Runtime compilation verifies the integration; preservation in a live game still needs native acceptance.
4. **A clean checkout lacked required serializer assemblies.** `RestoreTestDependencies.ps1` downloads a SHA-256-pinned official DFU 1.1.1 release and extracts the serializer and Unity managed dependencies. It does not overwrite installed assemblies; a conflicting existing serializer fails. CI provisions these dependencies before running the core suite. The README now describes the actual prerequisites.
5. **PowerShell tooling was syntactically and behaviorally damaged.** Six scripts had 54 parser errors, including truncated assertions and malformed hash-check arguments. Recovered scripts use literal or structured machine-format parsing. Town acceptance receipts are checked in `LogProtocol.ps1` and `TownTest.CurrentAssertions.ps1`; negative tests reject malformed, cross-row and duplicate evidence. Portrait builder execution also caught a split `$ErrorActionPreference` assignment that parsing alone could not detect. Current healthcare, household, commerce and player-healing proof selection is retained. Removed hunger/home-meal probes fail explicitly instead of claiming obsolete acceptance.
6. **Obsolete fixtures made 12 C# regression projects fail.** Repairs restore missing source links, replace removed lexical APIs with current typed contracts, establish genuine conversation capabilities, and mark synthetic legacy inboxes with their actual legacy version. The poison migration fixture retains its source evidence while removing fields that did not exist in that version. The refuge-expiry scenario now occurs after the actual one-hour seal. Production source-admission validation was not relaxed for these fixtures.
7. **Factor evaluation used stale source/capability assumptions.** Synthetic actions now obtain a real current conversation turn; historical inbox fixtures retain coherent source cursors. Archived speech-candidate rows validate against their pinned original appraisal fixture, rather than a later edited file. All 97 factor tests pass.
8. **Host launcher defaults and source ownership were inconsistent.** The parent-workspace launcher scripts are now versioned under `tools/host`; the missing planner setting defaults to Gemma + Breeze, consistent with mod settings. Mocked launcher and lifecycle tests verify ordinary and explicitly enabled planner paths without starting the game.
9. **Remaining regular-expression use violated project policy.** Affected C#, Python and PowerShell tooling now parses machine formats without regex. Natural-language memory grading uses the existing model contract rather than keyword grading. PowerShell AST checks and Python/C# policy checks run in CI. These static gates catch the supported patterns; they are not a general proof against every possible dynamically constructed regex call.

## Verification

The final offline run passes **88 executable C# test projects**, including **2082/2082 core checks**, **89/89 decision-adapter checks**, **9/9 personal-barter checks**, the transport regression, native-serializer round trips, archive CLI/GUI logic and inbox stress. Two separate projects were intentionally excluded: `MemoryArchiveGui.Rendering` requires UI rendering, and `SemanticRouting.Regression` requires the live model endpoint.

All **490 Python tests** pass: Breeze 221, architecture 158, interaction 14 and factor 97. The architecture suite uses `tools/NpcArchitectureEval/tests` as its discovery directory; discovering from its parent runs no tests and is not acceptance.

Both runtime and quest-hook compilation finish with **zero warnings and zero errors**. Source checks cover **1318 C# files**, **116 Python files**, and **67 PowerShell scripts**, excluding generated work/build folders. Additional checks pass for typed receipts, deferred publication, mocked host launch/service ownership, decision-model selection, speech launch arguments, archive transfer, native-audio protocol, dialogue model selection, portrait original locks and portrait workspace preparation. The portrait workspace test requires Windows PowerShell 5; the syntax and CI tools use PowerShell 7. Quest inventory parses the 265 local authored quest files without regex.

The archived source tree is also checked without existing build outputs using the README's dependency provisioning and core/adapter/barter/transport commands. This is local clean-checkout verification, not a claim that remote GitHub Actions has finished.

Reproduce the CI subset using the README. Additional deterministic suites:

```powershell
python -m unittest discover -s tools/BreezeTtsTrial -p 'test*.py'
python -m unittest discover -s tools/NpcArchitectureEval/tests -p 'test*.py'
python -m unittest discover -s tools/NpcInteractionEvaluation -p 'test*.py'
python -m unittest discover -s tools/NpcFactorEvaluation -p 'test*.py'
pwsh -NoProfile -File tools/TestLocalDialogueModel.ps1
pwsh -NoProfile -File tools/TestHybridGameLauncher.ps1
pwsh -NoProfile -File tools/TestMemoryArchiveTransfer.ps1
powershell -NoProfile -File tools/TestPortraitThinkingWorkspace.ps1
```

Some optional evaluation and art suites require the separately prepared local fixtures described by their own READMEs. The CI subset does not require a model, game session, original game data or portrait-production workspace.

## Limits and retained evidence

No game was launched for this repair verification. No repaired bundle was built, installed or accepted in a new game session. Live model grading, native guard preservation, the final haggled-price/encumbrance path, restored TownTest gates and in-game presentation still need native verification. Historical native results in `TESTING.md` remain tied to their original bundles.

Local evidence is retained under `work/code-audit-20261025/`: original source hashes, pre-fix copies, initial failures, focused reruns, clean-checkout output and final per-project logs. Those generated files are intentionally ignored by Git. The source tests, policy gates and this report are versioned.
