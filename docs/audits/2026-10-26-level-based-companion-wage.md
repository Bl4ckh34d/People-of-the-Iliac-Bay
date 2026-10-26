# Level-based companion wage

The user replaced the fixed120g daily fee with10g per NPC level. Quotes read the
actual bound native entity's positive Level. They do not infer level from the
player, skills, archetype or reputation. Unavailable/invalid levels and integer
overflow refuse a quote rather than creating a free or wrapped price.

Semantic action labels, consent, funding and native fixture messages use the same
quoted integer amount. Hire compares the current price with the confirmed amount
before any funding; a changed price requests a new quote. The accepted contract
stores that fee, so a later level change does not reprice an already funded day.
One begun24-hour block, no automatic renewal, failed-admission refund and existing
dismissal rules remain unchanged.

Root checks: Core2162/2162 and RuntimeCompile0 warnings/errors pass. Price cases
cover levels1,5,12,20,30, invalid/unknown levels and the integer boundary. The first
test compilation failed because the new API was absent; this is an API RED,
not a behavioral native RED. Existing native paid-hire probe now asserts actual
level pricing and stale-quote rejection without payment before its real hire.
That native probe remains unrun for this change.

Current ordinary CivilianEntity binding supplies level1 where DFU leaves level0;
positive native/custom levels are preserved. Consequently an ordinary generated
level1 companion quotes10g. This change does not introduce an additional seeded
level distribution or a character-level advancement system.

Root integrated the change into the full Unity packageD3CA68C3D919. Distribution
and installed Player hashes match; normal Build.ps1 exits0. No native hire-pass
claim is made: the separate traveler readiness prerequisite remains unresolved.
