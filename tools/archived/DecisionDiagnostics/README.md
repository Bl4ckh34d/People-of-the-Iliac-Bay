# Historical second-model diagnostics

These files preserve the former D3 wire readers and pinned native pilot runner as historical diagnostic evidence. They are outside `mod/Scripts` and are not included in RuntimeCompile or the mod build. There is no second-model runtime client, session, launcher or configuration.

`LocalDecisionWire.cs` and `LocalSpeechActWire.cs` remain linked only by the historical `NpcFactorEvaluation` and `SpeechActEvaluation` projects so old recorded payloads and responses remain inspectable. They are pure serialization/parsing helpers and do not launch a service. Their historical evaluator commands and fixtures do not describe the current game pipeline.

`RunNativePilotAudio.ps1` is archived, requires obsolete pinned D3/Qwen processes, and must not be used for current single-model acceptance. Use the current native speech probes and the normal Gemma/Breeze launcher instead. Model study reports remain historical evidence; no new native acceptance is claimed by this archive.
