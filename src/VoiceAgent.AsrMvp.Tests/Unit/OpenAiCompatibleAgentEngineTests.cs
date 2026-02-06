using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Domain;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class OpenAiCompatibleAgentEngineTests
{
    [Fact]
    public async Task GenerateReplyAsync_ReturnsText_WhenApiSucceeds()
    {
        const string json = """
                            {
                              "choices": [
                                {
                                  "message": {
                                    "role": "assistant",
                                    "content": "你好，我是 GLM"
                                  }
                                }
                              ]
                            }
                            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        var engine = CreateEngine(handler);

        var reply = await engine.GenerateReplyAsync(
            "s1",
            "system",
            [new AgentTurn("user", "早上好")],
            "早上好",
            CancellationToken.None);

        Assert.Equal("你好，我是 GLM", reply);
    }

    [Fact]
    public async Task GenerateReplyAsync_Throws_WhenApiFails()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent("{\"error\":\"invalid_request\"}", Encoding.UTF8, "application/json")
            });
        var engine = CreateEngine(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.GenerateReplyAsync(
            "s1",
            "system",
            [],
            "hello",
            CancellationToken.None));

        Assert.Contains("400", ex.Message, StringComparison.Ordinal);
    }

    private static OpenAiCompatibleAgentEngine CreateEngine(HttpMessageHandler handler)
    {
        var options = Options.Create(new AsrMvpOptions
        {
            Agent = new AgentOptions
            {
                Provider = "openai",
                OpenAiCompatible = new OpenAiCompatibleAgentOptions
                {
                    BaseUrl = "https://open.bigmodel.cn/api/paas/v4/",
                    ApiKey = "test-key",
                    Model = "glm-4.7"
                }
            }
        });

        return new OpenAiCompatibleAgentEngine(new StubHttpClientFactory(handler), options);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler);
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
