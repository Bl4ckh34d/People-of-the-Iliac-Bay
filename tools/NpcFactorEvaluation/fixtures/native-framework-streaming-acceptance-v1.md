# Combined native framework and streaming acceptance

`native-framework-streaming-acceptance-v1.json` defines a single gate for the existing native conversation/mood framework and the social streaming transport. The offline validator checks evidence only; it never launches the game or a model.

The evidence JSON must identify the exact mod assembly SHA-256 and the owned `PeopleOfTheIliacBayTest-9a5f197b` / `People - Gothway Garden` fixture. It must include the live social-completion, controlled-callback, disk-rollback, and baseline-restored framework markers. Streaming evidence must show at least two complete sentence callbacks, a monotonic first-sentence timestamp earlier than the final validated-result timestamp, and successful structured completion.

The cancellation leg must cancel from its first sentence callback, have no terminal callback, no completion-count increment, no mood-commit callback, and an identical native mood value, accepted-turn sequence, and state revision before and after. The stream test is transport evidence; it does not establish that queued or generated PCM was audible. Unity playback receipts remain the authority for audibility.

The checker deliberately requires one combined run ID and the hash of the assembly supplied by the caller, so evidence from a stale build cannot be combined with a newer artifact. Current TownSmoke evidence-writing remains a parent-owned integration step; do not manufacture timestamps from log ordering.

Run offline validator tests:

```powershell
python tools/NpcFactorEvaluation/test_native_combined_acceptance.py
```

Validate a later native evidence file against the exact built assembly:

```powershell
python tools/NpcFactorEvaluation/native_combined_acceptance.py --evidence work/npc-factor-evaluation/run/native-combined-acceptance.json --mod-assembly path\to\PeopleOfTheIliacBayMod.dll
```
