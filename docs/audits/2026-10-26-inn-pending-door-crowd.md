# Inn guests remained outside despite reaching the doorway

## Superseding exact-bed status

Current lodging no longer uses any room or floor grouping described in the
historical diagnosis below. Each physical bed is one exact place. Fresh native
audits pass all5 Gothway and all19 Daggerfall City Inns. Inn263174 passes ordinary
admission, actual doorway completion and five-of-five interior traveler bodies.

A later observation found three brief bodies at the Inn entrance after catch-up.
Root cause: pending visits could create new exterior actors at the route endpoint
while asynchronous bed inventory preparation had not yet committed admission.
Exterior materialization now distinguishes continuous play from catch-up. After
loading, loitering or another time jump, no guest keeps or receives an exterior
body once the scheduled arrival has passed. Once admitted, the guest materializes
only when the Inn interior loads. A continuously visible arrival without catch-up
still completes its physical doorway walk.
The pure policy passes the complete2147-test current-schema Core suite and RuntimeCompile with
zero warnings/errors. The updated native assertion rejects a pending catch-up body
and still requires a fresh package run.

## Reproduction

The expanded ordinary-body observer includes residents, destination visitors and
travelers. The earlier local-plan-only observer omitted travelers and therefore
could not establish absence of the reported crowds.

`work/door-crowd-user-04-player.log` reproduces seven travelers at Inn263174 in
`Pending:AtInn`, including bodies within0.28–0.32m of the actual door. Nearby
Inns263177 and263429 also have pending visitors. Navigation can reach the door;
increasing the arrival radius does not repair the pending booking.

The existing native Inn263174 survey fails with
`Rental marker belongs to multiple bed groups: 1`. Separate surveyed standing
regions around beds can share the same native rental marker. The old code treats
that as an impossible layout and never publishes its accommodation.

A second defect blocks admission updates for already prepared Inns while another
unloaded building is surveyed. Demand still creates travelers during that interval,
so they wait at the door even after accommodation has been prepared.

## Change

`LodgingFloorGroup.JoinSharedMarkers` joins marker-connected floor groups before
assigning room occupants and publishing reservations. Native closed-door and floor
height checks still constrain the source groups. All real beds remain in the room;
one reservation protects the complete room. Separate marker groups remain separate.

`NativeTavernRentals.RefreshExterior` continues bounded admission reconciliation
while remote survey work runs. It also prioritizes unprepared accommodation near
the player. The final proximity calculation uses actual native doors, rather than
building positions relative to individual RMB blocks.

## Verification

- Two focused regressions fail before joining is implemented. They verify shared
  and transitive marker identity, all beds retained, distinct rooms preserved and
  a player reservation excluding a second room booking. Full Core:2178/2178.
- Runtime compilation succeeds with zero warnings/errors.
- Native explicit partition check for actual Inn263174 returns6 rooms,11 beds and
  all7 native rental markers, joining7 floor groups into6 rooms. Its initial
  admission attempt has no demand yet and does not prove entry.
- `work/inn-admission-native-02-player.log` exercises ordinary demand and normal
  rental preparation without guest, room, booking or admission injection. Pending
  due admissions fall from9 to1 to0. Five travelers are admitted, no admitted guest
  remains blocked outdoors, and all5 materialize as actual active traveler bodies
  in the real Inn interior. `TownTest.ps1` reports the targeted city probe passes.

Exact native marker:

```
PEOPLE_OF_THE_ILIAC_BAY_ORDINARY_INN_ENTRY_PASS: building=263174; admitted=5; activeInteriorTravelers=5; duePending=0; blockedAdmittedOutside=0; noGuestOrBookingInjection=true
```

## Limits

The ordinary run verifies this Inn and admission-to-interior visibility. It does
not prove every Inn, an entire overnight stay or that every admitted body was
continuously observed walking outdoors. The final native-door proximity ordering
is compiled after this pass and awaits the next shared build. Other shop crowds
and the broader routine distribution retain their separate checks.
