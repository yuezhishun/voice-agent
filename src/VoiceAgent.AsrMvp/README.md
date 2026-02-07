# VoiceAgent ASR MVP

This is the phase-1 MVP service for real-time transcription.

## Features
- WebSocket ingest (`/ws/stt`)
- Binary PCM16 audio input (16k mono expected)
- ASR processing chain: preprocess -> classify -> quality check -> VAD -> endpointing
- Enhanced audio preprocess: DC removal + pre-emphasis + noise suppression + AGC
- Endpointing state machine (`end_silence`, `min_segment`, `max_segment`, `merge_back`)
- `stt.partial` and `stt.final` events
- Transcript post-process: normalization + punctuation restore
- Optional 2-pass final revision: SenseVoice offline windowed refine + prefix lock
- `agent.response` event triggered by each `stt.final`
- `tts.start/chunk/stop` events plus binary PCM16 audio chunks
- `listen.stop` text control to force finalization
- Runtime profile + resilience config (`dev/test/prod`, timeout/retry/circuit-breaker)
- Structured dependency health details (`/healthz`)
- Unified error payload (`traceId` + `error.stage` + `error.code`)
- Dynamic endpointing profile (`quiet/noisy`)
- TTS interrupt event when user speaks during playback
- Alert endpoint (`/alerts`) and release/rollback view (`/releasez`)

## Run
```bash
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj --urls http://127.0.0.1:5079
```

To use real FunASR Paraformer online websocket instead of mock, set:
- `AsrMvp:AsrProvider=funasr`
- `AsrMvp:FunAsrWebSocket:Url=ws://127.0.0.1:10095`

Protocol is aligned with the official FunASR websocket runtime client/server fields (`mode`, `chunk_size`, `is_speaking`, binary PCM chunks).

To use local Paraformer ONNX (ManySpeech) instead of mock, set:
- `AsrMvp:AsrProvider=manyspeech`
- `AsrMvp:ManySpeechParaformer:ModelDir=models/paraformer-online-onnx`

The engine auto-generates `tokens.txt` from `tokens.json` when needed.

To use real GLM agent (OpenAI-compatible API), set:
- `AsrMvp:Agent:Provider=openai`
- `AsrMvp:Agent:OpenAiCompatible:BaseUrl=https://open.bigmodel.cn/api/paas/v4/`
- `AsrMvp:Agent:OpenAiCompatible:Model=glm-4.7`
- `AsrMvp:Agent:OpenAiCompatible:ApiKey=<your_api_key>`

Example:
```bash
cp src/VoiceAgent.AsrMvp/appsettings.Local.json.example src/VoiceAgent.AsrMvp/appsettings.Local.json
# edit appsettings.Local.json, then run
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj --urls http://127.0.0.1:5079
```

To use local Kokoro TTS instead of mock, set:
- `AsrMvp:Tts:Provider=kokoro`
- `AsrMvp:Tts:Kokoro:ModelDir=models/kokoro-v1.0`

Kokoro model directory should include:
- `model.onnx`
- `voices.bin`
- `tokens.txt`
- `espeak-ng-data/`
- `dict/`

## Health
```bash
curl http://127.0.0.1:5079/healthz
```
`/healthz` returns per-stage checks for `asr/agent/tts` and reports `503 degraded` when any real dependency is unhealthy.

## Metrics
```bash
curl http://127.0.0.1:5079/metrics
```
`/metrics` includes stage-level success/failure/error-rate and latency stats (avg/p95).

## Alerts
```bash
curl http://127.0.0.1:5079/alerts
```
`/alerts` evaluates error-rate/latency/dependency rules and returns active alerts.

## Release
```bash
curl http://127.0.0.1:5079/releasez
```
Use `AsrMvp:Release:ForceMockAsr|ForceMockAgent|ForceMockTts=true` to force degrade/rollback per stage.

## WebSocket
- URL: `ws://127.0.0.1:5079/ws/stt`
- Input:
  - Binary: PCM16LE audio bytes
  - Text (optional): `{"type":"listen","state":"stop","sessionId":"..."}`
- Output examples:
```json
{"type":"stt","state":"partial","text":"...","segmentId":"seg-1","sessionId":"...","startMs":0,"endMs":320,"traceId":"...","latencyMs":{"stt":78}}
```
```json
{"type":"stt","state":"final","text":"...","segmentId":"seg-1","sessionId":"...","startMs":0,"endMs":2560,"traceId":"...","finalReason":"endpointing|max_segment|listen_stop","latencyMs":{"stt":125}}
```
```json
{"type":"stt","state":"error","sessionId":"...","traceId":"...","error":{"stage":"agent","code":"AGENT_TIMEOUT","detail":"request timed out"}}
```
```json
{"type":"agent","state":"response","text":"...","sessionId":"...","segmentId":"seg-1"}
```
```json
{"type":"tts","state":"start","sessionId":"...","segmentId":"seg-1","sampleRate":16000,"sequence":0,"traceId":"..."}
```
```json
{"type":"interrupt","state":"stop","sessionId":"...","segmentId":"seg-1","reason":"user_speech","atMs":1738890000000,"traceId":"..."}
```
Binary websocket frames after `tts.start` are PCM16 audio chunks, followed by:
```json
{"type":"tts","state":"stop","sessionId":"...","segmentId":"seg-1","sampleRate":16000,"sequence":N,"traceId":"..."}
```

## Runtime Profile / Resilience
Default profile is `AsrMvp:RuntimeProfile=dev`.

Environment-layer templates:
- `src/VoiceAgent.AsrMvp/appsettings.Development.json`
- `src/VoiceAgent.AsrMvp/appsettings.Test.json`
- `src/VoiceAgent.AsrMvp/appsettings.Production.json`
- Optional local override (loaded automatically): `src/VoiceAgent.AsrMvp/appsettings.Local.json`

Example:
```bash
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj \
  --environment Production \
  --AsrMvp:RuntimeProfile=prod \
  --AsrMvp:Resilience:Asr:TimeoutMs=2500 \
  --AsrMvp:Resilience:Agent:RetryCount=1 \
  --AsrMvp:Fallback:EnableOnAgentFailure=true
```

## M2 Tuning
- `AsrMvp:Endpointing:DynamicProfileEnabled`
- `AsrMvp:Endpointing:QuietProfile:*`
- `AsrMvp:Endpointing:NoisyProfile:*`
- `AsrMvp:TranscriptStability:*`
- `AsrMvp:TwoPass:Trigger:*`

## File-based ASR tests
WAV fixtures are under `src/VoiceAgent.AsrMvp.Tests/TestAssets/`.

- `speech_then_silence.wav`: should produce at least one final ASR result.
- `silence.wav`: should produce no final result.
- scenario manifest sample: `src/VoiceAgent.AsrMvp.Tests/TestAssets/datasets/scenario_manifest.sample.json`

Generate a fixed 200-file dev-clean-2 sample set (one command):
```bash
bash scripts/generate_dev_clean_2_sample.sh
```
Output:
- `src/VoiceAgent.AsrMvp.Tests/TestAssets/librispeech-dev-clean-2-200/audio/*.wav`
- `src/VoiceAgent.AsrMvp.Tests/TestAssets/librispeech-dev-clean-2-200/manifest.tsv`

End-to-end scenario matrix (mock + fault injection):
```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter EndToEndScenarioMatrixTests
```
This matrix verifies:
- happy path: `stt.final -> agent.response -> tts.start/chunk/stop`
- ASR failure: standardized `stt.error` (`stage=asr`)
- Agent failure: fallback response + TTS output
- TTS failure: standardized `stt.error` (`stage=tts`)

Real integration tests use config file:

```bash
cp src/VoiceAgent.AsrMvp.Tests/real-integration.settings.json.example src/VoiceAgent.AsrMvp.Tests/real-integration.settings.json
# set RealIntegration.Enabled=true and fill paths/apiKey
```

Optional real-server smoke test (`FunAsrWebSocketSmokeTests`):

```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter FunAsrWebSocketSmokeTests
```

Batch real wav regression with real FunASR server:
```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealWavBatchTests
```
Per-file report is written to `/tmp/voice-agent-reports/real_wav_asr_report_*.txt`.

Batch real wav regression with local ManySpeech Paraformer:
```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealWavBatchManySpeechTests
```
Per-file report is written to `/tmp/voice-agent-reports/real_wav_asr_manyspeech_report_*.txt`.

Optional real Kokoro TTS test:
```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealKokoroTtsTests
```

Optional real end-to-end test (ManySpeech ASR + GLM Agent + Kokoro TTS):
```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealEndToEndFlowTests
```

Enable 2-pass with SenseVoice offline model:
```bash
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj \
  --AsrMvp:TwoPass:Enabled=true \
  --AsrMvp:TwoPass:Provider=sensevoice \
  --AsrMvp:TwoPass:SenseVoice:ModelDir=/home/yueyuan/voice-agent/models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17 \
  --AsrMvp:TwoPass:WindowSeconds=12 \
  --AsrMvp:TwoPass:WindowSegments=3
```

SenseVoice model download (sherpa-onnx release):
```bash
cd models
curl -L -o sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2 \
  https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2
tar -xjf sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2
```

Optional real 2-pass test:
```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealSenseVoiceTwoPassTests
```
