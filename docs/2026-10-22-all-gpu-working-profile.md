# All-GPU working profile, 22 October 2026

Checkpoint153b subsequently passes three real full-WAV social replies through D3, Qwen and the production Breeze Unity mixer, including exact-text receipts, nonzero output, completed/drained playback, appraisal/reopen and baseline/settings restoration. Installed745-asset SHA256: `4219F2F1CBB0BFAE734803C6218CAD23F15378743C7A21C0766F2B38FD1488AD`. The152 voice-off evidence below remains historical. See [the native speech report](2026-10-22-all-gpu-native-speech-acceptance.md) for exact proof and remaining limits.

The user selected Qwen2.5-3B-Instruct Q4_K_M, D3-Nano Q8_0 and Breeze-TTS-2 Q4_0
as the working configuration. This supersedes experiment150's decision to restore
the previous defaults. It does not establish that the smaller dialogue model is
semantically equivalent to the previous7B model.

## Persistent selection

`../local-llm/active-model.json`:

```json
{"modelPath":"models/qwen2.5-3b/qwen2.5-3b-instruct-q4_k_m.gguf","executionDevice":"GPU"}
```

`../local-llm/active-decision-model.json`:

```json
{"profile":"nano"}
```

The normal `../tools/StartGame.ps1` launcher already reads these selections through
the dialogue and decision launchers. The saved NPC-voice setting remains enabled.
The production speech launcher uses the pinned Breeze Q4 model, CUDA backend,
`lazy_load=false`, and no idle unload. Dialogue and D3 retain4096-token contexts.
There is no CPU layer fallback or model swapping in this profile.

The default decision-launch path initially failed because it explicitly forwarded
an empty optional profile through the resolver's `ValidateSet`. The launcher now
omits the profile argument when none was supplied, allowing the saved selection
to resolve normally. Explicit Mini/Nano overrides remain supported. Native test
runner model-identity checks now use the selected registry model rather than a
hardcoded Mini path.

## Evidence and limits

Selection backups, previous server identities and startup receipts are in
`work/breeze-tts-20261021/working-profile-152/`. Qwen8080, Nano8093 and Breeze8094
started successfully. The production speech coordinator also reported ready;
optional CPU word alignment and viseme estimation remain enabled.

Installed checkpoint152 contains744 assets. Core1592/1592 and adapter84/84 pass.
The owned `native-working-profile-152b` run passes three real Nano SOCIAL routes,
three first Qwen3B replies, normal receipt appraisal and same-resident reopen,
then restores the baseline and exact settings bytes. Breeze remains GPU-resident,
but this existing probe deliberately disables game voice; it does not synthesize
or play those replies. Six late native-game memory samples retain at least2445MiB
free; these are not an inference peak measurement. A full speech acceptance plan
is retained in `working-profile-152/native-full-speech-plan.md`.

The first152 run exposed a test fixture defect: `DecisionRoutingProbe` still
expected a hardcoded Mini model despite the launch arguments selecting Nano. Its
raw first responses remain in `native-working-profile-152/`; the helper now reads
the explicitly selected model ID. This change does not relax model identity checks.

The technical pass is not a conversational-quality pass. The successful run's
first outputs include a weather-focused insult reply and an apology reply that
promises to wait despite no accepted wait action. Preserve those failures for
broader grounding/receipt evaluation; do not hide them with rerolls or word rules.

The earlier loaded-game concurrent experiment150 left at least1118MiB free across
43 inference samples. See [the comparison report](2026-10-22-all-gpu-model-comparison.md)
for exact conditions, timing and quality limitations. That experiment generated
audio but did not prove native dialogue-to-mixer integration. Do not report it as
an end-to-end spoken conversation acceptance test.

## Rollback

Keep all previous weights. Restore the saved `active-model-before.json` bytes from
the152 evidence directory to `../local-llm/active-model.json`. The previous dialogue
selection was Qwen2.5-7B-Instruct-abliterated-v2 Q4_K_M on CPU. The previous decision
selection file was absent, which selects Mini. Stop only the verified owned model
servers while DFU is closed, then restart the corresponding launchers. Do not
replace a server owned by a running game.

The broader roadmap remains active. Future quality work should use this selected
working profile, retain first outputs and separate model mistakes from harness
failures instead of adding semantic keyword rules.
