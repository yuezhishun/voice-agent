# ASR 测试集与端到端演练建议（2026-02-07）

## 目标

为 `ASR -> Agent -> TTS` 全链路自动化提供可复现的真实语音样本来源与执行方式。

## 推荐测试集（优先公开可商用评测）

1. AISHELL-1（中文普通话，安静近场基线）
   - 适合做基础识别准确率回归。
2. ST-CMDS（中文命令词，短句与口语化）
   - 适合命令型交互和短指令稳定性。
3. Aishell-3（中文多说话人语音）
   - 适合补充音色/说话人多样性，验证鲁棒性。
4. LibriSpeech test-clean/test-other（英文与中英混说补充）
   - 适合验证英文术语与口音场景。
5. MUSAN（噪声集）
   - 可与语音样本混合，构造噪声桶（键盘/人声背景/音乐）。

## 本仓库建议分桶

1. `quiet`：安静环境。
2. `noisy`：MUSAN 混噪后样本。
3. `fast`：语速高于常规（可用时长压缩生成）。
4. `code_switch`：中英混说或英文术语占比高。
5. `long_form`：单段 > 15s，验证 `max_segment`。
6. `interrupt`：TTS 播放中插话。
7. `dependency_failure`：ASR/Agent/TTS 故障注入。

## 数据组织规范

放在本地目录（不提交仓库）：

```text
<dataset_root>/
  quiet/
  noisy/
  fast/
  code_switch/
  long_form/
  interrupt/
```

每条样本建议有以下字段（可用 json/csv 记录）：

- `caseId`
- `bucket`
- `audioPath`
- `referenceText`
- `expectedFinalReason`
- `allowFallback`
- `expectInterrupt`
- `notes`

参考样例见：

- `src/VoiceAgent.AsrMvp.Tests/TestAssets/datasets/scenario_manifest.sample.json`

## 自动化测试入口

0. 一键生成 `dev-clean-2` 抽样 200 条（16k PCM16 WAV）

```bash
bash scripts/generate_dev_clean_2_sample.sh
```

默认行为：

- 自动下载并解压 `Mini LibriSpeech dev-clean-2`（若本地不存在）。
- 固定随机种子抽样 200 条音频。
- 输出到 `src/VoiceAgent.AsrMvp.Tests/TestAssets/librispeech-dev-clean-2-200/`。
- 生成 `manifest.tsv`（含 `caseId/bucket/referenceText/sourceFlac`）。

常用参数：

```bash
# 指定已解压数据目录
bash scripts/generate_dev_clean_2_sample.sh \
  --input-dir /path/to/LibriSpeech/dev-clean-2 \
  --count 200 \
  --seed 20260207
```

1. 完整链路 + 异常注入矩阵（mock 环境）

```bash
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter EndToEndScenarioMatrixTests
```

覆盖：

- 正常链路（`stt.final`、`agent.response`、`tts.start/chunk/stop`）
- ASR 异常（`stage=asr`）
- Agent 异常 + fallback
- TTS 异常（`stage=tts`）

2. 真实 ASR 批量回归（FunASR）

```bash
cp src/VoiceAgent.AsrMvp.Tests/real-integration.settings.json.example src/VoiceAgent.AsrMvp.Tests/real-integration.settings.json
# 编辑 RealIntegration 配置：Enabled/FunAsrWebSocketUrl/RealWavDir
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealWavBatchTests
```

3. 真实全链路回归（ManySpeech + GLM + Kokoro）

```bash
# 编辑 real-integration.settings.json 中 E2eWavFile/ParaformerModelDir/KokoroModelDir/Glm 配置
dotnet test src/VoiceAgent.AsrMvp.Tests/VoiceAgent.AsrMvp.Tests.csproj --filter RealEndToEndFlowTests
```

## 验收建议

1. 每次发布前固定执行 `EndToEndScenarioMatrixTests`。
2. 每周至少一次真实数据集批量回归（`RealWavBatchTests` 或 `RealWavBatchManySpeechTests`）。
3. 每次模型或配置大改后执行真实全链路回归（`RealEndToEndFlowTests`）。
