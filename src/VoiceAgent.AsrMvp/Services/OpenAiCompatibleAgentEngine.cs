using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Domain;

namespace VoiceAgent.AsrMvp.Services;

public sealed class OpenAiCompatibleAgentEngine : IAgentEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiCompatibleAgentOptions _options;

    public OpenAiCompatibleAgentEngine(IHttpClientFactory httpClientFactory, IOptions<AsrMvpOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.Agent.OpenAiCompatible;
    }

    public async Task<string> GenerateReplyAsync(
        string sessionId,
        string systemPrompt,
        IReadOnlyList<AgentTurn> history,
        string userText,
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey(_options);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Agent API key is not configured. Set AsrMvp:Agent:OpenAiCompatible:ApiKey or GLM_API_KEY.");
        }

        var baseUrl = ResolveBaseUrl(_options);
        var requestUri = BuildChatCompletionsUri(baseUrl);
        var model = ResolveModel(_options);
        var messages = BuildMessages(systemPrompt, history, userText);
        var payload = new ChatCompletionRequest(model, messages, _options.Temperature, _options.MaxTokens);

        using var client = _httpClientFactory.CreateClient(nameof(OpenAiCompatibleAgentEngine));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Agent request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
        }

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions);
        var text = NormalizeContent(parsed?.Choices?.FirstOrDefault()?.Message?.Content);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Agent returned empty response content.");
        }

        return text;
    }

    private static string ResolveApiKey(OpenAiCompatibleAgentOptions options)
    {
        var env = Environment.GetEnvironmentVariable("GLM_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return options.ApiKey;
    }

    private static string ResolveBaseUrl(OpenAiCompatibleAgentOptions options)
    {
        var env = Environment.GetEnvironmentVariable("GLM_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return options.BaseUrl;
    }

    private static string ResolveModel(OpenAiCompatibleAgentOptions options)
    {
        var env = Environment.GetEnvironmentVariable("GLM_MODEL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return options.Model;
    }

    private static string BuildChatCompletionsUri(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return $"{normalized}/chat/completions";
    }

    private static List<ChatMessage> BuildMessages(string systemPrompt, IReadOnlyList<AgentTurn> history, string userText)
    {
        var messages = new List<ChatMessage>(history.Count + 2)
        {
            new("system", systemPrompt)
        };

        foreach (var turn in history)
        {
            if (string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(new ChatMessage(turn.Role.ToLowerInvariant(), turn.Text));
            }
        }

        if (!messages.Any(x => x.Role == "user" && string.Equals(x.Content, userText, StringComparison.Ordinal)))
        {
            messages.Add(new ChatMessage("user", userText));
        }

        return messages;
    }

    private static string NormalizeContent(JsonElement? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        var value = content.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            // Compatible with multimodal array content: [{ "type":"text","text":"..." }, ...]
            var parts = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("text", out var textNode) &&
                    textNode.ValueKind == JsonValueKind.String)
                {
                    var part = textNode.GetString();
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        parts.Add(part);
                    }
                }
            }

            return string.Join("\n", parts);
        }

        return string.Empty;
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        float Temperature,
        int? MaxTokens);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<Choice>? Choices);
    private sealed record Choice(ChatMessageResponse? Message);
    private sealed record ChatMessageResponse(string? Role, JsonElement? Content);
}
