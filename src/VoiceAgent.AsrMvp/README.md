# VoiceAgent ASR MVP

This is the phase-1 MVP service for real-time transcription.

## Features
- WebSocket ingest (`/ws/stt`)
- Binary PCM16 audio input (16k mono expected)
- Endpointing state machine (`end_silence`, `min_segment`, `max_segment`, `merge_back`)
- `stt.partial` and `stt.final` events
- `agent.response` event triggered by each `stt.final`
- `tts.start/chunk/stop` events plus binary PCM16 audio chunks
- `listen.stop` text control to force finalization

## Run
```bash
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj --urls http://127.0.0.1:5079
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
