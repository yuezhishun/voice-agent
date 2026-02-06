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

## Metrics
```bash
curl http://127.0.0.1:5079/metrics
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

Optional real Kokoro TTS test:
```bash
export KOKORO_MODEL_DIR=/path/to/kokoro-v1.0
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealKokoroTtsTests
```

Optional real end-to-end test (ManySpeech ASR + GLM Agent + Kokoro TTS):
```bash
export REAL_E2E_WAV_FILE=/path/to/16k_pcm16_mono.wav
export REAL_PARAFORMER_MODEL_DIR=/home/yueyuan/voice-agent/models/paraformer-online-onnx
export KOKORO_MODEL_DIR=/home/yueyuan/voice-agent/models/kokoro-multi-lang-v1_0
export GLM_API_KEY=your_key
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
export REAL_SENSEVOICE_MODEL_DIR=/home/yueyuan/voice-agent/models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealSenseVoiceTwoPassTests
```
