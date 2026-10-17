# Persistent witness fear until crime resolution

Requirement: witnesses of the player's killing of an innocent resident flee and refuse dialogue until that crime resolves. Witnessed partner/child killing retains the separate lasting grudge. Do not attribute hostile or NPC-caused killings to the player.

Verified gap: generated resident alarms expire with their temporary incident/report window, allowing ordinary witnesses to talk again while Murder remains outstanding. Adult reaction selection can also choose Fight. Existing dialogue gates use the active alarm or family grudge. Generic `RecordViolence` has neither explicit offender attribution nor a resolution marker.

Implementation plan:

1. Add explicit saved player attribution from player-hit paths only (`StreetPopulation.BeforeWeaponHit`, keeper hit handling). Leave neutral `RecordViolence` callers neutral; `ThreatId == null` is not evidence of player guilt.
2. Persist an accusation-issued marker per incident when that incident dispatches the Murder charge. Keep direct witness fear before reporting and past temporary alarm expiry.
3. Clear this response only after accusation was issued and native crime state clears. `CrimeCommitted == None` before a delayed witness reaches a guard must never clear fear. Pursuit/search expiry is not crime resolution. Review fine, prison, save/load and native-charge paths.
4. Gate player dialogue and ambient chatting consistently. Ordinary direct witnesses flee; lasting family hostility remains separate. Preserve bounded warning spread.
5. Test neutral/hostile deaths, delayed reporting with no current charge, report and persistent Murder, reload in each phase, and post-resolution return to normal. Add a native test including actual reporting and legal resolution where feasible.

Implementation checkpoint: schema 19 now saves explicit player attribution, accusation and resolution state. Direct and warned ordinary witnesses retain fear beyond the temporary alarm window. A missing native crime before reporting does not clear fear; clearing an issued accusation does. Native keeper responses clear immediately after resolution. Core regression suite passes 518/518. Unity build and native isolated-store adapter/save-load checks pass (`work/WitnessPersistence-performance-pass.log`).

Local DFU court code and the existing guard integration remain authoritative for event wiring. No universal fine/prison completion callback is used: runtime checks the native crime state only after game readiness and only resolves incidents whose accusation was issued. The planned native probe checks adapter state and FullSerializer roundtrips on an isolated store; it does not enact a courtroom sentence or prove every regional crime transition.
