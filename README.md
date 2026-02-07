# voice-agent

面向实时语音交互的 `ASR + Agent + TTS` 工程化项目。当前仓库已从纯设计阶段进入可运行的 MVP 阶段。

## 当前阶段

- 已有可运行服务：`src/VoiceAgent.AsrMvp`
- 已有测试工程：`src/VoiceAgent.AsrMvp.Tests`
- 已沉淀设计文档：`docs/`
- `referense-code/` 仅作参考，不是生产基线

## 快速启动

```bash
dotnet run --project src/VoiceAgent.AsrMvp/VoiceAgent.AsrMvp.csproj --urls http://127.0.0.1:5079
```

健康检查：

```bash
curl http://127.0.0.1:5079/healthz
```

WebSocket 接口：

- `ws://127.0.0.1:5079/ws/stt`

## 文档入口

- 当前进度初始化基线：`docs/01_项目进度初始化基线.md`
- two-pass 方案：`docs/09_2pass方案描述.md`
- ASR 验收清单：`docs/08_ASR验收检查清单.md`
- ASR 测试集与端到端演练建议：`docs/12_ASR测试集与端到端演练建议.md`
- 音频处理方案：`docs/11_音频处理过程.md`
- 文本后处理策略：`docs/10_文本后处理策略详解.md`

## 术语约定

文档和代码统一使用以下术语：`ASR`、`Agent`、`TTS`、`streaming`、`two-pass`。
