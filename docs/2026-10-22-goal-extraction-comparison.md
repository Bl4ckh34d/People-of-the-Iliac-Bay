# Production goal extraction comparison146

The existing two-pass extractor preserves the requested chain shape better than
the existing one-pass extractor in this six-case diagnostic. Neither extractor
reliably distinguishes a new request from negation or a quoted past request.
No production prompt, model, setting default, or intent rule was changed for this
comparison. D3 was not called by this extraction-only experiment.

## Inputs and procedure

The three existing authored MoveTo/Wait chains contain three, four and five
steps. Three existing controls cover negation, a quoted historical request, and
an unresolved destination with unsupported boat reservations. Both arms receive
the same captured native141 factual context and reconstructed timestamp-only
history. Per-case attributed-history examples remain metadata and are not sent.
The parser binding is explicitly synthetic. Native145 candidates are not joined
to these inputs, and no native availability or route is inferred.

Production `NpcGoalChat.ExtractionInstructions`, `NpcGoalTwoPassChat` builders,
grammars, parsers and `LocalDialogueCompletion.Extract` construct every request.
An exact request-body roundtrip reproduces the archived native141 request before
new calls begin. Authored three/four/five-step references pass both parser paths.
Only the existing one allowed parameter repair is used when eligible; invalid
one-pass source spans do not gain a new repair. All first outputs are retained.

The retained CPU Qwen2.5-7B abliterated Q4 server completes21 requests, all HTTP200.
Every full prompt fits its measured4096-token context budget without history
windowing. The evaluator allows300 seconds per request rather than the normal
120-second default; all observed calls finish within18.31 seconds, so this
difference does not change these observed outcomes. Latencies are diagnostic,
with different prompts and cache paths, and do not establish general speed.

## Results

- One-pass omits the MoveTo steps in the three- and four-step requests. Its
  five-step output omits the first MoveTo and adds an invalid `Other` source.
  None has the intended complete ordered kinds.
- Two-pass decomposition returns the exact ordered kinds for all three chains.
  The three-step parameter pass omits the18-game-second duration. Its single
  repair adds a missing hint rather than safely filling the value, and the
  production invariant rejects it. Four- and five-step parameters preserve the
  ordered source spans and18/20-game-second durations.
- All two-pass target fields are empty. This violates the requested surface
  parameter extraction, but does not prove native binding failure: production
  binding also receives the complete original turn and each source span.
- Both arms initially interpret the negated and quoted historical controls as
  requests. The later two-pass repair rejections are not correct recognition of
  `none`. Separate willingness or native validation was not exercised here.
- Both arms omit the expected unresolved-place hints. One-pass calls the boat
  reservation `Purchase`; two-pass calls it `Interact`, rather than `Other`.
  No conclusion about recalling the fixture's metadata-only history is possible.

These are descriptive results from a small fixed corpus, not a general model
accuracy percentage. A parser-valid proposal can still omit requested actions
or misread the player's intent. Two-pass is the stronger candidate for a bounded
native follow-up, with its remaining failures visible. No new semantic regex,
keyword patch, repeated sample selection, or case-specific prompt tuning was
introduced.

## Evidence and remaining acceptance

Raw inputs, request bodies, tokenizer responses, HTTP envelopes, decoded first
outputs, parser results, reference preflight, exact roundtrip, structural summary
and independent review are retained under
`work/breeze-tts-20261021/goal-extraction-eval-146/`.
The ignored diagnostic helper references the current Core project directly.
Local preparation failures and the first helper compile error are retained in
separate logs; no model call occurred before the successful preflight.

## Native follow-up146b and installed diagnostic changes

RuntimeCompile and Unity builds146/146b pass with740 assets. Installed146b changes
the sequence test harness and validation only. It catches nested iterator
failures, cancels pending dialogue, closes Talk, disposes its owned decision
session, reloads the owned Gothway save and restores captured host flags/session
ownership before allowing PASS. The action/task check compares a documented
subset fingerprint, not complete world state. Player position is checked within
0.05m; the existing `playerPositionExact` marker label denotes this tolerance.
Settings are restored byte-for-byte by the external owned runner.

Native146 first exposes an old test bug: its reply detector mistakes the temporary
thinking row for the final response, interrupts pending decomposition and emits
no model completion trace. Recovery succeeds.146b adds the same busy-client and
conversation-stage guard already used by the willingness probe; this changes no
model prompt or gameplay decision. Both attempts and logs are preserved.

Native146b uses a real current resident, two surveyed native building entrances
and a verified40.47m alternate route. It asks for MoveTo/Wait/MoveTo/Wait, with
both waits explicitly18 game seconds. Actual Qwen decomposition returns all four
kinds correctly. Actual parameters omit both durations; the one allowed repair
also leaves them null. Production rejects the unresolved request before target
binding, willingness or task persistence. The observed baseline fingerprint,
player position tolerance, host ownership and exact settings bytes restore.

This is a native negative acceptance and a recovered model failure, not a
successful four-step trip. No target binding, consent, physical completion,
routine return, speech or full top-level free-text routing is established by
this follow-up. The probe starts at the production goal-extraction boundary.
No further sample was requested to obtain a passing model answer. The broader
complete-goal path remains experimental; normal settings/defaults are unchanged.

Native evidence: `native-two-pass-chain-4-146/` and
`native-two-pass-chain-4-146b/` beneath the same work directory. Installed146b
bundle SHA-256:
`F5B31FC6069D0D142EA46358011626276CFB15DFFE38BFE5E19DDBCE8F25F4F0`.
