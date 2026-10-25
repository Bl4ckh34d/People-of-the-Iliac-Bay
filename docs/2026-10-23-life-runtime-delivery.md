# NPC daily-life runtime delivery — 23 October 2026

> Historical delivery record. The current [Roadmap](ROADMAP.md) and repository AGENTS.md determine scope and agent settings; this document does not authorize removed systems or obsolete delegation settings.

The user authorized implementation with Sol/high agents. This delivery follows the accepted work–injury–treatment–recovery lifecycle and the LIFE-21/22 requirements. It does not resume deferred aged-memory compaction or STT.

## Work ownership

- Healthcare: inspect and close bounded treatment/recovery coordination gaps in the existing runtime and focused regression suites.
- Inn transactions: persist bounded replay protection independently of the transient visit, using one settlement cursor per buyer. No invented spending reserve or income is added.
- Resource ownership: preserve actual household contents and prevent renewed native stocking after NPC use. Additional profession-specific starting items are deferred.
- Native acceptance: require full autonomous recovery, released care ownership and no extra treatment consumption in the disposable fixture.
- Parent: review changes, execute combined CPU and runtime checks, build only after all agents finish, then run owned native acceptance and update evidence.
- Core audit: map existing needs, goals, tasks, effect receipts and persistence to the remaining common-system gaps before adding further feature-specific behavior.

The user clarified that shared Core Systems take priority. These changes harden existing action handoff, transaction replay and finite ownership contracts. Healthcare remains their first vertical acceptance case; this delivery does not claim to finish a generic autonomous planner.

## Acceptance boundaries

Existing saves, native ownership, quest protection and unrelated dirty work must survive. No new models or independent GPU workers. Tests distinguish controlled fixture setup from ordinary runtime choices. Any failed native acceptance remains documented; CPU passing alone does not establish gameplay completion.

## Results

The implementation is frozen and independently reviewed. The review found and corrected a native activation seam: protecting a chest only during an NPC transfer still allowed DFU to clear older nonempty contents when the player later opened it. Preparation now preserves any nonempty authorized home container, including freshly generated stock, before returning it for use.

- External full healing cancels obsolete care work while preserving the observed physical return position. A failed return-state write keeps care recoverable rather than silently releasing it.
- A completed, receipted partial treatment reopens care immediately. Unverified or failed treatment retains the existing retry backoff.
- Schema 45 persists one latest inn settlement cursor per buyer. Removing the visit, changing wallets through an unrelated transaction, and reloading cannot charge the same visit again. Changed replay terms are rejected. A different visit whose planned end is older or equal is conservatively recorded as unavailable; a later visit can buy normally. Schema 44 migration can recover only receipts still attached to observed visits, not already discarded history.
- Household containers retain exact contents and do not refill for free after NPC use. Untouched empty containers can still receive their initial native stock.
- The shared arbiter, goal/task association, authoritative resource reservations and bounded long-term ledger retention remain open. ROADMAP CORE-01–05 and the existing mindmap's corresponding system details record this sequence. Autobiographical memory compaction remains deferred.

Parent verification: Core **1692/1692**; focused healthcare **15**, choice **6**, health need **9**, health context **4**, and inn **15**, all passed. RuntimeCompile passed with zero warnings and errors after correcting an uninitialized probe local. TownTest PowerShell parsing passed. Logs are under `work/life-core-20261023/`. Mindmap structure remains 22 systems and 57 LIFE features with valid references; this edit only changes system detail text.

Unity verification/build and installation passed. Dist and installed bundle both have SHA-256 `7c1736f9e6510288a6825282352c0e961dfb39fad7c29ab321a2844e0edef1a9`. The portrait cache reused all 20 entries and 2160 unique textures.

The final native healthcare run passed: the ordinary coordinator healed the resident from **15 to 76 HP** with **13 exact one-unit owned potion consumptions**. Native Health need fell from **81 to 0**. Care became Recovered with no task ownership; the resident physically returned, the terminal return action cleared, and the ordinary schedule overlay passed through. Subsequent updates consumed nothing. Native effect rollback and stale replay rejection passed. The exact fixture was reloaded, all 13 saved files retained their original hashes, and the owned game process exited. No game, Unity, Gemma or Breeze process remained at the final check.

The first native attempt failed because the probe conflated care release with completion of its physical return and allowed only 30 seconds. The production return code can itself defer a repath by 30 seconds. The corrected probe separately observes care release and physical return, emits route-state diagnostics, and allows 120 seconds for the latter. The successful rerun showed actual movement while care was already Recovered, then required the same final no-active-action and ordinary-overlay conditions. No production return flag was cleared to satisfy the test. Evidence: `native-first-failed.log`, `native-final-pass.log`, `verification-summary.json` in `work/life-core-20261023/`.

This proves the owned-potion healthcare path in the disposable fixture. It does not establish a complete native bed/Inn/guild walkthrough, every physical route, or a common autonomous planner. Inn replay and home-container ownership changes have Core coverage and compiled/installed native integration, but no new complete native walkthrough in this delivery.
