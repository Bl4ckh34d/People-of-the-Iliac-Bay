# Immersion simplification implementation plan

User approved the audit recommendations with explicit corrections on 26 October 2026. Implement with GPT-6.1 Sol / high workers. This plan supersedes the audit proposals where the user supplied a different requirement.

## Approved behavior

- Missing sleeping art cannot block rest/healing. Use a standing pose beside the assigned bed until art is available. No sprite production in this task.
- Travelers retain one reserved origin household, one bed per person, family surname and relations, identity, personality and travel purpose. Do not survey a whole origin town for this. Prepare return route when return begins. Another chat owns room removal and lodging capacity.
- Select food/drink at the counter. One group member must afford the whole group's purchase; otherwise obtain money at the bank beforehand. No exact future menu prerequisite.
- Simulate unobserved group visits coarsely; choose adjacent seats when observed/entered. Preserve real purchases and stable identities.
- Select useful daily destinations incrementally. Resume interrupted routines with the next relevant commitment or home, allowing late sleep and skipping optional errands.
- Replace companion escrow machinery with one actual payment per begun 24-hour service block; preserve failed-start rollback and duplicate-payment protection.
- Remove artificial guild potion stock. NPC purchases must not deplete native shop stock; player purchases retain native behavior. NPCs still pay and receive real owned items.
- On household days with children, approximately half retain one adult at home; the other half permit both adults out with the child locked inside. Adult present means unlocked by day; night/all asleep means locked.
- Assign guard rest beds only when the player enters the wall/quarters; preserve shifts, disappearance through a real entrance and stable guard identity.
- Preserve point 11 completely: current context, observed events, companions and technical/history retention remain unchanged. The user explicitly prefers their immersion benefit and deferred any performance optimization.
- No obsolete development-save migrations. Current-format save/load remains supported. Do not delete saves.
- Use one LLM plus TTS. Remove separate D3 and Decision Shadow infrastructure, while preserving same-model goal extraction, multi-pass parameter resolution, willingness and native validation. The 22–23 October real-model study already demonstrated six of six five-step plans with Gemma E4B; multiple calls do not imply multiple models.
- Stationary guards who see an active guard pursuit of the player must join. A real guard's "Halt!" shout also recruits guards within 200 metres of the shouting guard without requiring line of sight. Murder investigators should search varied reachable points and interview nearby NPCs/witnesses instead of oscillating.

## Execution and ownership

All workers receive isolated source copies of the current dirty checkout and return a change manifest and focused tests. They do not build/install Unity packages, launch the Player, call model APIs, commit, push, alter the other chat's files, or delegate further. Parent owns integration, shared test registration, cross-scope review, current-state compilation and final evidence.

1. Health and NPC commerce: art-independent rest; non-depleting NPC purchases; remove synthetic guild stock.
2. Traveler origin and return lifecycle: preserve one real household and person, defer return preparation.
3. Family behavior: group funding, counter menu, coarse offscreen Inn state, 50/50 childcare and doors.
4. Routines: incremental destinations and practical recovery after interruption.
5. Paid companions and guard rest: simple payment; deferred bed materialization.
6. Persistence only: remove old save conversions; preserve memory and context unchanged.
7. Parent: single-LLM consolidation, coordination with Audit mod code, integration and verification. First available worker takes the additional guard pursuit/search task.

## Per-scope acceptance

- [x] Capture baseline and focused failing regression for the behavioral change.
- [x] Implement the smallest working change; remove superseded logic rather than add another parallel subsystem.
- [x] Run focused positive/negative tests for behavior, identity and actual money/item effects.
- [x] Report exact changes and any dependency on another scope.
- [x] Integrate against current files, preserving simultaneous room work.
- [x] Run core and affected adapter tests plus runtime compilation on the combined source.
- [ ] Coordinate any native run/build with the other chat; do not claim native acceptance from source tests.

No global cleanup, art work, speculative economic simulation, or save rescue. Native collision safety, ownership, witness knowledge, quest protection and asynchronous response identity guards remain required.

Integrated source results and native acceptance boundary: [result report](../../audits/2026-10-26-immersion-simplification-results.md). The separate room-audit chat was notified that source is ready for its next serialized Unity build; this task did not compete with its running editor.
