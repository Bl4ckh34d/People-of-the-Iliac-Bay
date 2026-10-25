# Optional warm Breeze server prototype

This is a standalone prototype. It does not change `coordinator.py` or `RunNativePilotAudio.ps1`, and it does not start, stop, or kill processes. Keep current full-WAV game tests as baseline. Parent operator decides whether to run this separate greeting trial after those tests.

Pinned audio.cpp `0.9.2-drama3-base-small` at commit `50749561` supports eager model load in server config and SSE PCM output from `POST /v1/audio/speech`. Config in `server_breeze_streaming.json` binds loopback port `8094`, sets `lazy_load: false`, `idle_unload_ms: 0`, and configures pinned Breeze Q4_0 in streaming mode. Check that port `8094` is free before a later manual start. This config uses the current workstation's model path; edit only path if installation differs.

## Greeting trial sequence

1. Verify the server binary and model against `local-tts/download-manifest.json`, then check port `8094` is free. Start the server manually with the pinned `audiocpp_server.exe` and config. Record the exact server PID, executable, command line, and creation time. Wait for the Breeze model-load success in its log; `/health` alone proves service readiness, not model load. Keep D3 and game processes untouched.
2. Send the greeting through `server_sse_client.py`. Pass finalized text and that NPC's `profiles.json` instruction. Reference-free Voice Design omits both reference fields; voice cloning can pass a server-visible reference WAV and its exact transcript together. The client posts `response_format=pcm`, `stream_format=sse`, `options.instruct`, `options.guidance_scale`, and optional `options.seed`; it decodes each `speech.audio.delta` event, requires `speech.audio.done` and `[DONE]`, then writes one complete 24 kHz mono PCM16 WAV. It fails on HTTP/SSE errors and refuses to overwrite output.
3. Wait for the client to exit successfully and verify output WAV is complete. Immediately call `POST /v1/tasks/unload_models` with `{"model_ids":["breeze"]}`. Confirm the response and unload log before playback. This unload timing is after all PCM is generated and buffered, before playback starts. Do not wait for playback completion to unload.
4. Play the complete WAV through the native audio path. Keep this playback measurement separate from generation time. Restore/start Qwen through the existing launcher after unload confirmation, then let user type while greeting playback runs if trial setup supports it.

The server process stays operator-owned and running after model unload. The client only makes the speech HTTP request; it has no process start/stop or kill code. Use the server unload API to free Breeze model/session memory. Do not kill the server PID as a shortcut. `ui_management` stays false; `/v1/tasks/unload_models` is the documented task unload route.

Example later server launch, from the DFU repository root:

```powershell
$runtime = 'C:/Users/Daniel/Desktop/Code_Experiments/DFU/local-tts/runtime/audio-v0.9.2-drama3-base-small-cuda12.4'
& "$runtime/audiocpp_server.exe" --config 'tools/BreezeTtsTrial/server_breeze_streaming.json' --log --log-file 'work/breeze-tts-20261021/audio-server.log'
```

This foreground command is for an operator-controlled trial only. No server launch is part of the client. After a later successful synthesis, unload with:

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8094/v1/tasks/unload_models' `
  -ContentType 'application/json' -Body '{"model_ids":["breeze"]}'
```

Example later request, from the DFU repository root:

```powershell
python tools/BreezeTtsTrial/server_sse_client.py `
  --endpoint http://127.0.0.1:8094 `
  --text 'A short finalized greeting.' `
  --instruction 'Adult female voice. Warm, steady delivery.' `
  --seed 71001 `
  --guidance-scale 4 `
  --output-wav 'work/breeze-tts-20261021/server-greeting.wav'
```

For reference audio, the server API accepts `voice_ref: {"type":"path","path":"..."}` and `reference_text`; the path is on the server host. The client requires both together or neither. The pinned Breeze request-option list includes `instruct`, `guidance_scale`, and `seed`; Voice Design uses `task=tts` without a reference. `race_voice_batch.py` applies the checked-in 20-sample fixed-text corpus sequentially to an already-running endpoint, writes one WAV and metadata row per sample, then creates `gallery.html` grouped by race. It does not start/stop the server, retry samples, or autoplay audio. The caller owns confirming that the already-running server has the intended model/config loaded.

Verification completed offline only: parser tests use fabricated SSE byte chunks, and batch tests replace the HTTP synthesizer with a local WAV fixture. They make no HTTP request. No audio.cpp server, model, inference, or process was started or changed. Streaming TTFA, GPU memory overlap, and the warm-first greeting result remain unmeasured.

Pinned primary references:

- [Server API at pinned commit](https://github.com/0xShug0/audio.cpp/blob/50749561/app/server/api.md) — `/v1/audio/speech`, `voice_ref`, `options.instruct`, SSE and unload endpoints.
- [Server examples at pinned commit](https://github.com/0xShug0/audio.cpp/blob/50749561/examples/server/README.md) — config format, reference library, and stream-mode requirement.
- [Breeze server config example at pinned commit](https://github.com/0xShug0/audio.cpp/blob/50749561/examples/server/breeze-tts.json) — server/model config fields.
