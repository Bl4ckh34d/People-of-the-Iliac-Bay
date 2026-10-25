# Qwen willingness callsite follow-up147

Source review finds three paths that still set `separateWillingness` from D3
availability alone: single actions, pending-offer acceptance and ordered semantic
tasks. With D3 off and explicit Qwen willingness enabled, the interpreter can
therefore use combined consent and the resolver receives `required:false`,
bypassing the intended separate Qwen stage. Complete-goal extraction already
requires a separate stage and is not affected by this callsite omission.

The bounded source correction uses
`DecisionWillingnessEnabled || QwenWillingnessEnabled` at all three callsites.
Typed cancellation remains before consent dispatch; pending offers retain their
existing priority. Configured D3 outage still cannot select Qwen implicitly,
because `QwenWillingnessEnabled` requires the planner setting to be off. No
prompt wording, model, default, parser or semantic intent rule changes.

Native147 independently verifies the existing authoritative-reply boundary on
installed146b with free text, voice and prewarm disabled. Real saved task/action
cancellation produces a Cancelled receipt; submitting an older Accepted receipt
becomes Unknown exactly once, with no model reply dispatch or duplicate effect.
The owned save is reloaded, player position/greeting state restore, and external
settings restore byte-for-byte. This is direct native stop/receipt validation,
not a model's interpretation of player `CANCEL`, pending-offer priority or proof
of the three changed classifier callback paths.

Evidence: `work/breeze-tts-20261021/native-reply-outcome-147/`. The three-callsite
correction is compiled and deployed in checkpoint148c (742 assets, clean runtime
and Unity builds). Independent source review confirms D3-outage blocking and
cancellation/offer priority are unchanged. Positive and negative native classifier-to-
willingness acceptance for those callsites remain a coverage gap; existing
controlled willingness tests prove the backend boundary, not those entry routes.
