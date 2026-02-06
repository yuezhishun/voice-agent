namespace VoiceAgent.AsrMvp.Services;

public sealed class BasicAudioQualityChecker : IAudioQualityChecker
{
    public bool IsAcceptable(AudioPreprocessResult frame)
    {
        // Reject extremely tiny frames and clipping-heavy audio.
        if (frame.Samples.Length < 160)
        {
            return false;
        }

        if (frame.Rms < 0.003f)
        {
            return false;
        }

        if (frame.Clipping && frame.Peak >= 0.999f)
        {
            return false;
        }

        return true;
    }
}
