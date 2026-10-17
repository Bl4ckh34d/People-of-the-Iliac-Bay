# Installation and local dialogue

Status checked 17 October 2026. This repository is a development source snapshot. There is currently **no published release bundle or complete end-user installer**. `StartLocalDialogue.ps1` starts an already-installed server; it does not download the model, install CUDA libraries or install the mod.

## Daggerfall Unity mod

The tested project targets **Daggerfall Unity 1.1.1**. For source builds, see [repository setup](DEVELOPMENT.md#repository) and [integration notes](DEVELOPMENT.md#integration); those need a separate DFU source tree, Unity editor and build dependencies. Downloading this repository does not produce a playable installation by itself.

When a built `livingcities.dfmod` is available, close DFU, place it in the player's `DaggerfallUnity_Data/StreamingAssets/Mods` directory and enable **People of the Iliac Bay** in the mod list. The internal filename and GUID remain stable despite the public rename. Back up your saves and existing bundle before an update; use dedicated test saves during development.

Install third-party mods separately, following their authors' requirements. The local integration set and explicit limitations are recorded in [external requirements](COMPATIBILITY.md#requirements), [external audit](COMPATIBILITY.md#external-mods) and [tested local order](EXTERNAL-MOD-LOAD-ORDER.txt). These are development evidence, not a promise that arbitrary versions work together. Changed interior layouts require a restart; save outdoors before changing those mods.

<a id="memory-backups"></a>

## Memory archives, backups and moving saves

Schema 23 stores conversation records once in `PeopleMemory` alongside the `SAVE0`, `SAVE1`, etc. folders in DFU's configured Saves directory. Each save holds an immutable checkpoint. Earlier slots and alternate timelines retain their own history, even when their game dates match. Existing inline journals migrate when that slot is next saved; the mod does not rewrite other old slots. Older mod builds cannot read schema 23.

**Back up the entire Saves directory, including PeopleMemory.** Copying one SAVE folder alone no longer carries its conversation history. A missing/corrupt archive produces a load error and suspends the mod rather than silently losing memories. Restore the archive from the matching backup. Do not delete archive chunks merely because one save slot was deleted: another slot or exported backup can still reference that ancestry.

For a portable single-save package, close DFU and run these source-workspace tools with the .NET 8 SDK. Export automatically includes only the ancestry referenced by that save, not sibling futures. Import validates the archive, preserves existing chunks, and requires an unused destination slot:

```powershell
dotnet run --project tools/MemoryArchiveTool -c Release -- export "C:/path/to/Saves/SAVE4" "C:/backups/my-save.zip"
dotnet run --project tools/MemoryArchiveTool -c Release -- import "C:/backups/my-save.zip" "C:/path/to/Saves" SAVE12
```

Normal gameplay requires no database server, embedding model or extra runtime. The shared archive still grows with new conversations; automatic forgetting and archive garbage collection are not implemented.

## Optional local LLM: what is actually used

The tested local setup uses:

| Component | Recorded configuration |
| --- | --- |
| Runtime | Prebuilt Windows x64 llama.cpp, build `b11680` |
| GPU backend | NVIDIA CUDA 12.4 runtime package and matching DLL package |
| Model | Qwen2.5-7B-Instruct, GGUF `Q4_K_M`, two shards |
| Endpoint | `http://127.0.0.1:8080` |
| Launcher defaults | Context 4096, GPU layers 99, one parallel request |
| Reference machine | Windows, RTX 4070 SUPER with 12 GiB VRAM, 64 GiB system RAM |

Official downloads: [llama.cpp b11680 release](https://github.com/ggml-org/llama.cpp/releases/tag/b11680) and [Qwen's Qwen2.5-7B-Instruct GGUF repository](https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF). These are the recorded runtime/model choices, not a claim that the newest available versions were tested.

The local runtime archives are `llama-b11680-bin-win-cuda-12.4-x64.zip` and `cudart-llama-bin-win-cuda-12.4-x64.zip`. Extract the server and matching dependencies into the same `bin` directory. Retain the runtime's DLLs, including its CUDA/cuBLAS and GGML dependencies. This prebuilt arrangement does not require Python or a Python inference framework.

## Manual folder layout and startup

The current launcher expects `local-llm` alongside the `LivingCities` source folder. If you clone using another directory name, the launcher still resolves `local-llm` from the repository's parent directory.

```text
DFU/
  LivingCities/
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

From the repository directory in PowerShell:

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
