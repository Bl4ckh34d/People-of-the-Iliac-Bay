# Guard ranged defense and messenger recruitment

## Implemented source behavior

Guard archery is assigned once from the persistent appearance seed (one in five seed residues). The role and a finite initial stock of 24 arrows persist in GuardRecord and validated copies. This is approximately 20% of a real garrison, not a forced exact quota. Loading or recreating an actor does not refill spent ammunition. Archers receive a real bow and arrow inventory and use native projectile/hit resolution. City-watch art has no ranged frames; this implementation does not add a bow-drawing animation.

NativeDefenseHooks now observes EnemyAttack.ApplyDamageToNonPlayer as well as DamageHealthFromSource. Native NPC arrows use the former and directly decrease health. The runtime reacts only after a successful health decrease, retaining attacker attribution and leaving player crime handling separate. A visible attacker can activate a posted guard without a ground route to its rooftop. Unknown or obscured attackers do not acquire a fabricated known position.

A melee defender facing an elevated target can physically route to an available archer. During that trip, visible available guards within 20 metres receive the report and move to the observed attack origin. They do not recursively broadcast without contact. The archer then routes back to that origin and engages on sight; the messenger returns to its post. This is coordinated response, not formation-following. Existing combat/interior/refuge assignments are preserved. The support destination and route survive save/load, while target reacquisition still requires a valid scene enemy.

## Verification and remaining acceptance

Core suite: 1712/1712 passed. RuntimeCompile and QuestHooks builds: zero warnings/errors. Focused coverage includes stable 20% assignment, no ammunition refill, defense redirection endpoint validation and serialized support/ammunition state.

No new game package was installed. Daggerfall Unity was running during implementation. Native acceptance remains required before claiming the reported scenario fixed in-game:

1. Hit a posted melee guard with a real rooftop enemy arrow; verify attributed response without player crime.
2. Verify the messenger physically reaches an archer and recruits visible available guards en route, without recruiting hidden or busy guards.
3. Verify a finite native arrow is fired, hits the hostile NPC and consumes exactly one saved arrow. Check blocked and friendly-obstructed lines, paralysis, low/high range and return to melee.
4. Save/load during a support trip and after ammunition expenditure; verify no duplication, refill or stale-target tracking.
5. Verify unavailable archers, moving/dead helpers and a killed/disappeared attacker recover to ordinary duty without a stuck assignment.

Not implemented here: searching for a shooter whose position is unknown, specialized ranged animation, formation escorting, or a general elevated navigation solver. These are explicit coverage limits.
