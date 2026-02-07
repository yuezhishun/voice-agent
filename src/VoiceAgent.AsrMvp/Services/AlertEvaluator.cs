using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed record AlertItem(
    string Rule,
    string Severity,
    string Message,
    string? Stage = null);

public sealed class AlertEvaluator
{
    private readonly AlertsOptions _options;

    public AlertEvaluator(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.Alerts;
    }

    public IReadOnlyList<AlertItem> Evaluate(MetricsSnapshot metrics, IReadOnlyList<DependencyHealthStatus> health)
    {
        if (!_options.Enabled)
        {
            return Array.Empty<AlertItem>();
        }

        var alerts = new List<AlertItem>();

        foreach (var stage in metrics.Stages)
        {
            if (stage.ErrorRatePercent >= _options.StageErrorRateWarnPercent)
            {
                alerts.Add(new AlertItem(
                    Rule: "stage_error_rate",
                    Severity: "warning",
                    Message: $"{stage.Stage} error rate {stage.ErrorRatePercent:F2}% >= {_options.StageErrorRateWarnPercent:F2}%",
                    Stage: stage.Stage));
            }

            var latencyThreshold = stage.Stage.ToLowerInvariant() switch
            {
                "asr" => _options.AsrP95WarnMs,
                "agent" => _options.AgentP95WarnMs,
                "tts" => _options.TtsP95WarnMs,
                _ => int.MaxValue
            };

            if (latencyThreshold < int.MaxValue && stage.P95LatencyMs >= latencyThreshold)
            {
                alerts.Add(new AlertItem(
                    Rule: "stage_latency_p95",
                    Severity: "warning",
                    Message: $"{stage.Stage} p95 {stage.P95LatencyMs}ms >= {latencyThreshold}ms",
                    Stage: stage.Stage));
            }
        }

        foreach (var dep in health.Where(x => !x.Healthy))
        {
            alerts.Add(new AlertItem(
                Rule: "dependency_unhealthy",
                Severity: "critical",
                Message: $"{dep.Stage}/{dep.Provider} unhealthy: {dep.Detail}",
                Stage: dep.Stage));
        }

        return alerts;
    }
}
