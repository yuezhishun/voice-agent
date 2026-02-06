namespace VoiceAgent.AsrMvp.Services;

public sealed class BasicAudioClassifier : IAudioClassifier
{
    public AudioKind Classify(AudioPreprocessResult frame)
    {
        // MVP heuristic: low RMS means silence/background.
        if (frame.Rms < 0.008f)
        {
            return AudioKind.NonSpeech;
        }

        return AudioKind.Speech;
    }
}
