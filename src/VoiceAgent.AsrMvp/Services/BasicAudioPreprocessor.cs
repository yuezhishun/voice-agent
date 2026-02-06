using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class BasicAudioPreprocessor : IAudioPreprocessor
{
    private readonly AudioProcessingOptions _options;
    private float _noiseFloor = 0.004f;

    public BasicAudioPreprocessor()
    {
        _options = new AudioProcessingOptions();
    }

    public BasicAudioPreprocessor(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.AudioProcessing;
        _noiseFloor = Math.Max(1e-5f, options.Value.Endpointing.AdaptiveVadFloor);
    }

    public AudioPreprocessResult Process(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return new AudioPreprocessResult(Array.Empty<float>(), 0, 0, false);
        }

        var output = samples.ToArray();
        if (_options.EnableDcRemoval)
        {
            RemoveDc(output);
        }

        if (_options.EnablePreEmphasis)
        {
            ApplyPreEmphasis(output, _options.PreEmphasis);
        }

        if (_options.EnableDenoise)
        {
            SuppressNoise(output);
        }

        if (_options.EnableAgc)
        {
            ApplyAgc(output);
        }

        return ComputeMetrics(output);
    }

    private static void RemoveDc(float[] samples)
    {
        double mean = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            mean += samples[i];
        }

        mean /= samples.Length;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] -= (float)mean;
        }
    }

    private static void ApplyPreEmphasis(float[] samples, float factor)
    {
        var prev = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var current = samples[i];
            samples[i] = current - factor * prev;
            prev = current;
        }
    }

    private void SuppressNoise(float[] samples)
    {
        var rms = ComputeRms(samples);
        _noiseFloor = (_options.NoiseFloorAlpha * _noiseFloor) + ((1f - _options.NoiseFloorAlpha) * rms);
        var gate = _noiseFloor * _options.NoiseSuppressStrength;

        for (var i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            var abs = Math.Abs(s);
            if (abs <= gate)
            {
                samples[i] = 0;
                continue;
            }

            samples[i] = MathF.Sign(s) * (abs - gate);
        }
    }

    private void ApplyAgc(float[] samples)
    {
        var rms = ComputeRms(samples);
        if (rms < 1e-6f)
        {
            return;
        }

        var gain = _options.AgcTargetRms / rms;
        gain = Math.Clamp(gain, 1f / _options.AgcMaxGain, _options.AgcMaxGain);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }

    private static AudioPreprocessResult ComputeMetrics(float[] samples)
    {
        double sumSq = 0;
        float peak = 0;
        var clipping = false;
        for (var i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            if (s > 1f)
            {
                s = 1f;
                clipping = true;
            }
            else if (s < -1f)
            {
                s = -1f;
                clipping = true;
            }

            samples[i] = s;
            var abs = Math.Abs(s);
            if (abs > peak)
            {
                peak = abs;
            }

            sumSq += s * s;
        }

        var rms = (float)Math.Sqrt(sumSq / samples.Length);
        return new AudioPreprocessResult(samples, rms, peak, clipping);
    }

    private static float ComputeRms(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        double sumSq = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sumSq += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sumSq / samples.Length);
    }
}
