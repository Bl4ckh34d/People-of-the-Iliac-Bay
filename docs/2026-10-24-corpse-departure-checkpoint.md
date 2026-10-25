# Corpse departure and investigation checkpoint — 24 October 2026

## Current installed checkpoint — schema 64

Built and installed package SHA-256: `41CB9D035C4170938268CD97F6E47F5B178A818EA479E28754C0DB53EC309133` (dist and Player copy match). Unity build/integration passed and the portrait cache reused 20 entries / 2160 textures. Build log: `work/schema64-package-build.log`; preceding schema-61 package preserved in `work/schema61-before-inquiry-window.dfmod`.

Native healthcare and actual self-healing spell passed again on this package, including rollback, replay rejection, physical routine return, paused paid-service billing/resume, exact native currency/clock restoration and exact original fixture reload. Actual FullSerializer passes schema 64 and early corpse cleanup anchoring with fixed inquiry expiry. Preserved logs: `work/schema64-native-healthcare-serializer.log`, `work/schema64-native-player.log`. The owned test player exited. The corpse serializer fixture remains controlled/detached (`physicalGameplay=false`); this does not prove the visible scene or prison-time catch-up. Those native cases remain pending.

## Updated policy — schema 64

The original first-departure-plus-thirty-minute inquiry gate below is superseded. Physical guard cleanup immediately starts an inquiry window, including when the player never left. The deadline is the earlier of actual cleanup plus 1800 game seconds and the existing departure case cap after return reserve. Continuous absence cleanup keeps its separate thirty-minute rule. Presence observations, retries and save/reload do not restart either case deadline.

The outdoor report stores the exact cleanup anchor and whole-world restore ties it to its cleared corpse. New receipts use window version 1; historical version-0 inquiry receipts remain distinguishable and loadable. A responder arriving after the shortened deadline reverses or continues its already surveyed path toward its post from its actual travelled point. It gains no fake arrival at the corpse. Searchers also begin their return from their actual patrol point.

Verification: **1786/1786 Core**, **24/24 corpse regressions**, clean RuntimeCompile and read-only review with no blocker. Logs: `work/corpse-inquiry-schema64-core.log`, `work/corpse-inquiry-window-legacy.log`, `work/corpse-inquiry-schema64-runtime.log`. Initial timing/return failures are preserved in `work/corpse-inquiry-window-red.log`. The extended native FullSerializer fixture now passes on installed schema 64. Visible cleanup, witness/search behavior and custody skip acceptance remain open, as do shared-case recruitment and typed general NPC suspect handling. The schema-61 evidence below remains historical deployment evidence.

## Policy and implementation

Schema 61 saves a continuous absence interval for managed exterior civilian bodies. Entering an interior or leaving the scene begins absence. Returning before 30 game minutes preserves the body and resets that interval. Catch-up on loading evaluates the elapsed absence before recording the player's return. After 30 continuous minutes the body is removed without inventing a discoverer or a witness to the killing. A physically present guard retains the independent cleanup path.

A separate, nonrenewable case starts at the first departure. Repeated inn visits do not restart the investigation. The nearest available reachable guard is selected from real routes, without requiring a witness. The guard travels to the body, then patrols and can question physically present mobile or exterior static NPCs after body removal and the first 30 minutes. The case reserves physical return travel so guards resume ordinary duty by the first departure's 60-minute deadline. Existing multi-guard cases reserve the longest return budget. No available route or guard means deferred dispatch; this implementation does not spawn or teleport a replacement.

Each inquiry records the actual person, guard, time and whether an existing direct witness supplied a statement. A questioned bystander is not a witness and does not identify the player. Restore validates the guard assignment, chronology, known person and statement provenance. Historical household identities remain valid when a household generation retires.

The independent native lawful-aggressor corpse adapter retains its existing physical guard cleanup and no-homicide-investigation exception. It does not yet share the new absence timer. Interior crime-scene investigation also retains its existing rules.

## Verification

- Full Core suite: **1746/1746**, `work/schema61-core-final.log`.
- Focused departure suite: **18/18**, `work/corpse-final-focused.log`.
- The inquiry phase regression failed before the body-cleared gate, then passed. The multi-guard deadline regression failed with a guard still Returning at minute 60, then passed after reserving every assigned guard's return.
- net472 RuntimeCompile: zero warnings and errors.
- The first schema-61 installed package passed the native healthcare fixture, actual self-healing spell, failure rollback, exact resources, source replay rejection, alarm interruption, paid-service pause/resume, physical routine return and exact fixture restoration. Evidence: `work/schema61-healthcare-first.log`.
- The same run passed the actual game FullSerializer round trip for the 30-minute absence, quick return, 60-minute guard deadline and bystander inquiry. This is a controlled detached fixture, **not** visual native acceptance of corpse routes or interviews.

The final package rebuilt successfully and passed the same native healthcare and serializer acceptance again. Distribution and installed SHA-256 are both `8CB7B8B28737B4BBD3BDE6EC0479362C72A2D761158C66A40BB83F5E484B07C8`. Final run evidence is `work/schema61-healthcare-final.log`. Eleven focused suites passed separately, including boundary 8/8, calming 7/7, speech policy 65 checks, health choice 10/10, health care 17/17, meal need 12/12, role fear 5/5, role policy 11/11, Lead 18 checks, disposition commerce 11/11 and player commerce 15/15. The role migration fixture was corrected to use the actual historical inbox envelope rather than relabeling a v5 inbox as schema 42.

Do not equate the detached serializer checks with observed scene behavior. Visible guard arrival, corpse disappearance across actual inn transitions, mobile/static interview selection and the native aggressor timer remain explicit acceptance or implementation work. Review also identified a stale-assignment retry gap: an earlier dead or returned responder can prevent a new departure dispatch. This is queued for the next source slice and is not fixed in this package.
