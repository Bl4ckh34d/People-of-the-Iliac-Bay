# First live decision-routing slice

Continue the approved hybrid architecture in the existing integrated checkout. The previous goal turn made verified progress: the naming refactor is installed and its converted-save native acceptance passes. No new worktree is created because that would omit the integrated uncommitted implementation.

## Design

D3-mini's existing four-way speech-act menu becomes an awaited first stage in `SubmitFreeText`, replacing parallel shadow observation when gameplay mode is selected. Qwen refines the selected route using the existing classifier, exact targets, task interpreter, consent and response path. D3's SOCIAL route cannot become a new physical action during Qwen refinement; ACTION_REQUEST and COMMITMENT_UPDATE admit action/home/appointment interpretation or clarification; OFFER_RESPONSE uses the existing current-offer interpreter. Direct threat or immediate social departure may still override a route through the existing social contract.

This first slice does not transfer final willingness or every later planning decision to D3. It connects real gameplay to the decision service without duplicating the conversation/session stack. Future slices reuse this boundary for bounded plan/willingness choices.

Technical D3 failure falls back once to the existing Qwen router. Invalidated sessions, save/scene changes and closed windows never continue a request. The same main-thread stage tokens and native validators remain authoritative. There is no semantic confidence/perfection threshold.

## Tasks

- [x] Test and implement route refinement grammar and typed route validation.
- [x] Test and implement mutually exclusive shadow/gameplay configuration and D3 startup/reuse.
- [x] Await D3 at the existing player-turn boundary; preserve captured actor/session/window and cancel on every existing lifecycle hook.
- [x] Connect normal game launcher to separate Qwen and D3 services, preserving Qwen model selection and a Qwen-only fallback.
- [x] Build and run targeted adapter/runtime tests, then package and install.
- [x] Run native live-stage callback/failure/cancellation acceptance on the disposable fixture, and a bounded actual-model smoke. Preserve its semantic mistakes rather than retuning indefinitely. Real Talk closure and invoked save/scene/disable hooks are covered; actual disk-load/scene-transition acceptance remains separate.
- [x] Record scope, evidence and remaining architecture work in repository and Obsidian. See `../specs/2026-10-20-live-decision-routing-acceptance.md`.
