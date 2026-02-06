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

builder.Services.Configure<AsrMvpOptions>(builder.Configuration.GetSection(AsrMvpOptions.SectionName));
builder.Services.AddSingleton<IAudioDecoder, Pcm16AudioDecoder>();
builder.Services.AddSingleton<IEnergyVad, EnergyVad>();
builder.Services.AddSingleton<IStreamingAsrEngine, MockParaformerStreamingEngine>();
builder.Services.AddSingleton<EndpointingEngine>();
builder.Services.AddSingleton<TranscriptStabilizer>();

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

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Map("/ws/stt", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required");
        return;
    }

    var options = context.RequestServices.GetRequiredService<IOptions<AsrMvpOptions>>().Value;
    var decoder = context.RequestServices.GetRequiredService<IAudioDecoder>();
    var vad = context.RequestServices.GetRequiredService<IEnergyVad>();
    var endpointing = context.RequestServices.GetRequiredService<EndpointingEngine>();
    var asr = context.RequestServices.GetRequiredService<IStreamingAsrEngine>();
    var stabilizer = context.RequestServices.GetRequiredService<TranscriptStabilizer>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SttSocket");

    using var socket = await context.WebSockets.AcceptWebSocketAsync();

    var sessionId = Guid.NewGuid().ToString("N");
    var session = new SessionContext(sessionId);
    sessions[sessionId] = session;

    logger.LogInformation("Session {SessionId} connected", sessionId);

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(socket, context.RequestAborted);
            if (message is null)
            {
                break;
            }

            if (message.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (message.MessageType == WebSocketMessageType.Text)
            {
                await HandleControlTextAsync(socket, session, message.Payload, jsonOptions, asr, logger, context.RequestAborted);
                continue;
            }

            if (message.MessageType != WebSocketMessageType.Binary)
            {
                await SendErrorAsync(socket, sessionId, "UNSUPPORTED_MESSAGE", "Only binary/text is supported", jsonOptions, context.RequestAborted);
                continue;
            }

            if (!decoder.TryDecode(message.Payload, out var pcm, out var decodeError))
            {
                await SendErrorAsync(socket, sessionId, decodeError ?? "DECODE_FAIL", "Invalid PCM16 payload", jsonOptions, context.RequestAborted);
                continue;
            }

            var wasInSpeech = session.Endpointing.InSpeech;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var chunkMs = Math.Max(1, (int)Math.Round(pcm.Length * 1000.0 / options.SampleRate));
            var speech = vad.IsSpeech(pcm);

            var decision = endpointing.Process(session, pcm, speech, chunkMs, nowMs);

            if (!wasInSpeech && decision.InSpeech)
            {
                session.ActiveSegmentId = session.NextSegmentId();
            }

            if (decision.InSpeech)
            {
                var partialTextRaw = await asr.DecodePartialAsync(session.SessionId, session.SnapshotSegment(), context.RequestAborted);
                var partialText = stabilizer.Stabilize(session.Transcript.LastPartial, partialTextRaw);

                if (!string.IsNullOrWhiteSpace(partialText) && !string.Equals(partialText, session.Transcript.LastPartial, StringComparison.Ordinal))
                {
                    session.Transcript.LastPartial = partialText;
                    session.Transcript.LastPartialSentAtMs = nowMs;

                    var partialMessage = new SttMessage(
                        Type: "stt",
                        State: "partial",
                        Text: partialText,
                        SegmentId: session.ActiveSegmentId ?? "seg-unknown",
                        SessionId: session.SessionId,
                        StartMs: decision.SegmentStartMs,
                        EndMs: decision.SegmentEndMs);

                    await SendJsonAsync(socket, partialMessage, jsonOptions, context.RequestAborted);
                }
            }

            if (decision.ShouldFinalize && decision.FinalSegmentSamples is { Length: > 0 })
            {
                var finalText = await asr.DecodeFinalAsync(session.SessionId, decision.FinalSegmentSamples, context.RequestAborted);

                if (!string.IsNullOrWhiteSpace(finalText))
                {
                    var finalMessage = new SttMessage(
                        Type: "stt",
                        State: "final",
                        Text: finalText,
                        SegmentId: session.ActiveSegmentId ?? session.NextSegmentId(),
                        SessionId: session.SessionId,
                        StartMs: decision.SegmentStartMs,
                        EndMs: decision.SegmentEndMs);

                    await SendJsonAsync(socket, finalMessage, jsonOptions, context.RequestAborted);
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
        await SendErrorAsync(socket, sessionId, "INTERNAL_ERROR", ex.Message, jsonOptions, context.RequestAborted);
    }
    finally
    {
        sessions.TryRemove(sessionId, out _);
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Remote endpoint may already be closed.
            }
            catch (ObjectDisposedException)
            {
                // Test host websocket can be disposed by the client side.
            }
        }

        logger.LogInformation("Session {SessionId} disconnected", sessionId);
    }
});

app.Run();

static async Task HandleControlTextAsync(
    WebSocket socket,
    SessionContext session,
    byte[] payload,
    JsonSerializerOptions jsonOptions,
    IStreamingAsrEngine asr,
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

    var finalText = await asr.DecodeFinalAsync(session.SessionId, samples, cancellationToken);
    if (string.IsNullOrWhiteSpace(finalText))
    {
        session.Endpointing.Reset();
        return;
    }

    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var finalMessage = new SttMessage(
        Type: "stt",
        State: "final",
        Text: finalText,
        SegmentId: session.ActiveSegmentId ?? session.NextSegmentId(),
        SessionId: session.SessionId,
        StartMs: session.Endpointing.SegmentStartMs,
        EndMs: nowMs);

    await SendJsonAsync(socket, finalMessage, jsonOptions, cancellationToken);

    session.Endpointing.Reset();
    session.ActiveSegmentId = null;
    session.Transcript.LastPartial = string.Empty;
    session.Transcript.LastPartialSentAtMs = 0;

    logger.LogDebug("Session {SessionId} finalized by manual stop", session.SessionId);
}

static async Task SendErrorAsync(
    WebSocket socket,
    string sessionId,
    string code,
    string detail,
    JsonSerializerOptions jsonOptions,
    CancellationToken cancellationToken)
{
    var msg = new SttErrorMessage(
        Type: "stt",
        State: "error",
        Code: code,
        SessionId: sessionId,
        Detail: detail);

    await SendJsonAsync(socket, msg, jsonOptions, cancellationToken);
}

static async Task SendJsonAsync(WebSocket socket, object message, JsonSerializerOptions options, CancellationToken cancellationToken)
{
    if (socket.State != WebSocketState.Open)
    {
        return;
    }

    var json = JsonSerializer.Serialize(message, options);
    var buffer = Encoding.UTF8.GetBytes(json);
    await socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
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
