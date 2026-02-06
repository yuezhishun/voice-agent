# VoiceAgent ASR MVP

This is the phase-1 MVP service for real-time transcription.

## Features
- WebSocket ingest (`/ws/stt`)
- Binary PCM16 audio input (16k mono expected)
- Endpointing state machine (`end_silence`, `min_segment`, `max_segment`, `merge_back`)
- `stt.partial` and `stt.final` events
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
