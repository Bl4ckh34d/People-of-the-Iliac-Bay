# Corpse cleanup and fixed inquiry window

The guard removes the physical body on arrival. Witness questioning starts at the actual cleanup time and lasts at most thirty game minutes. The separate departure investigation still reserves enough return travel to end the scene within the existing one-hour bound. Repeated presence observations, reloads and later participants cannot reset this deadline.

The current code incorrectly requires first player departure plus thirty minutes before questioning. Remove that gate in both Core and runtime. Preserve the independent thirty-minute absence rule for automatic offscreen cleanup.

Persist an explicit cleanup-time anchor on the outdoor investigation report. Derive its deadline from that anchor and the existing case cap. Validate the anchor against the exact corpse during whole-world restore and reject future fields in old schema headers. Keep historical inquiry receipts distinguishable from new-window receipts so migration does not rewrite old testimony.

If an assigned responder cannot reach the scene before the shortened deadline, return it along the surveyed route from its actual travelled point. Never jump the guard to the corpse merely to begin its return. Record no arrival for that guard. Reuse this fixed deadline when adding the requested shared-case recruitment later.

Verification starts with failing regressions for immediate inquiry after early physical cleanup, no-departure cleanup, nonrenewal across observations/reload, forged deadlines, and continuous return of a late responder. Then run the corpse suite, full Core, runtime compilation, and native scene acceptance after the remaining report/suspect integrations are ready. Native acceptance remains a separate gate.

Result: schema 64 persists the report cleanup anchor and distinguishes new-window inquiry receipts from legacy receipts. Initial tests failed as expected; current corpse suite passes 24/24, full Core 1786/1786 and RuntimeCompile has zero warnings/errors. Read-only review found no blocker. Package `41CB9D035C417…` is built and installed; the actual game serializer passes early cleanup and receipt provenance, along with native healthcare/self-healing and exact fixture restoration. Native visible corpse/custody acceptance remains open. Shared-case recruitment and typed suspect work continue in the adjacent implementation plan.
