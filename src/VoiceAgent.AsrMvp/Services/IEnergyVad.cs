namespace VoiceAgent.AsrMvp.Services;

public interface IEnergyVad
{
    bool IsSpeech(ReadOnlySpan<float> samples);
}
