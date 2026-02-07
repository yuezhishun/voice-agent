using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Contracts;
using VoiceAgent.AsrMvp.Domain;
using VoiceAgent.AsrMvp.Pipeline;
using VoiceAgent.AsrMvp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<AsrMvpOptions>()
    .Bind(builder.Configuration.GetSection(AsrMvpOptions.SectionName))
    .Validate(ValidateRuntimeProfile, "AsrMvp:RuntimeProfile must be one of dev/test/prod")
    .ValidateOnStart();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAudioDecoder, Pcm16AudioDecoder>();
builder.Services.AddSingleton<IAudioPreprocessor, BasicAudioPreprocessor>();
builder.Services.AddSingleton<IAudioClassifier, BasicAudioClassifier>();
builder.Services.AddSingleton<IAudioQualityChecker, BasicAudioQualityChecker>();
builder.Services.AddSingleton<IEnergyVad, EnergyVad>();
builder.Services.AddSingleton<ResilienceCoordinator>();
builder.Services.AddSingleton<DependencyHealthProbe>();
builder.Services.AddSingleton<AlertEvaluator>();

var forceMockAsr = string.Equals(builder.Configuration[$"{AsrMvpOptions.SectionName}:Release:ForceMockAsr"], "true", StringComparison.OrdinalIgnoreCase);
var forceMockAgent = string.Equals(builder.Configuration[$"{AsrMvpOptions.SectionName}:Release:ForceMockAgent"], "true", StringComparison.OrdinalIgnoreCase);
var forceMockTts = string.Equals(builder.Configuration[$"{AsrMvpOptions.SectionName}:Release:ForceMockTts"], "true", StringComparison.OrdinalIgnoreCase);

var asrProvider = builder.Configuration[$"{AsrMvpOptions.SectionName}:AsrProvider"] ?? "mock";
if (forceMockAsr)
{
    builder.Services.AddSingleton<IStreamingAsrEngine, MockParaformerStreamingEngine>();
}
else if (string.Equals(asrProvider, "funasr", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IStreamingAsrEngine, FunAsrWebSocketStreamingEngine>();
}
else if (string.Equals(asrProvider, "manyspeech", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IStreamingAsrEngine, ManySpeechParaformerOnlineEngine>();
}
else
{
    builder.Services.AddSingleton<IStreamingAsrEngine, MockParaformerStreamingEngine>();
}

var agentProvider = builder.Configuration[$"{AsrMvpOptions.SectionName}:Agent:Provider"] ?? "mock";
if (forceMockAgent)
{
    builder.Services.AddSingleton<IAgentEngine, MockAgentEngine>();
}
else if (string.Equals(agentProvider, "openai", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(agentProvider, "openai-compatible", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(agentProvider, "glm", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAgentEngine, OpenAiCompatibleAgentEngine>();
}
else
{
    builder.Services.AddSingleton<IAgentEngine, MockAgentEngine>();
}

var ttsProvider = builder.Configuration[$"{AsrMvpOptions.SectionName}:Tts:Provider"] ?? "mock";
if (forceMockTts)
{
    builder.Services.AddSingleton<ITtsEngine, MockTtsEngine>();
}
else if (string.Equals(ttsProvider, "kokoro", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ITtsEngine, KokoroTtsEngine>();
}
else
{
    builder.Services.AddSingleton<ITtsEngine, MockTtsEngine>();
}

builder.Services.AddSingleton<EndpointingEngine>();
builder.Services.AddSingleton<TranscriptStabilizer>();
builder.Services.AddSingleton<TranscriptPostProcessor>();
builder.Services.AddSingleton<AsrPipelineMetrics>();
var twoPassEnabled = string.Equals(builder.Configuration[$"{AsrMvpOptions.SectionName}:TwoPass:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
var twoPassProvider = builder.Configuration[$"{AsrMvpOptions.SectionName}:TwoPass:Provider"] ?? "sensevoice";
if (twoPassEnabled && string.Equals(twoPassProvider, "sensevoice", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ITwoPassRefiner, SenseVoiceTwoPassRefiner>();
}
else
{
    builder.Services.AddSingleton<ITwoPassRefiner, NoopTwoPassRefiner>();
}

builder.Services.AddSingleton<AsrFileProcessor>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var sessions = new ConcurrentDictionary<string, SessionContext>();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

app.MapGet("/healthz", async (DependencyHealthProbe probe, CancellationToken ct) =>
{
    var checks = await probe.CheckAsync(ct);
    var ok = checks.All(x => x.Healthy);
    return ok
        ? Results.Ok(new { status = "ok", checks })
        : Results.Json(new { status = "degraded", checks }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/metrics", (AsrPipelineMetrics metrics) => Results.Content(metrics.SnapshotJson(), "application/json"));
app.MapGet("/alerts", async (AsrPipelineMetrics metrics, DependencyHealthProbe healthProbe, AlertEvaluator evaluator, CancellationToken ct) =>
{
    var health = await healthProbe.CheckAsync(ct);
    var snapshot = metrics.Snapshot();
    var alerts = evaluator.Evaluate(snapshot, health);
    return Results.Ok(new
    {
        status = alerts.Count == 0 ? "ok" : "warning",
        generatedAt = DateTimeOffset.UtcNow,
        alerts
    });
});
app.MapGet("/releasez", (IOptions<AsrMvpOptions> opts) =>
{
    var o = opts.Value;
    return Results.Ok(new
    {
        version = o.Release.Version,
        runtimeProfile = o.RuntimeProfile,
        asrProvider = o.Release.ForceMockAsr ? "mock(forced)" : o.AsrProvider,
        agentProvider = o.Release.ForceMockAgent ? "mock(forced)" : o.Agent.Provider,
        ttsProvider = o.Release.ForceMockTts ? "mock(forced)" : o.Tts.Provider,
        fallback = new
        {
            o.Fallback.EnableOnAsrFailure,
            o.Fallback.EnableOnAgentFailure,
            o.Fallback.EnableOnTtsFailure
        }
    });
});

app.MapMethods("/ws/stt", new[] { "GET" }, (Delegate)(async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required");
        return;
    }

    var options = context.RequestServices.GetRequiredService<IOptions<AsrMvpOptions>>().Value;
    var decoder = context.RequestServices.GetRequiredService<IAudioDecoder>();
    var preprocessor = context.RequestServices.GetRequiredService<IAudioPreprocessor>();
    var classifier = context.RequestServices.GetRequiredService<IAudioClassifier>();
    var qualityChecker = context.RequestServices.GetRequiredService<IAudioQualityChecker>();
    var vad = context.RequestServices.GetRequiredService<IEnergyVad>();
    var endpointing = context.RequestServices.GetRequiredService<EndpointingEngine>();
    var asr = context.RequestServices.GetRequiredService<IStreamingAsrEngine>();
    var agent = context.RequestServices.GetRequiredService<IAgentEngine>();
    var tts = context.RequestServices.GetRequiredService<ITtsEngine>();
    var stabilizer = context.RequestServices.GetRequiredService<TranscriptStabilizer>();
    var postProcessor = context.RequestServices.GetRequiredService<TranscriptPostProcessor>();
    var metrics = context.RequestServices.GetRequiredService<AsrPipelineMetrics>();
    var twoPassRefiner = context.RequestServices.GetRequiredService<ITwoPassRefiner>();
    var resilience = context.RequestServices.GetRequiredService<ResilienceCoordinator>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SttSocket");

    using var socket = await context.WebSockets.AcceptWebSocketAsync();

    var sessionId = Guid.NewGuid().ToString("N");
    var session = new SessionContext(sessionId);
    var runtime = new SessionRuntime();
    sessions[sessionId] = session;
    metrics.OnSessionOpened(sessionId);

    logger.LogInformation("Session {SessionId} connected, profile={RuntimeProfile}", sessionId, options.RuntimeProfile);

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(socket, context.RequestAborted);
            if (message is null)
            {
                break;
            }

            var traceId = CreateTraceId(sessionId);

            if (message.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (message.MessageType == WebSocketMessageType.Text)
            {
                await HandleControlTextAsync(
                    socket,
                    session,
                    runtime,
                    traceId,
                    message.Payload,
                    jsonOptions,
                    asr,
                    agent,
                    tts,
                    postProcessor,
                    metrics,
                    twoPassRefiner,
                    resilience,
                    options,
                    logger,
                    context.RequestAborted);
                continue;
            }

            if (message.MessageType != WebSocketMessageType.Binary)
            {
                await SendErrorAsync(socket, runtime.SendLock, sessionId, traceId, "transport", "UNSUPPORTED_MESSAGE", "Only binary/text is supported", jsonOptions, context.RequestAborted);
                continue;
            }

            if (!decoder.TryDecode(message.Payload, out var pcm, out var decodeError))
            {
                await SendErrorAsync(socket, runtime.SendLock, sessionId, traceId, "decode", decodeError ?? "DECODE_FAIL", "Invalid PCM16 payload", jsonOptions, context.RequestAborted);
                continue;
            }

            var wasInSpeech = session.Endpointing.InSpeech;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var preprocessed = preprocessor.Process(pcm);
            var audioKind = classifier.Classify(preprocessed);

            if (audioKind == AudioKind.NonSpeech && !wasInSpeech)
            {
                continue;
            }

            var chunkMs = Math.Max(1, (int)Math.Round(preprocessed.Samples.Length * 1000.0 / options.SampleRate));
            metrics.OnChunkProcessed(session.SessionId, chunkMs);
            var speech = audioKind == AudioKind.Speech &&
                         qualityChecker.IsAcceptable(preprocessed) &&
                         vad.IsSpeech(preprocessed.Samples);

            var decision = endpointing.Process(session, preprocessed.Samples, speech, chunkMs, nowMs, preprocessed.Rms);

            if (!wasInSpeech && decision.InSpeech)
            {
                session.ActiveSegmentId = session.NextSegmentId();
                await InterruptTtsIfActiveAsync(
                    socket,
                    runtime,
                    metrics,
                    session.SessionId,
                    traceId,
                    reason: "user_speech",
                    jsonOptions,
                    logger,
                    context.RequestAborted);
            }

            if (decision.InSpeech)
            {
                var partialResult = await ExecuteStageWithRetryAsync(
                    stage: "asr",
                    socket,
                    runtime.SendLock,
                    sessionId,
                    traceId,
                    resilience,
                    metrics,
                    jsonOptions,
                    logger,
                    ct => asr.DecodePartialAsync(session.SessionId, session.SnapshotSegment(), ct),
                    context.RequestAborted);

                if (partialResult.Success && !string.IsNullOrWhiteSpace(partialResult.Value))
                {
                    var partialText = stabilizer.Stabilize(session.Transcript.LastPartial, postProcessor.ProcessPartial(partialResult.Value));
                    if (!string.IsNullOrWhiteSpace(partialText) && !string.Equals(partialText, session.Transcript.LastPartial, StringComparison.Ordinal))
                    {
                        session.Transcript.LastPartial = partialText;
                        session.Transcript.LastPartialSentAtMs = nowMs;
                        metrics.OnPartial();

                        var partialMessage = new SttMessage(
                            Type: "stt",
                            State: "partial",
                            Text: partialText,
                            SegmentId: session.ActiveSegmentId ?? "seg-unknown",
                            SessionId: session.SessionId,
                            StartMs: decision.SegmentStartMs,
                            EndMs: decision.SegmentEndMs,
                            TraceId: traceId,
                            LatencyMs: new StageLatencyMs(Stt: partialResult.LatencyMs));

                        await SendJsonAsync(socket, partialMessage, jsonOptions, context.RequestAborted, runtime.SendLock);
                    }
                }
            }

            if (decision.ShouldFinalize && decision.FinalSegmentSamples is { Length: > 0 })
            {
                var finalResult = await ExecuteStageWithRetryAsync(
                    stage: "asr",
                    socket,
                    runtime.SendLock,
                    sessionId,
                    traceId,
                    resilience,
                    metrics,
                    jsonOptions,
                    logger,
                    ct => asr.DecodeFinalAsync(session.SessionId, decision.FinalSegmentSamples, ct),
                    context.RequestAborted);

                if (finalResult.Success && !string.IsNullOrWhiteSpace(finalResult.Value))
                {
                    var finalText = postProcessor.Process(finalResult.Value);
                    if (!string.IsNullOrWhiteSpace(finalText))
                    {
                        var segmentId = session.ActiveSegmentId ?? session.NextSegmentId();
                        if (ShouldRunTwoPass(options, decision.FinalReason, decision.SegmentDurationMs))
                        {
                            finalText = await twoPassRefiner.RefineFinalAsync(
                                session.SessionId,
                                segmentId,
                                finalText,
                                decision.FinalSegmentSamples,
                                options.SampleRate,
                                context.RequestAborted);
                            finalText = postProcessor.Process(finalText);
                        }

                        metrics.OnFinal(session.SessionId);
                        var finalMessage = new SttMessage(
                            Type: "stt",
                            State: "final",
                            Text: finalText,
                            SegmentId: segmentId,
                            SessionId: session.SessionId,
                            StartMs: decision.SegmentStartMs,
                            EndMs: decision.SegmentEndMs,
                            TraceId: traceId,
                            FinalReason: decision.FinalReason,
                            LatencyMs: new StageLatencyMs(Stt: finalResult.LatencyMs));

                        await SendJsonAsync(socket, finalMessage, jsonOptions, context.RequestAborted, runtime.SendLock);
                        logger.LogInformation(
                            "Session {SessionId} segment {SegmentId} finalized reason={FinalReason} profile={EndpointingProfile} trace={TraceId}",
                            session.SessionId,
                            segmentId,
                            decision.FinalReason,
                            decision.EndpointingProfile,
                            traceId);
                        await SendAgentResponseAsync(socket, session, runtime, segmentId, traceId, finalText, agent, tts, resilience, options, jsonOptions, metrics, logger, context.RequestAborted);
                    }
                }

                session.ActiveSegmentId = null;
                session.Transcript.LastPartial = string.Empty;
                session.Transcript.LastPartialSentAtMs = 0;
            }
        }
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("Session {SessionId} canceled", sessionId);
    }
    catch (IOException) when (socket.State is WebSocketState.Aborted or WebSocketState.Closed or WebSocketState.CloseReceived or WebSocketState.CloseSent)
    {
        logger.LogDebug("Session {SessionId} closed by remote endpoint", sessionId);
    }
    catch (ObjectDisposedException)
    {
        logger.LogDebug("Session {SessionId} socket disposed", sessionId);
    }
    catch (WebSocketException ex) when (socket.State is WebSocketState.Aborted or WebSocketState.Closed or WebSocketState.CloseReceived or WebSocketState.CloseSent)
    {
        logger.LogDebug(ex, "Session {SessionId} websocket closed", sessionId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Session {SessionId} failed", sessionId);
        metrics.OnError();
        var traceId = CreateTraceId(sessionId);
        var sent = await SendErrorAsync(socket, runtime.SendLock, sessionId, traceId, "session", "INTERNAL_ERROR", ex.Message, jsonOptions, context.RequestAborted);
        if (!sent)
        {
            logger.LogDebug("Session {SessionId} error response skipped because socket is closed", sessionId);
        }
    }
    finally
    {
        await InterruptTtsIfActiveAsync(socket, runtime, metrics, sessionId, CreateTraceId(sessionId), "session_close", jsonOptions, logger, CancellationToken.None);
        runtime.Dispose();
        sessions.TryRemove(sessionId, out _);
        twoPassRefiner.ResetSession(sessionId);
        metrics.OnSessionClosed(sessionId);
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        logger.LogInformation("Session {SessionId} disconnected", sessionId);
    }
}));

app.Run();

static bool ValidateRuntimeProfile(AsrMvpOptions options)
{
    return options.RuntimeProfile is "dev" or "test" or "prod";
}

static string CreateTraceId(string sessionId)
{
    return $"{sessionId[..Math.Min(8, sessionId.Length)]}-{Guid.NewGuid().ToString("N")[..8]}";
}

static bool ShouldRunTwoPass(AsrMvpOptions options, string finalReason, int segmentDurationMs)
{
    if (!options.TwoPass.Enabled)
    {
        return false;
    }

    var trigger = options.TwoPass.Trigger;
    return finalReason switch
    {
        "listen_stop" => trigger.OnListenStop,
        "max_segment" => trigger.OnMaxSegment,
        _ => trigger.OnEndpointing && segmentDurationMs >= trigger.MinSegmentMsForEndpointing
    };
}

static async Task HandleControlTextAsync(
    WebSocket socket,
    SessionContext session,
    SessionRuntime runtime,
    string traceId,
    byte[] payload,
    JsonSerializerOptions jsonOptions,
    IStreamingAsrEngine asr,
    IAgentEngine agent,
    ITtsEngine tts,
    TranscriptPostProcessor postProcessor,
    AsrPipelineMetrics metrics,
    ITwoPassRefiner twoPassRefiner,
    ResilienceCoordinator resilience,
    AsrMvpOptions options,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var text = Encoding.UTF8.GetString(payload);

    ListenControlMessage? control;
    try
    {
        control = JsonSerializer.Deserialize<ListenControlMessage>(text, jsonOptions);
    }
    catch
    {
        control = null;
    }

    if (control is null || !string.Equals(control.Type, "listen", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    if (!string.Equals(control.State, "stop", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var samples = session.DrainSegment();
    if (samples.Length == 0)
    {
        session.Endpointing.Reset();
        return;
    }

    var finalResult = await ExecuteStageWithRetryAsync(
        stage: "asr",
        socket,
        runtime.SendLock,
        session.SessionId,
        traceId,
        resilience,
        metrics,
        jsonOptions,
        logger,
        ct => asr.DecodeFinalAsync(session.SessionId, samples, ct),
        cancellationToken);

    if (!finalResult.Success || string.IsNullOrWhiteSpace(finalResult.Value))
    {
        session.Endpointing.Reset();
        return;
    }

    var finalText = postProcessor.Process(finalResult.Value);
    if (string.IsNullOrWhiteSpace(finalText))
    {
        session.Endpointing.Reset();
        return;
    }

    var segmentId = session.ActiveSegmentId ?? session.NextSegmentId();
    if (ShouldRunTwoPass(options, "listen_stop", (int)Math.Round(samples.Length * 1000.0 / Math.Max(1, options.SampleRate))))
    {
        finalText = await twoPassRefiner.RefineFinalAsync(
            session.SessionId,
            segmentId,
            finalText,
            samples,
            options.SampleRate,
            cancellationToken);
        finalText = postProcessor.Process(finalText);
    }

    metrics.OnFinal(session.SessionId);

    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var finalMessage = new SttMessage(
        Type: "stt",
        State: "final",
        Text: finalText,
        SegmentId: segmentId,
        SessionId: session.SessionId,
        StartMs: session.Endpointing.SegmentStartMs,
        EndMs: nowMs,
        TraceId: traceId,
        FinalReason: "listen_stop",
        LatencyMs: new StageLatencyMs(Stt: finalResult.LatencyMs));

    await SendJsonAsync(socket, finalMessage, jsonOptions, cancellationToken, runtime.SendLock);
    logger.LogInformation(
        "Session {SessionId} segment {SegmentId} finalized reason=listen_stop trace={TraceId}",
        session.SessionId,
        segmentId,
        traceId);
    await SendAgentResponseAsync(socket, session, runtime, segmentId, traceId, finalText, agent, tts, resilience, options, jsonOptions, metrics, logger, cancellationToken);

    session.Endpointing.Reset();
    session.ActiveSegmentId = null;
    session.Transcript.LastPartial = string.Empty;
    session.Transcript.LastPartialSentAtMs = 0;

    logger.LogDebug("Session {SessionId} finalized by manual stop", session.SessionId);
}

static async Task SendAgentResponseAsync(
    WebSocket socket,
    SessionContext session,
    SessionRuntime runtime,
    string segmentId,
    string traceId,
    string finalText,
    IAgentEngine agent,
    ITtsEngine tts,
    ResilienceCoordinator resilience,
    AsrMvpOptions options,
    JsonSerializerOptions jsonOptions,
    AsrPipelineMetrics metrics,
    ILogger logger,
    CancellationToken cancellationToken)
{
    session.AddUserTurn(finalText);
    var history = session.GetHistoryWindow(options.Agent.MaxHistoryTurns);

    var replyResult = await ExecuteStageWithRetryAsync(
        stage: "agent",
        socket,
        runtime.SendLock,
        session.SessionId,
        traceId,
        resilience,
        metrics,
        jsonOptions,
        logger,
        ct => agent.GenerateReplyAsync(
            session.SessionId,
            options.Agent.SystemPrompt,
            history,
            finalText,
            ct),
        cancellationToken);

    var reply = replyResult.Success ? replyResult.Value : null;
    if (string.IsNullOrWhiteSpace(reply))
    {
        if (!options.Fallback.EnableOnAgentFailure)
        {
            return;
        }

        reply = options.Fallback.AgentFallbackText;
    }

    session.AddAssistantTurn(reply);
    var agentMessage = new AgentMessage(
        Type: "agent",
        State: "response",
        Text: reply,
        SessionId: session.SessionId,
        SegmentId: segmentId);

    await SendJsonAsync(socket, agentMessage, jsonOptions, cancellationToken, runtime.SendLock);
    logger.LogInformation("Session {SessionId} segment {SegmentId} agent response ready trace={TraceId}", session.SessionId, segmentId, traceId);
    StartTtsOutput(
        socket,
        runtime,
        session.SessionId,
        segmentId,
        traceId,
        reply,
        tts,
        resilience,
        options,
        jsonOptions,
        metrics,
        logger,
        cancellationToken);
}

static void StartTtsOutput(
    WebSocket socket,
    SessionRuntime runtime,
    string sessionId,
    string segmentId,
    string traceId,
    string text,
    ITtsEngine tts,
    ResilienceCoordinator resilience,
    AsrMvpOptions options,
    JsonSerializerOptions jsonOptions,
    AsrPipelineMetrics metrics,
    ILogger logger,
    CancellationToken sessionCancellationToken)
{
    lock (runtime.TtsSync)
    {
        runtime.TtsCts?.Cancel();
        runtime.TtsCts?.Dispose();
        runtime.TtsCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken);
        runtime.TtsSegmentId = segmentId;
        Task? taskRef = null;
        taskRef = Task.Run(async () =>
        {
            try
            {
                await SendTtsOutputAsync(
                    socket,
                    runtime.SendLock,
                    sessionId,
                    segmentId,
                    traceId,
                    text,
                    tts,
                    resilience,
                    options,
                    jsonOptions,
                    metrics,
                    logger,
                    runtime.TtsCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Session {SessionId} background TTS task failed", sessionId);
            }
            finally
            {
                lock (runtime.TtsSync)
                {
                    if (ReferenceEquals(runtime.TtsTask, taskRef))
                    {
                        runtime.TtsTask = null;
                        runtime.TtsCts?.Dispose();
                        runtime.TtsCts = null;
                        runtime.TtsSegmentId = null;
                    }
                }
            }
        }, CancellationToken.None);
        runtime.TtsTask = taskRef;
    }
}

static async Task<bool> InterruptTtsIfActiveAsync(
    WebSocket socket,
    SessionRuntime runtime,
    AsrPipelineMetrics metrics,
    string sessionId,
    string traceId,
    string reason,
    JsonSerializerOptions jsonOptions,
    ILogger logger,
    CancellationToken cancellationToken)
{
    Task? runningTask;
    string? segmentId;
    lock (runtime.TtsSync)
    {
        runningTask = runtime.TtsTask;
        if (runningTask is null)
        {
            return false;
        }
        if (runningTask.IsCompleted)
        {
            runtime.TtsTask = null;
            runtime.TtsCts?.Dispose();
            runtime.TtsCts = null;
            runtime.TtsSegmentId = null;
            return false;
        }

        segmentId = runtime.TtsSegmentId;
        runtime.TtsCts?.Cancel();
    }

    try
    {
        await runningTask;
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "Session {SessionId} tts task interrupted with exception", sessionId);
    }

    lock (runtime.TtsSync)
    {
        runtime.TtsTask = null;
        runtime.TtsCts?.Dispose();
        runtime.TtsCts = null;
        runtime.TtsSegmentId = null;
    }

    if (socket.State != WebSocketState.Open)
    {
        return true;
    }

    var interrupt = new InterruptMessage(
        Type: "interrupt",
        State: "stop",
        SessionId: sessionId,
        SegmentId: segmentId ?? "seg-unknown",
        Reason: reason,
        AtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        TraceId: traceId);

    await SendJsonAsync(socket, interrupt, jsonOptions, cancellationToken, runtime.SendLock);
    metrics.OnInterrupt();
    logger.LogInformation("Session {SessionId} tts interrupted reason={Reason} segment={SegmentId} trace={TraceId}", sessionId, reason, segmentId, traceId);
    return true;
}

static async Task SendTtsOutputAsync(
    WebSocket socket,
    SemaphoreSlim sendLock,
    string sessionId,
    string segmentId,
    string traceId,
    string text,
    ITtsEngine tts,
    ResilienceCoordinator resilience,
    AsrMvpOptions options,
    JsonSerializerOptions jsonOptions,
    AsrPipelineMetrics metrics,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var start = new TtsMessage(
        Type: "tts",
        State: "start",
        SessionId: sessionId,
        SegmentId: segmentId,
        SampleRate: options.Tts.SampleRate,
        Sequence: 0,
        TraceId: traceId);
    await SendJsonAsync(socket, start, jsonOptions, cancellationToken, sendLock);

    var seq = 0;
    var stageOptions = resilience.GetStageOptions("tts");

    for (var attempt = 0; attempt <= Math.Max(0, stageOptions.RetryCount); attempt++)
    {
        if (resilience.IsOpen("tts", out var openUntil))
        {
            metrics.OnStageFailure("tts");
            await SendErrorAsync(
                socket,
                sendLock,
                sessionId,
                traceId,
                "tts",
                "TTS_CIRCUIT_OPEN",
                $"circuit open until {openUntil:O}",
                jsonOptions,
                cancellationToken);
            return;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(100, stageOptions.TimeoutMs));

            await foreach (var chunk in tts.SynthesizePcm16Async(sessionId, text, options.Tts.SampleRate, options.Tts.ChunkDurationMs, timeoutCts.Token))
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }

                seq++;
                var sentBinary = await SendBinaryAsync(socket, chunk, cancellationToken, sendLock);
                if (!sentBinary)
                {
                    return;
                }

                var chunkMeta = new TtsMessage(
                    Type: "tts",
                    State: "chunk",
                    SessionId: sessionId,
                    SegmentId: segmentId,
                    SampleRate: options.Tts.SampleRate,
                    Sequence: seq,
                    TraceId: traceId);
                await SendJsonAsync(socket, chunkMeta, jsonOptions, cancellationToken, sendLock);
            }

            resilience.MarkSuccess("tts");
            metrics.OnStageSuccess("tts", seq * Math.Max(1, options.Tts.ChunkDurationMs));
            var stop = new TtsMessage(
                Type: "tts",
                State: "stop",
                SessionId: sessionId,
                SegmentId: segmentId,
                SampleRate: options.Tts.SampleRate,
                Sequence: seq,
                TraceId: traceId);
            await SendJsonAsync(socket, stop, jsonOptions, cancellationToken, sendLock);
            return;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (attempt >= Math.Max(0, stageOptions.RetryCount))
            {
                resilience.MarkFailure("tts");
                metrics.OnStageFailure("tts");
                logger.LogWarning(ex, "Session {SessionId} tts timed out", sessionId);
                await SendErrorAsync(socket, sendLock, sessionId, traceId, "tts", "TTS_TIMEOUT", "TTS timed out", jsonOptions, cancellationToken);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (attempt >= Math.Max(0, stageOptions.RetryCount))
            {
                resilience.MarkFailure("tts");
                metrics.OnStageFailure("tts");
                logger.LogWarning(ex, "Session {SessionId} tts generation failed", sessionId);
                await SendErrorAsync(socket, sendLock, sessionId, traceId, "tts", "TTS_PROVIDER_ERROR", ex.Message, jsonOptions, cancellationToken);
                return;
            }
        }
    }
}

static async Task<(bool Success, T? Value, int LatencyMs)> ExecuteStageWithRetryAsync<T>(
    string stage,
    WebSocket socket,
    SemaphoreSlim sendLock,
    string sessionId,
    string traceId,
    ResilienceCoordinator resilience,
    AsrPipelineMetrics metrics,
    JsonSerializerOptions jsonOptions,
    ILogger logger,
    Func<CancellationToken, Task<T>> run,
    CancellationToken cancellationToken)
{
    if (resilience.IsOpen(stage, out var openUntil))
    {
        metrics.OnStageFailure(stage);
        await SendErrorAsync(
            socket,
            sendLock,
            sessionId,
            traceId,
            stage,
            $"{stage.ToUpperInvariant()}_CIRCUIT_OPEN",
            $"circuit open until {openUntil:O}",
            jsonOptions,
            cancellationToken);
        return (false, default, 0);
    }

    var stageOptions = resilience.GetStageOptions(stage);
    var retry = Math.Max(0, stageOptions.RetryCount);

    for (var attempt = 0; attempt <= retry; attempt++)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(100, stageOptions.TimeoutMs));
            var value = await run(timeoutCts.Token);
            resilience.MarkSuccess(stage);
            var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            metrics.OnStageSuccess(stage, elapsedMs);
            return (true, value, elapsedMs);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (attempt >= retry)
            {
                resilience.MarkFailure(stage);
                metrics.OnStageFailure(stage);
                logger.LogWarning(ex, "Stage {Stage} timed out for session {SessionId}", stage, sessionId);
                await SendErrorAsync(socket, sendLock, sessionId, traceId, stage, $"{stage.ToUpperInvariant()}_TIMEOUT", "request timed out", jsonOptions, cancellationToken);
                return (false, default, (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            if (attempt >= retry)
            {
                resilience.MarkFailure(stage);
                metrics.OnStageFailure(stage);
                logger.LogWarning(ex, "Stage {Stage} provider failure for session {SessionId}", stage, sessionId);
                await SendErrorAsync(socket, sendLock, sessionId, traceId, stage, $"{stage.ToUpperInvariant()}_PROVIDER_ERROR", ex.Message, jsonOptions, cancellationToken);
                return (false, default, (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }
        }
    }

    return (false, default, 0);
}

static async Task<bool> SendErrorAsync(
    WebSocket socket,
    SemaphoreSlim sendLock,
    string sessionId,
    string traceId,
    string stage,
    string code,
    string detail,
    JsonSerializerOptions jsonOptions,
    CancellationToken cancellationToken)
{
    var msg = new SttErrorMessage(
        Type: "stt",
        State: "error",
        SessionId: sessionId,
        TraceId: traceId,
        Error: new ErrorEnvelope(stage, code, detail));

    return await SendJsonAsync(socket, msg, jsonOptions, cancellationToken, sendLock);
}

static async Task<bool> SendJsonAsync(
    WebSocket socket,
    object message,
    JsonSerializerOptions options,
    CancellationToken cancellationToken,
    SemaphoreSlim? sendLock = null)
{
    if (socket.State != WebSocketState.Open)
    {
        return false;
    }

    if (sendLock is not null)
    {
        await sendLock.WaitAsync(cancellationToken);
    }

    try
    {
        if (socket.State != WebSocketState.Open)
        {
            return false;
        }

        var json = JsonSerializer.Serialize(message, options);
        var buffer = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
        return true;
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (IOException)
    {
        return false;
    }
    catch (ObjectDisposedException)
    {
        return false;
    }
    catch (WebSocketException)
    {
        return false;
    }
    finally
    {
        sendLock?.Release();
    }
}

static async Task<bool> SendBinaryAsync(
    WebSocket socket,
    byte[] chunk,
    CancellationToken cancellationToken,
    SemaphoreSlim? sendLock = null)
{
    if (socket.State != WebSocketState.Open)
    {
        return false;
    }

    if (sendLock is not null)
    {
        await sendLock.WaitAsync(cancellationToken);
    }

    try
    {
        if (socket.State != WebSocketState.Open)
        {
            return false;
        }

        await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, cancellationToken);
        return true;
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (IOException)
    {
        return false;
    }
    catch (ObjectDisposedException)
    {
        return false;
    }
    catch (WebSocketException)
    {
        return false;
    }
    finally
    {
        sendLock?.Release();
    }
}

static async Task<SocketMessage?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
{
    var buffer = new byte[16 * 1024];
    using var ms = new MemoryStream();

    WebSocketReceiveResult result;
    do
    {
        try
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (WebSocketException)
        {
            return null;
        }

        if (result.Count > 0)
        {
            ms.Write(buffer, 0, result.Count);
        }
    }
    while (!result.EndOfMessage);

    return new SocketMessage(result.MessageType, ms.ToArray());
}

file sealed record SocketMessage(WebSocketMessageType MessageType, byte[] Payload);

file sealed class SessionRuntime : IDisposable
{
    public SemaphoreSlim SendLock { get; } = new(1, 1);
    public object TtsSync { get; } = new();
    public Task? TtsTask { get; set; }
    public CancellationTokenSource? TtsCts { get; set; }
    public string? TtsSegmentId { get; set; }

    public void Dispose()
    {
        lock (TtsSync)
        {
            TtsCts?.Dispose();
            TtsCts = null;
            TtsTask = null;
            TtsSegmentId = null;
        }

        SendLock.Dispose();
    }
}
