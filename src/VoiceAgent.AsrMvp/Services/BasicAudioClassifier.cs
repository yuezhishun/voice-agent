namespace VoiceAgent.AsrMvp.Services;

public sealed class BasicAudioClassifier : IAudioClassifier
{
    public AudioKind Classify(AudioPreprocessResult frame)
    {
        if (frame.Samples.Length == 0 || frame.Rms < 0.006f)
        {
            return AudioKind.NonSpeech;
        }

        var zcr = ZeroCrossingRate(frame.Samples);
        var crest = frame.Rms > 1e-6f ? frame.Peak / frame.Rms : 0f;

        // Music/noise usually has much denser zero-crossing in short frame.
        if (zcr > 0.28f && crest < 3.2f)
        {
            return AudioKind.NonSpeech;
        }

        // Transient-like noise spikes.
        if (crest > 12f && zcr > 0.2f)
        {
            return AudioKind.NonSpeech;
        }

        return AudioKind.Speech;
    }

    private static float ZeroCrossingRate(float[] samples)
    {
        if (samples.Length < 2)
        {
            return 0;
        }

        var count = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] >= 0 && samples[i] < 0) || (samples[i - 1] < 0 && samples[i] >= 0))
            {
                count++;
            }
        }

        return (float)count / (samples.Length - 1);
    }
}
