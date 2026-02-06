using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class EnergyVad : IEnergyVad
{
    private readonly float _threshold;

    public EnergyVad(IOptions<AsrMvpOptions> options)
    {
        _threshold = options.Value.Endpointing.EnergyThreshold;
    }

    public bool IsSpeech(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return false;
        }

        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        var rms = Math.Sqrt(sum / samples.Length);
        return rms >= _threshold;
    }
}
