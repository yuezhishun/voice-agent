namespace VoiceAgent.AsrMvp.Services;

public interface IAudioDecoder
{
    bool TryDecode(byte[] payload, out float[] pcm, out string? errorCode);
}
