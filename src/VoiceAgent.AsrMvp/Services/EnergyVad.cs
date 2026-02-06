using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class EnergyVad : IEnergyVad
{
    private readonly EndpointingOptions _options;
    private readonly Queue<float> _history = new();
    private readonly object _lock = new();
    private float _adaptiveNoise = 0.004f;

    public EnergyVad(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.Endpointing;
        _adaptiveNoise = Math.Max(1e-5f, _options.AdaptiveVadFloor);
    }

    public bool IsSpeech(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return false;
        }

        var rms = ComputeRms(samples);
        var threshold = _options.EnergyThreshold;
        if (_options.AdaptiveVadEnabled)
        {
            threshold = UpdateAndGetAdaptiveThreshold(rms);
        }

        return rms >= threshold;
    }

    private float UpdateAndGetAdaptiveThreshold(float rms)
    {
        lock (_lock)
        {
            _history.Enqueue(rms);
            while (_history.Count > Math.Max(4, _options.AdaptiveVadWindowFrames))
            {
                _history.Dequeue();
            }

            var min = float.MaxValue;
            foreach (var e in _history)
            {
                if (e < min)
                {
                    min = e;
                }
            }

            if (min == float.MaxValue)
            {
                min = _adaptiveNoise;
            }

            _adaptiveNoise = (0.98f * _adaptiveNoise) + (0.02f * min);
            var adaptive = Math.Max(_options.AdaptiveVadFloor, _adaptiveNoise * _options.AdaptiveVadMultiplier);
            return Math.Max(_options.EnergyThreshold * 0.5f, adaptive);
        }
    }

    private static float ComputeRms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }
}
