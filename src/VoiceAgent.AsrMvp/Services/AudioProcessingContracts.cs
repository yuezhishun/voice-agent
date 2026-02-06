namespace VoiceAgent.AsrMvp.Services;

public enum AudioKind
{
    Speech,
    NonSpeech
}

public readonly record struct AudioPreprocessResult(
    float[] Samples,
    float Rms,
    float Peak,
    bool Clipping);

public interface IAudioPreprocessor
{
    AudioPreprocessResult Process(ReadOnlySpan<float> samples);
}

public interface IAudioClassifier
{
    AudioKind Classify(AudioPreprocessResult frame);
}

public interface IAudioQualityChecker
{
    bool IsAcceptable(AudioPreprocessResult frame);
}
