# Exterior corpse reporting and witnesses — source checkpoint, 24 October 2026

## Requested behavior

A civilian who discovers a corpse seeks the nearest reachable available exterior guard within 300 m, without requiring initial sight. If none is found, the civilian continues the routine and retains the location for the next visible available guard. Reporting requires physical contact, and the approach uses the civilian's running speed. A murder witness instead seeks accessible shelter and locks it for one hour, ending earlier only when the culprit has been dealt with and the corpse has been removed. Static human witnesses should inform nearby guards through a visible approach. A guard's actual Halt shout recruits available guards within 100 m.

## Implemented source

- [Pursuit shelter replanning](2026-10-24-pursuit-shelter-replanning.md) now allows an NPC to divert toward a nearby accessible door roughly in the escape direction when a known pursuer rapidly approaches, including from behind. Timed witness routes distinguish refuge arrival from guard reporting. Source verification: thirteen new cases, Core1739/1739, clean RuntimeCompile/QuestHooks; native acceptance remains open.

- `OutdoorBodyReport` is saved per observer; discovery alone does not imply seeing a killer. Deferred reports and current exterior positions survive snapshots. The runtime walks real routes to a guard and then physically rejoins the routine. Guard selection is throttled globally and per reporter. A higher-priority active alarm interrupts the reporting motion.
- Player departure no longer clears discovered outdoor bodies. `ClearOutdoorCorpse` requires a living exterior guard within 3 m using measured coordinates; the runtime also checks actual scene distance and sight. Existing old cleared saves remain cleared.
- Reached refuge has a saved one-hour deadline. Shelter locks retain incident ownership and can unlock early only after resolution and cleanup. Unlocked ordinary houses now participate in shelter selection alongside existing accessible buildings; owned player houses and crime scenes remain excluded.
- Player murder resolution no longer follows arbitrary `CrimeCommitted=None`. The prison-completion event supplies positive evidence. A guarded enemy murder case can resolve when its known target is observed dead; unloading a target does not count as death.
- A native NPC-on-resident damage hook records confirmed exterior deaths and separates `KillerThreatId` from player attribution. Witnesses must see both victim and visible attacker. The attacker identity is passed to investigators through witness contact, never inferred from a body.
- Exterior human StaticNPCs use persistent identities with `WorkKey=-1`. A guard approaches a saved direct witness before the statement is delivered; the current physical NPC must still be present. Native interior keeper witness/escape handling remains separate.
- Only one static interview may be in progress per incident. Any delivered direct-witness statement suppresses further static interviews. A dead, absent or no-longer-assigned witness/guard allows replacement; successful assignment stops the selection loop immediately. RuntimeCompile verifies this source change; native multi-witness acceptance remains open.
- Native aggressor loot corpses now join the same saved civilian reporting motion. `DefenseCorpseCleanupRuntime` discovers exact native corpse UIDs, dispatches a typed cleanup-only guard response, removes the real native container at physical arrival, then returns the guard to duty. It never supplies a suspect or investigation to the generic pursuit path. Cleared UIDs remain cleared on reload. Active quest-reward corpses and a currently open corpse inventory delay removal.
- `StaticWitnessInterview` now enforces the per-case reservation and physical-contact delivery in Core as well as in the runtime selector. A pending reservation survives reload; a dead guard permits replacement, and a delivered statement prevents all later interviews for the case.
- Halt dispatch uses the real shout position and a 100 m hearing radius. Responders travel to the reported sighting; they do not gain visual perception through obstacles. Civilian Halt reactions retain their existing radius.

## Verification

Initial checkpoint: Core suite 1717/1717 passed. The follow-up integration adds six passing targeted tests in `CorpseCleanupIntegrationTests`: saved aggressor reports; no-suspect cleanup dispatch and actual arrival; passive/provoked exclusions; failed native-removal rollback; forged-state rejection; and one witness per case with replacement/reload. The cleanup state and guard response also pass a full-world round trip through DFU's FullSerializer. RuntimeCompile and QuestHooks compile successfully with zero warnings/errors on the final source checkpoint.

The latest broad Core run was 1723/1724, with the separately edited dialogue case `New direct hostility labels retain conservative appraisal bounds` failing. All six new cleanup/interview cases passed in that run and a separate targeted execution. No dialogue source was changed here to hide that unrelated failure.

NuGet vulnerability lookup became unavailable during verification. A CLI-only restore with `-p:NuGetAudit=false --ignore-failed-sources` allowed the test build to run; no project audit setting was changed. RuntimeCompile reports NU1900 as a warning, not a compiler error.

## Native acceptance still required

### Clarified attacker-corpse rule

The user explicitly requires cleanup without a homicide investigation for the corpse of an NPC whose unprovoked aggression against the player or another NPC is established. This includes assassins, rogues and other attackers, but class or appearance alone is not evidence. A provoked defender or an unidentified dead person must not automatically receive this exemption.

Discovery, the physical civilian report, urgent guard dispatch and actual corpse removal still occur. Only the killer-search/accusation stage is omitted for the established aggressor's death. This does not cancel investigations into that aggressor's own victims. Combat provenance and witness knowledge must remain distinct; encountering the corpse alone must not manufacture knowledge of who started the fight.

The existing DefenseCombatant ledger provides origin, unprovoked-attack and player-provocation evidence, and DefenseDeath stores validated lawful-defense classification. The native enemy-loot-corpse adapter now consumes that evidence. The native damage hook also records successful attacks on residents, guards and passive NPCs, without erasing recorded player provocation. A failed or non-damaging hit does not establish aggression. This supersedes the earlier documentation-only checkpoint for the rule.

No new game package was built or installed in this side conversation. These are source and CPU-test results, not an in-game pass.

- Run a finder to an out-of-sight guard at 299 m; verify no remote delivery and no teleport on returning to the routine.
- With no available reachable guard inside 300 m, verify routine continuation, later visible-guard contact, save/reload, crowded paths and scene transitions. Normal scheduled movement retains its established timing; the dedicated reporting/return-to-schedule approach runs urgently. Fully unloaded reporter travel still needs acceptance.
- Murder witness: shelter arrival, actual exterior door locking, exactly one game hour, no unlock from clearing the player crime flag, and early unlock only after both resolution and body removal.
- Static witness: visible approach, disappearance/death before interview, no invented player accusation, then actual target acquisition and audible Halt.
- NPC-on-resident murder: arrows and melee, witness attribution, body cleanup, culprit search, target death and reload.
- Native nonresident aggressor loot corpses are now implemented in the exterior adapter; actual in-game cleanup still needs acceptance. Unbound mod-provided corpse props, native interior static witness interviewing and fine-only/acquittal early-release paths are not expanded here. Active quest-reward corpses are deferred until the quest permits removal.
