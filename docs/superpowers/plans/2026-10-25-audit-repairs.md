# Audit repair implementation plan

**Goal:** Repair the confirmed audit defects and restore meaningful offline verification.

**Authorization:** The user requested fixes to the reported problems. Work stays in the current checkout because the audited implementation is largely uncommitted. No delegation, deployment, or unrelated redesign.

**Architecture:** Keep existing native adapters, semantic contracts, and regression harnesses. Separate barter quote validation from final payment validation. Enforce the existing loopback-only HTTP policy at transport time. Restore damaged tooling using exact machine-format parsers.

**Evidence:** `work/code-audit-20261025/` contains the original source hashes, failing clean-checkout build, parser diagnostics, all test logs, and executable redirect/haggling reproductions.

- [x] Repair six syntactically damaged PowerShell scripts; finish remaining regex use in the affected tooling. Add a parser/policy gate.
- [x] Provision the exact pinned FullSerializer test dependency for a clean checkout and CI.
- [x] Add regression coverage and block automatic redirects in dialogue HTTP paths.
- [x] Permit an unaffordable baseline quote to be negotiated; enforce funds against the final accepted price immediately before mutation.
- [x] Preserve player-allied city-watch actors and spawners during finite-guard cleanup.
- [x] Update obsolete test fixtures to the current semantic and source-admission contracts. Preserve negative assertions; do not weaken production validation to accommodate fixtures.
- [x] Rerun syntax, clean-checkout, core, adapter, focused C#, and Python suites. Review changes against the saved pre-fix files, document any native-only limits, and capture verified results in Obsidian.

Results and remaining native-only verification: [audit report](../../audits/2026-10-26-code-audit.md). User additionally requested a README update, commit and push; those delivery steps are authorized.
