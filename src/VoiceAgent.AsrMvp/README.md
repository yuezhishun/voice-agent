# VoiceAgent ASR MVP

This is the phase-1 MVP service for real-time transcription.

## Features
- WebSocket ingest (`/ws/stt`)
- Binary PCM16 audio input (16k mono expected)
- ASR processing chain: preprocess -> classify -> quality check -> VAD -> endpointing
- Endpointing state machine (`end_silence`, `min_segment`, `max_segment`, `merge_back`)
- `stt.partial` and `stt.final` events
- `agent.response` event triggered by each `stt.final`
- `tts.start/chunk/stop` events plus binary PCM16 audio chunks
- `listen.stop` text control to force finalization

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
- API key from env `GLM_API_KEY` (recommended) or config `AsrMvp:Agent:OpenAiCompatible:ApiKey`

Example:
```bash
export GLM_API_KEY=your_key
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj \
  --urls http://127.0.0.1:5079 \
  --AsrMvp:Agent:Provider=openai \
  --AsrMvp:Agent:OpenAiCompatible:Model=glm-4.7
```

## Health
```bash
curl http://127.0.0.1:5079/healthz
```

## WebSocket
- URL: `ws://127.0.0.1:5079/ws/stt`
- Input:
  - Binary: PCM16LE audio bytes
  - Text (optional): `{"type":"listen","state":"stop","sessionId":"..."}`
- Output examples:
```json
{"type":"stt","state":"partial","text":"...","segmentId":"seg-1","sessionId":"...","startMs":0,"endMs":320}
```
```json
{"type":"stt","state":"final","text":"...","segmentId":"seg-1","sessionId":"...","startMs":0,"endMs":2560}
```
```json
{"type":"agent","state":"response","text":"...","sessionId":"...","segmentId":"seg-1"}
```
```json
{"type":"tts","state":"start","sessionId":"...","segmentId":"seg-1","sampleRate":16000,"sequence":0}
```
Binary websocket frames after `tts.start` are PCM16 audio chunks, followed by:
```json
{"type":"tts","state":"stop","sessionId":"...","segmentId":"seg-1","sampleRate":16000,"sequence":N}
```

## File-based ASR tests
WAV fixtures are under `src/VoiceAgent.AsrMvp.Tests/TestAssets/`.

- `speech_then_silence.wav`: should produce at least one final ASR result.
- `silence.wav`: should produce no final result.

Optional real-server smoke test:
- set env `FUNASR_WS_URL` then run tests; `FunAsrWebSocketSmokeTests` will call the real websocket server.

Batch real wav regression with real FunASR server:
```bash
export FUNASR_WS_URL=ws://127.0.0.1:10095
export REAL_WAV_DIR=/path/to/your/wav_dir
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealWavBatchTests
```
Per-file report is written to `/tmp/voice-agent-reports/real_wav_asr_report_*.txt`.

Batch real wav regression with local ManySpeech Paraformer:
```bash
export REAL_WAV_DIR=/path/to/your/wav_dir
export REAL_PARAFORMER_MODEL_DIR=/home/yueyuan/voice-agent/models/paraformer-online-onnx
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealWavBatchManySpeechTests
```
Per-file report is written to `/tmp/voice-agent-reports/real_wav_asr_manyspeech_report_*.txt`.
