# Guard defense against hostile enemies

**Goal:** Finite settlement guards physically defend the settlement from hostile creatures and attackers, independently of player crime.

**Architecture:** Reuse the saved roster, actual-position dispatch, native combat and physical return. Add an optional enemy target identity to a report; absence preserves existing crime and investigation behavior. Resolve native persistent targets after loading; a missing transient target must never turn into a player accusation. Engine scripts remain unchanged.

**Approved scope:** M4 enemy defense. This plan implements the user's existing authorization to continue implementing the complete mod. M4 remains open after the exterior pilot.

## Requirements

- Use existing finite guards; never create additional responders.
- Ignore dead, inactive, pacified or friendly enemies. Respect native quest AI attackability.
- Detect visible threats locally; use actual guard position and physical navigation.
- Save the distinction between enemy defense and player criminal response.
- Preserve player crimes. Defense alone never creates Assault or Murder.
- Enemy combat must not target the innocent player or other guards, including with native enemy infighting disabled.
- Stop after death/pacification, loss of target or bounded search expiry. Return by a surveyed route from actual engagement position.
- Existing guard health/death, native enemy damage, animation and reload ownership remain authoritative.
- Expose a defense toggle; toggling off ends defense through physical return.

## Implementation sequence

1. **Saved defense reports:** Add `GuardReport.ThreatId`, validated copy/save handling and `GuardRoster.DispatchDefense`. Write failing core cases for actual-position dispatch, repeated reports, target mismatch, ordinary-report collisions, missing routes, patrol interruption, save migration and corrupt defense reports. Reuse normal guard engagement and return rather than inventing another position owner.
2. **Native target adapter:** Identify native enemies by nonzero LoadID, otherwise use a session identity which cannot accidentally bind after reload. Survey routes from available guards that see a living hostile enemy. Scope selection to supported town/exterior and a bounded responder count. Persist the report before actor combat.
3. **Native fighting and return:** Add explicit enemy-target combat to GuardActor. Temporarily use native ally combat filtering so guard defense does not depend on EnemyInfighting; restore original team and callbacks on exit. Capture enemy observations without reading player location as suspect. Resolve saved target and stand down physically when invalid. Player assault and existing bounties must still reach native criminal response.
4. **Live acceptance:** Spawn a controlled hostile enemy near a finite guard. Observe physical response, enemy target and actual native enemy damage; innocent player crime stays None. Check pacified/blocked candidates, no extra guards, wounded guard and real reload, target death, physical return and prior crime preservation. Run existing patrol, indoor surrender and housing regressions after native integration.

Core foundation is not proof of playable defense. Mark each stage only after its own evidence passes; preserve broader interior/civilian/event requirements in M4/M5.

## Verified exterior checkpoint

Stages1–3 and the controlled exterior portion of stage4 pass in0.3.25. Core222/222, native WorldState serialization and GuardDefenseProbe verify finite actual-position response, enemy damage with EnemyInfighting off, pacified/inactive/wall-obscured rejection, native enemy and wounded guard real reload, target death/physical return, prior Theft retention and player-assault takeover. SAVE46 is the living defense encounter. Native MakeEnemyHostileToAttacker can retain an adjacent visible enemy when the player attacks; the expanded red fixture reproduced ignored assault, and takeover now uses the player-hostility flag independently of retained target. Evidence: work/GuardDefense-assault-red.log and work/GuardDefense-0325-pass.log. The assault probe calls the native reaction method; it does not simulate an actual player weapon swing. Interior defense, missing-target live reload acceptance, wider layouts, events and large-town profiling remain open.
