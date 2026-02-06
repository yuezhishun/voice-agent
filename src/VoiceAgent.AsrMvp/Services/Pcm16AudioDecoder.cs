namespace VoiceAgent.AsrMvp.Services;

public sealed class Pcm16AudioDecoder : IAudioDecoder
{
    public bool TryDecode(byte[] payload, out float[] pcm, out string? errorCode)
    {
        errorCode = null;
        if (payload.Length < 2 || (payload.Length % 2) != 0)
        {
            pcm = Array.Empty<float>();
            errorCode = "DECODE_FAIL";
            return false;
        }

        var sampleCount = payload.Length / 2;
        pcm = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            short sample = (short)(payload[i * 2] | (payload[(i * 2) + 1] << 8));
            pcm[i] = sample / 32768f;
        }

        return true;
    }
}
