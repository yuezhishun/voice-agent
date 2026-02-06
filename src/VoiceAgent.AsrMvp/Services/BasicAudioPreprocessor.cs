namespace VoiceAgent.AsrMvp.Services;

public sealed class BasicAudioPreprocessor : IAudioPreprocessor
{
    public AudioPreprocessResult Process(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return new AudioPreprocessResult(Array.Empty<float>(), 0, 0, false);
        }

        var output = new float[samples.Length];

        double mean = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            mean += samples[i];
        }

        mean /= samples.Length;

        double sumSq = 0;
        float peak = 0;
        var clipping = false;

        for (var i = 0; i < samples.Length; i++)
        {
            // Basic DC offset removal + mild gain normalization.
            var s = (float)(samples[i] - mean);
            s *= 1.2f;

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

            output[i] = s;
            var abs = Math.Abs(s);
            if (abs > peak)
            {
                peak = abs;
            }

            sumSq += s * s;
        }

        var rms = (float)Math.Sqrt(sumSq / output.Length);
        return new AudioPreprocessResult(output, rms, peak, clipping);
    }
}
