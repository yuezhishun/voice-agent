using VoiceAgent.AsrMvp.Domain;

namespace VoiceAgent.AsrMvp.Services;

public interface IAgentEngine
{
    Task<string> GenerateReplyAsync(
        string sessionId,
        string systemPrompt,
        IReadOnlyList<AgentTurn> history,
        string userText,
        CancellationToken cancellationToken);
}
