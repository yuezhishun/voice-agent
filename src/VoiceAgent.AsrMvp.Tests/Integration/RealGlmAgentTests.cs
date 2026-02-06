using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Integration;

public sealed class RealGlmAgentTests
{
    [Fact]
    public async Task OpenAiCompatibleAgent_CanCallRealGlm_WhenConfigured()
    {
        var apiKey = Environment.GetEnvironmentVariable("GLM_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var options = Options.Create(new AsrMvpOptions
        {
            Agent = new AgentOptions
            {
                Provider = "openai",
                TimeoutMs = 15000,
                SystemPrompt = "You are a concise assistant. Reply in one short sentence.",
                OpenAiCompatible = new OpenAiCompatibleAgentOptions
                {
                    BaseUrl = Environment.GetEnvironmentVariable("GLM_API_BASE_URL") ?? "https://open.bigmodel.cn/api/paas/v4/",
                    ApiKey = apiKey,
                    Model = Environment.GetEnvironmentVariable("GLM_MODEL") ?? "glm-4.7",
                    Temperature = 0.2f,
                    MaxTokens = 128
                }
            }
        });

        var engine = new OpenAiCompatibleAgentEngine(new DefaultHttpClientFactory(), options);
        var text = await engine.GenerateReplyAsync(
            sessionId: Guid.NewGuid().ToString("N"),
            systemPrompt: options.Value.Agent.SystemPrompt,
            history: [],
            userText: "请回复：连接成功",
            cancellationToken: CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
