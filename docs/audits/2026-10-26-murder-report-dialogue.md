# Anonymous murder report through native free-text dialogue

## Reproduction and repairs

The earlier native report fixture injected a controlled accepted action, while
the local model script tested interpretation separately. Neither established
that the ordinary conversation handler could accept and deliver the request.

`-ValidateMurderReportChat`, together with `-ValidateMurderReport`, now opens the
actual adult resident's native Talk window. The owned fixture sets a friendly
disposition baseline of 95, submits a free-text request, and requires exactly one
accepted `ReportMurder` action and no multi-step task. It does not inject consent
or a classification. After the reply appears, Talk remains open for a further
25 real seconds; the actor must remain still and accepted. Closing Talk must
produce actual movement, arrival, both saved memories and no change to the
player's crime. Returning model greetings are suppressed; the native opening
greeting and ordinary speech presentation remain enabled.

The request is: “I saw a murder by someone I don't know. Please find the nearest
guard and report it anonymously.”

1. `murder-report-chat-01/02` classify this as two requests, both `ReportMurder`.
   The exact captured completion replays as
   `CHAIN|find the nearest guard|report it anonymously`. Decomposition now
   explicitly counts the report outcome, with examples showing that finding
   its listener is part of delivering it. A separate subsequent wait remains a
   distinct action. No keyword classifier or automatic task deduplication was
   introduced.
2. The old standalone script substituted a shorter system instruction and
   omitted the production framing's trailing newlines. It misleadingly passed
   the same wording. The script now translates the actual Core completion
   framing to the server-native template and uses the native decomposition
   token limit. With that correction, the old prompt fails the captured case;
   the repaired prompt passes eight intent cases plus two separate-effect
   controls. These are regressions, not held-out model accuracy estimates.
3. The first dialogue fixtures had no speech coordinator watching their
   isolated data directory. The canonical reply remained pending for audio;
   this was not a second action-reply ownership failure. Starting the matching
   owned Breeze services in `murder-report-chat-03` permits the normal reply.
4. That run then fails with “The walking route remained blocked.” The action
   motion's twenty-second real-time progress timer starts at acceptance, before
   the conversation ends. DFU's `IsPlayingGame()` is false while Talk is open,
   so resetting only inside the per-actor pause branch is insufficient;
   `murder-report-chat-04` reproduces this after an explicit 25-second hold.
   Street updates now mark existing action motions paused before their global
   early return. Resuming refreshes progress observations while retaining the
   actual route and waypoint. Genuine walking stalls still use the existing
   twenty-second limit; game-time deadlines and interrupted rest semantics are
   unchanged.

## Running the fixture

Start the selected local dialogue model using `StartLocalDialogue.ps1`, with an
ownership receipt. Then run:

```powershell
./tools/ProfileInEditor.ps1 -RunName murder-report-chat-check -CountersOnly -ValidateMurderReport -ValidateMurderReportChat
./tools/StartLocalSpeech.ps1 -PersistentDataPath "$PWD/work/playtest-2026-10-26/editor/murder-report-chat-check/data" -OwnershipReceiptPath "$PWD/work/murder-report-chat-speech.json" -RequireServerReady
```

Start speech promptly during editor loading. It must watch this run's isolated
data path, not the ordinary Player directory. Both tools retain their existing
process/configuration ownership checks. Stop only services owned by the matching
receipts after the editor finishes. The script rejects the chat flag without the
native report flag before starting Unity.

Exact model requests and replies are captured only for this opt-in fixture in
`work/semantic-action-model-capture.jsonl` beneath its run directory. Ordinary
gameplay does not capture prompts.

## Boundaries

This uses a friendly controlled adult and an exterior finite guard roster.
Refusal remains possible in ordinary play. Indoor reporting, named accusations,
real investigations, hostile-speaker willingness and arbitrary paraphrases are
not established by this test. The guard receives anonymous hearsay, not player
culpability or an invented crime scene.

## Verified result

`murder-report-chat-05` completes the full path with the configured local
Gemma 4 E4B Q4_K_M model (16,384-token context) and the matching isolated Breeze
speech services. Captured stages are `SINGLE`, the report category, then
`DIRECT|ReportMurder|1|1|300`. The exact player request and canonical acceptance
appear in the native transcript. The saved action stays accepted and stationary
through the additional 25-second hold. After Talk closes, the body travels
18.63097 m and delivers the report; both memories exist and player crime remains
unchanged. No action/task admission or semantic model output is injected.

The raw route classifier labels the wording `insult`; the existing separate
speech-act guard correctly turns the applied tone into `neutral` before action
admission. The raw diagnostic must not be mistaken for an applied social penalty.

The owned editor exits with `complete`, without an `Exception:` entry. The
existing `Some objects were not cleaned up when closing the scene` teardown
warning remains and is not counted as fixed. Core 2121/2121, RuntimeCompile,
70 PowerShell syntax checks, tooling protocols and source policy pass. The
three locally started model/speech processes are stopped using their ownership
receipts after the test; Unity Hub is untouched.

Build/install completed at 08:23. Distribution and Player SHA256 both equal
`51AFECAA1991B751A02E4E996A9BBEE2FB5B8119A16D61D1DA5B90838418B26B`.
Unity integration/build exits 0 with the serializer, adapter lifecycle, contract
and bundle verification marker. The owned build editor exits normally.
