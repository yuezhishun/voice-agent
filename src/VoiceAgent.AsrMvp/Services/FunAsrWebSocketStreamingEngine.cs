using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class FunAsrWebSocketStreamingEngine : IStreamingAsrEngine, IDisposable
{
    private sealed class StreamState
    {
        public ClientWebSocket Socket { get; set; } = new();
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public int SentSamples { get; set; }
        public string LastText { get; set; } = string.Empty;
        public bool Initialized { get; set; }
    }

    private readonly AsrMvpOptions _options;
    private readonly ConcurrentDictionary<string, StreamState> _streams = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FunAsrWebSocketStreamingEngine(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> DecodePartialAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        var stream = _streams.GetOrAdd(sessionId, _ => new StreamState());
        await stream.Lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(stream, sessionId, cancellationToken);
            await SendDeltaAudioAsync(stream, samples, cancellationToken);
            await ReceiveLoopAsync(stream, sessionId, waitForFinal: false, cancellationToken);
            return stream.LastText;
        }
        finally
        {
            stream.Lock.Release();
        }
    }

    public async Task<string> DecodeFinalAsync(string sessionId, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        var stream = _streams.GetOrAdd(sessionId, _ => new StreamState());
        await stream.Lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(stream, sessionId, cancellationToken);
            await SendDeltaAudioAsync(stream, samples, cancellationToken);
            await SendJsonAsync(stream.Socket, new Dictionary<string, object> { ["is_speaking"] = false }, cancellationToken);
            await ReceiveLoopAsync(stream, sessionId, waitForFinal: true, cancellationToken);
            return stream.LastText;
        }
        finally
        {
            stream.Lock.Release();
            await CloseAndRemoveAsync(sessionId);
        }
    }

    private async Task EnsureConnectedAsync(StreamState state, string sessionId, CancellationToken cancellationToken)
    {
        if (state.Socket.State == WebSocketState.Open && state.Initialized)
        {
            return;
        }

        if (state.Socket.State != WebSocketState.None)
        {
            try
            {
                state.Socket.Abort();
                state.Socket.Dispose();
            }
            catch
            {
            }
        }

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(_options.FunAsrWebSocket.Url), cancellationToken);

        state.SentSamples = 0;
        state.LastText = string.Empty;
        state.Initialized = true;

        var control = new Dictionary<string, object>
        {
            ["mode"] = _options.FunAsrWebSocket.Mode,
            ["chunk_size"] = _options.FunAsrWebSocket.ChunkSize,
            ["chunk_interval"] = _options.FunAsrWebSocket.ChunkInterval,
            ["encoder_chunk_look_back"] = _options.FunAsrWebSocket.EncoderChunkLookBack,
            ["decoder_chunk_look_back"] = _options.FunAsrWebSocket.DecoderChunkLookBack,
            ["audio_fs"] = _options.SampleRate,
            ["wav_name"] = sessionId,
            ["wav_format"] = "pcm",
            ["is_speaking"] = true,
            ["itn"] = _options.FunAsrWebSocket.Itn
        };

        await SendJsonAsync(socket, control, cancellationToken);
        state.Socket = socket;
    }

    private async Task SendDeltaAudioAsync(StreamState stream, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        if (stream.Socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("FunASR websocket is not open");
        }

        if (samples.Length <= stream.SentSamples)
        {
            return;
        }

        var delta = samples.Span[stream.SentSamples..];
        var bytes = new byte[delta.Length * 2];
        for (var i = 0; i < delta.Length; i++)
        {
            var s = (short)Math.Clamp((int)(delta[i] * 32767), short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[(i * 2) + 1] = (byte)((s >> 8) & 0xff);
        }

        await stream.Socket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancellationToken);
        stream.SentSamples = samples.Length;
    }

    private async Task ReceiveLoopAsync(StreamState stream, string sessionId, bool waitForFinal, CancellationToken cancellationToken)
    {
        var timeout = waitForFinal ? _options.FunAsrWebSocket.FinalTimeoutMs : _options.FunAsrWebSocket.ReceiveTimeoutMs;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);

        while (DateTime.UtcNow < deadline)
        {
            var left = deadline - DateTime.UtcNow;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(left);

            string? message;
            try
            {
                message = await ReceiveTextAsync(stream.Socket, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                if (!waitForFinal)
                {
                    break;
                }

                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    stream.LastText = text;
                }

                var isFinal = root.TryGetProperty("is_final", out var f) && f.ValueKind == JsonValueKind.True;
                var mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                if (waitForFinal && (isFinal || mode.Contains("offline", StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }
            catch
            {
                // Ignore malformed server payloads and keep waiting.
            }
        }
    }

    private async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return null;
        }

        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                ms.Write(buffer, 0, result.Count);
            }
        }
        while (!result.EndOfMessage);

        return result.MessageType == WebSocketMessageType.Text
            ? Encoding.UTF8.GetString(ms.ToArray())
            : null;
    }

    private async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task CloseAndRemoveAsync(string sessionId)
    {
        if (!_streams.TryRemove(sessionId, out var stream))
        {
            return;
        }

        try
        {
            if (stream.Socket.State == WebSocketState.Open || stream.Socket.State == WebSocketState.CloseReceived)
            {
                await stream.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch
        {
        }
        finally
        {
            stream.Socket.Dispose();
            stream.Lock.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var kv in _streams)
        {
            try
            {
                kv.Value.Socket.Abort();
                kv.Value.Socket.Dispose();
                kv.Value.Lock.Dispose();
            }
            catch
            {
            }
        }

        _streams.Clear();
    }
}
