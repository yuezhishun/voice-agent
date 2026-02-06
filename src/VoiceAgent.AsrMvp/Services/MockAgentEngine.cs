using VoiceAgent.AsrMvp.Domain;

namespace VoiceAgent.AsrMvp.Services;

public sealed class MockAgentEngine : IAgentEngine
{
    public Task<string> GenerateReplyAsync(
        string sessionId,
        string systemPrompt,
        IReadOnlyList<AgentTurn> history,
        string userText,
        CancellationToken cancellationToken)
    {
        var reply = $"收到：{userText}";
        return Task.FromResult(reply);
    }
}
