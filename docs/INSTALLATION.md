# Installation and local dialogue

The installed portrait/speech checkpoint and exact native test scope are recorded in [23 October acceptance](2026-10-23-portrait-speech-acceptance.md).

23 October speech/portrait source update: normal play uses the selected `gemma-4-E4B-it-Q4_K_M.gguf` with GPU execution from `local-llm/active-model.json`, plus Breeze TTS. Older Qwen/D3 setup sections below are historical development records, not the current model installation or launch recommendation. The normal launcher no longer starts D3. Current model choice does not imply that all semantic behavior tests pass.

Each native `personId` receives one persisted voice profile under `local-tts/voices/per-npc/v1`: seven independent standard-normal voice-design traits, a unique random seed, and an individual neutral reference WAV. The reference uses the fixed gallery sentence, not the short in-game greeting. First speech creates this reference without playing it; later greetings, replies and mood changes reuse its exact hash and seed. Mood/personality add delivery instructions without rerolling the base identity. Back up this voice directory to preserve voices across installations. Invalid profiles or references are rejected rather than silently replaced. The old twelve-profile registry is not required by normal per-NPC speech.

The animated portrait package is rebuilt from the gallery catalog, currently CommonFaces records 72–91. It includes six moods, lateral gaze, blinking and speech mouth shapes. Asset and timing-policy changes flow through `Deploy.ps1`; changes to gallery JavaScript behavior still require reviewing the equivalent C# logic. Catalog export preserves each portrait's review status.

23 October source update: a guided Windows save-transfer helper is available to build with `tools/MemoryArchiveGui/Publish.ps1`. It packages `MemoryArchiveGui.exe` and the existing validated `MemoryArchiveTool.exe` together. This is a local development artifact, not an uploaded release. The new `Dialogue.DispositionCommerce` setting can disable personal price adjustments and commerce refusal while retaining conversational relationship effects. Reopen an existing commerce window after changing the setting. These runtime changes require a newly built mod and quest-hook DLL; no deployment is implied here.

20 October verified development checkpoint: installed bundle SHA-256 `E5402CF0DFBCB46F4148112EB0BAFB451D9A21EB8219ED60F7B60C7707D34FE6`, 654 runtime assets. Core 1289/1289, adapter 77/77, RuntimeCompile with zero warnings/errors, Unity build/install and the bounded native social-mood fixture pass. Scope and limitations: [persistent mood acceptance](2026-10-20-persistent-mood-acceptance.md). This remains a local development build, not a published release.

Earlier 20 October development checkpoint `E3D1F5BA` added one bounded missing-Wait-duration repair to normal structured extraction. The separate two-pass strategy requires explicit `-peopleoftheiliacbay-goal-two-pass` in addition to existing goal extraction; the normal hybrid launcher does not enable it. Model-quality and acceptance limits: [parameter feedback report](2026-10-20-parameter-feedback-and-two-pass-results.md).

Status checked 20 October 2026. This repository is a development source snapshot. There is currently **no published release bundle or complete end-user installer**. `StartLocalDialogue.ps1` starts an already-installed server; it does not download the model, install CUDA libraries or install the mod.

## Daggerfall Unity mod

The tested project targets **Daggerfall Unity 1.1.1**. For source builds, see [repository setup](DEVELOPMENT.md#repository) and [integration notes](DEVELOPMENT.md#integration); those need a separate DFU source tree, Unity editor and build dependencies. Downloading this repository does not produce a playable installation by itself.

When a built `peopleoftheiliacbay.dfmod` is available, close DFU, place it in the player's `DaggerfallUnity_Data/StreamingAssets/Mods` directory and enable **People of the Iliac Bay** in the mod list. The stable mod GUID remains unchanged. Back up your saves and existing bundle before an update; use dedicated test saves during development.

Install third-party mods separately, following their authors' requirements. The local integration set and explicit limitations are recorded in [external requirements](COMPATIBILITY.md#requirements), [external audit](COMPATIBILITY.md#external-mods) and [tested local order](EXTERNAL-MOD-LOAD-ORDER.txt). These are development evidence, not a promise that arbitrary versions work together. Changed interior layouts require a restart; save outdoors before changing those mods.

<a id="memory-backups"></a>

## Memory archives, backups and moving saves

Schema 23 stores conversation records once in `PeopleMemory` alongside the `SAVE0`, `SAVE1`, etc. folders in DFU's configured Saves directory. Each save holds an immutable checkpoint. Earlier slots and alternate timelines retain their own history, even when their game dates match. Existing inline journals migrate when that slot is next saved; the mod does not rewrite other old slots. Older mod builds cannot read schema 23.

**Back up the entire Saves directory, including PeopleMemory.** Copying one SAVE folder alone no longer carries its conversation history. A missing/corrupt archive produces a load error and suspends the mod rather than silently losing memories. Restore the archive from the matching backup. Do not delete archive chunks merely because one save slot was deleted: another slot or exported backup can still reference that ancestry.

For a portable single-save package, close DFU and use the self-contained `MemoryArchiveTool.exe` from the release download. The command-line tool needs no source checkout or installed .NET runtime on the player machine. Export writes a versioned manifest with the save checkpoint, exact ancestry list, and relative path, length, and SHA-256 for every packaged file. It includes only ancestry referenced by that save, not sibling futures. Import validates the complete manifest, file set, checksums, and ancestry before publishing chunks; it preserves valid existing chunks and requires an unused destination slot. Existing packages without a manifest remain importable only after path and exact-ancestry validation. The tool prints a warning because legacy packages cannot verify save-file hashes.

```powershell
.\MemoryArchiveTool.exe export "C:/path/to/Saves/SAVE4" "C:/backups/my-save.zip"
.\MemoryArchiveTool.exe import "C:/backups/my-save.zip" "C:/path/to/Saves" SAVE12
```

For the guided interface, keep `MemoryArchiveGui.exe` and `MemoryArchiveTool.exe` in one folder, then open the GUI. Select Export or Import, choose the requested paths, and review the resolved destination before confirming. Import suggests an unused slot and rejects occupied slots. Cancellation before confirmation starts no transfer. Error details include recovery guidance; a failed import can leave harmless unreferenced immutable chunks, which should not be deleted manually. See [guided helper instructions](../tools/MemoryArchiveGui/README.md).

Project maintainers can build both self-contained Windows x64 executables with `tools/MemoryArchiveGui/Publish.ps1`, which defaults to `work/MemoryArchiveGui/win-x64`. The CLI-only publisher remains `tools/PublishMemoryArchiveTool.ps1`, defaulting to `work/MemoryArchiveTool/win-x64`. Both permit an alternate output directory only below the repository's `work` folder and do not clean it. The .NET 8 SDK is needed for building; the published helpers include their runtime. `tools/TestPublishedMemoryArchiveTool.ps1` checks CLI startup without `dotnet` on `PATH` plus temporary-fixture manifest export/import. GUI controller and offscreen Windows Forms regressions are described in the helper README. A clean-machine run, manual file-picker review, native game load and uploaded Nexus release remain separate acceptance steps.

Normal gameplay requires no database server, embedding model or extra runtime. The shared archive still grows with new conversations; automatic forgetting and archive garbage collection are not implemented.

If a save reports missing or corrupt history, restore the matching full `Saves` backup, including `PeopleMemory`, or import the matching portable package into a new slot. Never replace a broken checkpoint with another timeline's archive.

## Optional local LLM: what is actually used

The tested local setup uses:

| Component | Recorded configuration |
| --- | --- |
| Runtime | Prebuilt Windows x64 llama.cpp, build `b11680` |
| GPU backend | NVIDIA CUDA 12.4 runtime package and matching DLL package |
| Model | Qwen2.5-7B-Instruct, GGUF `Q4_K_M`, two shards |
| Endpoint | `http://127.0.0.1:8080` |
| Launcher defaults | Context 4096, CPU inference with CUDA offload disabled, one parallel request |
| Reference machine | Windows, RTX 4070 SUPER with 12 GiB VRAM, 64 GiB system RAM |

Official downloads: [llama.cpp b11680 release](https://github.com/ggml-org/llama.cpp/releases/tag/b11680) and [Qwen's Qwen2.5-7B-Instruct GGUF repository](https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF). These are the recorded runtime/model choices, not a claim that the newest available versions were tested.

The local runtime archives are `llama-b11680-bin-win-cuda-12.4-x64.zip` and `cudart-llama-bin-win-cuda-12.4-x64.zip`. Extract the server and matching dependencies into the same `bin` directory. Retain the runtime's DLLs, including its CUDA/cuBLAS and GGML dependencies. This prebuilt arrangement does not require Python or a Python inference framework.

## Manual folder layout and startup

The current launcher expects `local-llm` alongside the source checkout. The checkout directory can have any name; the launcher resolves `local-llm` from its parent directory. `DFU/tools/StartGame.ps1` discovers the checkout by `mod/PeopleOfTheIliacBay.dfmod.json`, or accepts an explicit `-RepositoryRoot` when multiple checkouts exist.

### Selecting a local model

`StartLocalDialogue.ps1` reads the optional `local-llm/active-model.json` selection. Paths are relative to `local-llm` unless absolute. For the requested Qwen2.5-7B-Instruct abliterated v2 Q4_K_M trial, the selection is:

```json
{"modelPath":"models/abliterated/Qwen2.5-7B-Instruct-abliterated-v2.Q4_K_M.gguf","executionDevice":"CPU"}
```

Normal launch uses CPU inference (`--device none -ngl 0 --no-kv-offload --no-op-offload`) so Qwen leaves CUDA memory available for D3 and Breeze TTS. Set `executionDevice` to `GPU` in this file, or pass `-ExecutionDevice GPU` to `StartLocalDialogue.ps1`, to opt in to CUDA inference. The GPU path uses `--device CUDA0`, 99 layers by default, and explicit KV/operator offload. A running server is reused only when its command line and endpoint match the selected device; the launcher never changes or replaces an incompatible running server.

This is a community modification of the same base model. Sources: [huihui-ai model card](https://huggingface.co/huihui-ai/Qwen2.5-7B-Instruct-abliterated-v2) and [RichardErkhov GGUF conversion](https://huggingface.co/RichardErkhov/huihui-ai_-_Qwen2.5-7B-Instruct-abliterated-v2-gguf). A local model change does not establish improved conversation, planning or instruction accuracy; retain and compare failed cases.

An explicit `-ModelPath` overrides the selection for diagnostics. Without a selection file, the launcher uses the original two-shard Qwen model listed below. Invalid selections and incomplete shard sets fail explicitly rather than silently selecting another model. Existing endpoint, model, context and listener ownership checks still apply; the launcher never replaces a running incompatible server automatically.

Close DFU before switching. Stop only the verified local model server, change the selection, then launch normally. The local swap retains `active-model.baseline.json` beside `active-model.json`; copying the baseline selection over the active selection and restarting the server restores the original model. Both original GGUF shards remain installed. `StartGame.ps1` uses the selected model automatically.

The folder layout below documents the original baseline. A selected single-file GGUF does not need those baseline shards for serving, although they remain available for rollback.

```text
DFU/
  PeopleOfTheIliacBay/
    tools/StartLocalDialogue.ps1
  Player/
    DaggerfallUnity.exe
  local-llm/
    bin/
      llama-server.exe
      ...matching runtime DLLs...
    models/
      qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf
      qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf
```

Download **both** matching model shards into `models`. Point the server at the first shard as the launcher does; keep the second alongside it. Do not rename unrelated quantizations to match these filenames. The recorded shards total approximately 4.68 GB on disk; disk size is not the GPU memory requirement.

For normal play, use `DFU/tools/StartGame.ps1` or the `DFU/Play DFU.lnk` shortcut. The launcher starts or reuses the local server, checks `/health` and `/props` for the expected model, 4096-token context and one slot, then opens the standalone player. Server output goes to the checkout's `work` directory. Startup errors remain visible in a Windows dialog, the command-line error stream and `work/local-dialogue-launcher-error.log`.

On a normal `Play DFU` launch, the hidden lifecycle watcher stops only the Gemma and Breeze services that this launch started, after the exact game process exits. Services already running before launch remain available to other tools. Use `DFU/tools/StartGame.ps1 -DevelopmentMode` for gallery or test sessions that should keep model services warm after the game closes. Launching `DaggerfallUnity.exe` directly bypasses the watcher and leaves services running.

Production full-WAV speech waits for exact-text CPU word alignment and pinned Rhubarb viseme cues before the coordinator publishes the response manifest that permits playback. The game therefore keeps its pending/thinking presentation visible during preparation. Both sidecars share a 240-second request deadline; if preparation fails or times out, no playable manifest is published and the accepted response falls back to text only.

The native audio loader also validates and caches both timelines before making a prepared response playable. The 240-second deadline includes first-time neutral reference creation and utterance synthesis. Saved sentence-streaming settings alone cannot bypass this barrier: legacy streaming requires the explicit developer argument `-peopleoftheiliacbay-allow-legacy-speech-streaming`. Alignment and Rhubarb are CPU helpers, not additional dialogue or speech models; cancellation during an already running helper is observed when it exits or reaches its bounded timeout.

### Historical hybrid decision routing (superseded by Gemma-only dialogue)

Current typed goal binding filters structurally invalid native candidates before model selection. For example, a later Wait must use the preceding MoveTo destination; the old-location Wait is excluded from that task step. Qwen still selects the target, D3 still decides willingness, and no action is committed after the originating chat closes while a decision is pending. See [ordered-goal acceptance](superpowers/specs/2026-10-20-native-ordered-goals-acceptance.md) for the bounded physical and cancellation tests.

The current normal hybrid launcher enables `-peopleoftheiliacbay-goal-extraction` and `-peopleoftheiliacbay-action-wording`. For new action requests, Qwen first extracts typed amounts and game-time units without a candidate menu, then resolves current targets. D3 evaluates the bound proposal before native commitment. Qwen can then phrase the verified outcome without granting action authority to prose. Slow turns can display one LLM-written temporary narration outside transcript memory; no fixed phrase list is used. All gameplay durations mean in-game time. Requests with emitted unresolved constraints or unavailable targets produce no saved action. Pending offers and existing commitments retain their existing semantic paths. The [current acceptance report](2026-10-20-action-wording-and-chain-acceptance.md) separates controlled execution from real-model interpretation and records semantic limits. The experimental two-pass extractor remains offline.

The normal launcher also attempts to start or reuse D3-mini on `127.0.0.1:8093`, using `tools/StartLocalDecision.ps1`. It requires the verified local files `local-llm/models/d3-mini/vllm-d3-mini-Q4_K_M.gguf` and `mmproj-vllm-d3-mini-Q8_0.gguf`; it does not download them. The existing selected Qwen dialogue model remains on port 8080.

When both services are ready, D3 chooses the first speech-act route. For semantic action requests, Qwen interprets parameters and D3 then accepts or declines the complete grounded proposal before native commitment. This gate covers single actions, ordered tasks and accepted pending offers. Existing native target, price, consent and action checks remain in place. Accepted acknowledgments still use authoritative typed receipts; general goal extraction/planning remains under development. If Mini cannot start, the launcher warns and runs the existing Qwen-only path. Use `DFU/tools/StartGame.ps1 -DialogueOnly` to skip Mini explicitly. An already running game is never restarted or retrofitted with routing flags.

For an independently started player, explicit routing arguments are `-peopleoftheiliacbay-decision-routing -peopleoftheiliacbay-decision-model "<exact model ID returned by the service>" -peopleoftheiliacbay-decision-endpoint "http://127.0.0.1:8093/v1/systemone"`. Routing and shadow mode are mutually exclusive. The returned model ID must match exactly. Technical routing failures fall back once to Qwen. A failed willingness request after interpretation-only output leaves agreement unconfirmed and commits nothing; it does not infer consent. Closed or stale conversation requests do not continue.

The reported outage followed a PC restart: port 8080 had no listening server, while the old Play shortcut launched `Player/DaggerfallUnity.exe` directly and skipped model startup. The server does not start at Windows logon. After a reboot, use the shortcut or `DFU/tools/StartGame.ps1`; this remains an explicit local launch path with no scheduled task, registry run entry or other global autostart.

The game launcher always targets `127.0.0.1:8080`, which is the endpoint configured by the mod. `StartLocalDialogue.ps1 -Port <port>` can be used for a separate manual diagnostic, but changing that port does not reconfigure the mod or `StartGame.ps1`.

Dialogue quality remains mixed. In local paired fixtures, adding the live haunting status removed the 10:09 AM nighttime claim, but the NPC still described the public inn where they work as their home in another sample. Review the recorded prompts and outputs in `LivingCities/work/dialogue-grounding-before-haunting-helper.json` and `.../dialogue-grounding-after-haunting-helper.json`; do not treat the clock fix as complete location grounding.

To start the server manually in the foreground, run from the repository directory in PowerShell:

```powershell
.\tools\StartLocalDialogue.ps1
```

The server occupies that console while running. From a second console, check readiness:

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:8080/health'
```

Enable the mod's free-text dialogue option and use the matching local endpoint. `EnableFreeText` and `PrewarmModel` are the current dialogue controls. Prewarming sends a background request to an available server; **it does not launch or install that server**. Leave the server running while using generated dialogue; stop the console process when finished.

To use native conversation without generated chat, turn off free text and prewarming. The requested complete optional installer and broader chapter-by-chapter module isolation are future work, not provided by these two settings alone. A model server is not a game-content authority: game state, allowed facts, prices and actions remain controlled by the mod/native systems.

## Hardware and prerequisites: measured versus unknown

The reference GPU has 12 GiB VRAM. Earlier local model testing recorded roughly 6.3 GiB GPU use at a 4096-token context, but graphics/mods/context length add their own demand. **A minimum supported VRAM or RAM value has not been established.** Do not interpret the reference machine's capacity as a verified minimum, or model size as enough free VRAM for the game and inference together.

Only the Windows/NVIDIA/CUDA setup above has been exercised here. CPU inference, AMD/Intel GPUs and other operating systems are not certified by this project. Other llama.cpp backends may exist, but their availability does not establish mod integration or acceptable performance.

Player-side use of this prebuilt server needs **no Python version**. The .NET 8 SDK is for source/core-test development; Unity 2019.4.40f1 is for runtime builds. Neither is an LLM Python environment. If a future bootstrap uses Python, its exact supported interpreter and dependency versions must be specified and clean-machine tested before release.

## Installation work still in the backlog

- Publish a versioned mod bundle and tested dependency list with hashes.
- Supply an optional runtime/model bootstrap or packaged launcher with consent, checksums, resumable downloads, prerequisite checks, health checks and update/removal behavior.
- Keep the no-LLM path free of model/CUDA downloads and background processes.
- Measure minimum/recommended combined game/inference hardware and test failures such as missing DLLs, insufficient VRAM, occupied port and missing model shard.
- Document clean-machine validation and module-toggle/save migration behavior.

See the [roadmap](ROADMAP.md) for priorities. None of these installer features is claimed as implemented by this guide.
