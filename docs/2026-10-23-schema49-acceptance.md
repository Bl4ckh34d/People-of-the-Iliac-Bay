# Schema 49 acceptance checkpoint

Status: package built, locally installed and accepted by the native healthcare/FullSerializer probe (exit0). This is a scoped checkpoint, not completion of the roadmap or inbox-retention work.

## Changes

- Public generic native-event admission rejects caller-constructed outcomes. Health episodes, completed transfers and confirmed appointments use the internal source-owned path.
- Appointments persist None, Pending, Projected or LegacyUnavailable. Failed projection retries without repeating social effects; confirmed projected outcomes do not replay after detail retirement. Legacy outcomes without exact evidence do not invent a new event.
- NPC health progress persists its last projected Vitals revision. Episode clearing and cursor advancement follow successful projection or exact historical reconciliation. Conflicting evidence remains pending.
- Historical test/evaluation fixtures use explicit validated save imports. Live probe fixtures reload before reacquiring native actor bindings.

Inbox detail retention is not enabled. Poison and speech source protection, historical query availability and bounded receipt projections remain outstanding. This is not autobiographical compaction.

## Verified evidence

- Main Core: 1694/1694, `work/life-core-20261023/temp/schema49-parent-core-reviewed.log`. Two old test fixtures were corrected to distinguish historical schema48 data from current projection markers; production validation was not relaxed.
- Appointment projection marker: 6/6, including schemas26/34/48, `temp/appointment-marker-old-schema.log`.
- Appointment retry: 5/5, `temp/appointment-retry-schema49.log`.
- Health projection cursor: 6/6 reported by its focused test owner; parent source review and native serializer confirmation completed.
- RuntimeCompile: zero warnings/errors, `temp/schema49-parent-runtime.log`.
- Unity build/integration/install: exit0, `work/life-core-20261023/schema49-package-build.log`. Portrait cache reused20 entries and2160 textures.
- Dist and installed SHA256 both `FBB35EAE1B06E9ECF3063ACDBCB29C253F169910A93229877C8D66CAAFAB1D37`.
- All664 top-level runtime/Core source files match the deployed Unity `Scripts` and `Core` directories by hash. No game or Unity process remained after the owned test.
- All13 baseline fixture file hashes matched both before and after the native test.

## Native acceptance

Owned fixture: `PeopleOfTheIliacBayTest-9a5f197b` / `People - Gothway Garden`.

Command: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/TownTest.ps1 -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b -FixtureSaveName 'People - Gothway Garden' -NpcHealthCareProbe`.

Harness log: `work/life-core-20261023/schema49-healthcare-native.log`. Required new marker: `PEOPLE_OF_THE_ILIAC_BAY_NATIVE_EXPERIENCE_PROJECTION_MARKERS_PASS`, in addition to schema49 framework persistence, actual full recovery, routine return, no extra consumption and exact baseline restoration.

The harness exited0 and all required markers are present in the archived `work/life-core-20261023/schema49-healthcare-player.log`. The actual resident healed from15 to76 HP using five owned native potions, returned to ordinary routine and consumed no additional potion. The marker probe started from the actual native recovery snapshot, projected its exact health interval in a detached store and preserved its cursor through two FullSerializer disk roundtrips with exact detail removal between them. The appointment source was a separately controlled Core reservation/resolution, explicitly not a native meeting. Both sources remained unreplayed after detail removal. The original named baseline was reloaded successfully.

Other migrated conversation/personality/memory probe paths have compiled and received source review, but were not rerun natively in this checkpoint. No new model-quality or voice claim is made.
