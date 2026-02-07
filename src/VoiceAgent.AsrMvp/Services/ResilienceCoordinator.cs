using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class ResilienceCoordinator
{
    private sealed class StageState
    {
        public int ConsecutiveFailures;
        public DateTimeOffset? OpenUntil;
    }

    private readonly ResilienceOptions _options;
    private readonly ConcurrentDictionary<string, StageState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ResilienceCoordinator(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.Resilience;
    }

    public bool IsOpen(string stage, out DateTimeOffset? openUntil)
    {
        openUntil = null;
        if (!_states.TryGetValue(stage, out var state))
        {
            return false;
        }

        if (state.OpenUntil is null)
        {
            return false;
        }

        if (state.OpenUntil <= DateTimeOffset.UtcNow)
        {
            state.OpenUntil = null;
            return false;
        }

        openUntil = state.OpenUntil;
        return true;
    }

    public StageResilienceOptions GetStageOptions(string stage)
    {
        return stage.ToLowerInvariant() switch
        {
            "asr" => _options.Asr,
            "agent" => _options.Agent,
            "tts" => _options.Tts,
            _ => new StageResilienceOptions()
        };
    }

    public void MarkSuccess(string stage)
    {
        var state = _states.GetOrAdd(stage, _ => new StageState());
        state.ConsecutiveFailures = 0;
        state.OpenUntil = null;
    }

    public void MarkFailure(string stage)
    {
        var stageOptions = GetStageOptions(stage);
        var state = _states.GetOrAdd(stage, _ => new StageState());
        state.ConsecutiveFailures++;
        if (stageOptions.CircuitBreakFailures > 0 && state.ConsecutiveFailures >= stageOptions.CircuitBreakFailures)
        {
            state.OpenUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, stageOptions.CircuitBreakWindowSeconds));
            state.ConsecutiveFailures = 0;
        }
    }
}
