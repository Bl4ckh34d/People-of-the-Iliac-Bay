# Running cycle rejection and correction attempts

The user correctly reported that the same arm stays forward in some directions.
The previous static-sheet inspection was insufficient. Different leg positions
do not prove opposite arm lead or a coherent running cycle.

## Baseline review

The atlas produced from `generated-run-v2.png` is not accepted. Front-left,
side and rear-left repeatedly hold the same foreground arm forward. Front and
rear contain some left/right differences, but they have not been accepted as a
coherent four-phase sequence (contact A, flight A, opposite contact B, flight B).

## Attempts and observed results

1. `generated-run-v3-rejected.png`: explicit all-row half-cycle reversal prompt.
   Front-left, side and rear-left still repeat their dominant arm positions.
2. `contact-b-generated.png`: one frozen opposite-contact pose in five views.
   Some side arm changes appear, but this alone is not an animation sequence.
3. `contact-a-rejected.png`: requested opposite phase of the frozen pose.
   Front changes; several other views retain the same arm arrangement.
4. `cycle-front-left-v4.png`: isolated four-frame strip. The chest-crossing near
   sleeve in frames 0/1 changes to a backward near arm in frames 2/3. This is a
   local improvement, not a validated full cycle or five-view replacement.
5. `cycle-side-v4-rejected.png`: isolated side cycle. Frame 2 moves the near arm
   backward, but frame 3 reverts to the foreground sleeve crossing the chest.
6. `cycle-side-v5-rejected.png`: single-frame correction explicitly requested
   frame 3's upper-body pose to match frame 2. The incorrect foreground sleeve
   remains. Not accepted.
7. `cycle-rear-left-v4-rejected.png`: isolated rear-left cycle. The same hand
   remains behind the same side of the torso across all four phases. Rejected.

All pose changes above were made through the built-in Imagegen tool. No manual
pixel pose creation was substituted. No corrected runtime atlas was published.

## Current status

The original runtime PNG and `.png.bytes` remain unchanged, SHA-256
`E46CA97117D3C32D2466C3FD3E86193E197DC7997110AB703019B8120419DF18`.
The user authorized trying this current draft in game on 16 October despite the
same-arm issue and missing side leg-passing poses. This does not accept the art
or claim the issue fixed. Further authoring requires a controlled pose-reference
approach or explicit manual animation work, followed by frame-by-frame and
animated review of every view.

## Bounded runtime preview

Runtime integration is limited to living adult male Breton outfit variant 0
(native archive 385) during active flee movement. Conversation, sleep, death,
idle, standing and ordinary walking restore native rendering. This is a
temporary appearance-scoped preview only; pilot remains rejected.

`tools/TownTest.ps1 -FixtureCharacter <fixture> -RunningPilotProbe` launches an
actual-player fixture without mouse capture. It rotates one disposable actor
through all eight native orientations, samples all four atlas frames, checks
native-renderer restoration, and captures one screenshot per orientation. Run
result and screenshots still need collection before reporting runtime behavior.
The probe does not validate production flee routing or approve art.
