using System.Buffers.Binary;

namespace VoiceAgent.AsrMvp.Utils;

public static class WavFileReader
{
    public static (float[] Samples, int SampleRate, int Channels) ReadPcm16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44)
        {
            throw new InvalidDataException("WAV file too small");
        }

        static string Tag(byte[] b, int start) => System.Text.Encoding.ASCII.GetString(b, start, 4);

        if (Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE")
        {
            throw new InvalidDataException("Invalid WAV header");
        }

        int offset = 12;
        short audioFormat = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        int dataOffset = -1;
        int dataSize = 0;

        while (offset + 8 <= bytes.Length)
        {
            var chunkId = Tag(bytes, offset);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;

            if (offset + chunkSize > bytes.Length)
            {
                break;
            }

            if (chunkId == "fmt ")
            {
                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
                channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 14, 2));
            }
            else if (chunkId == "data")
            {
                dataOffset = offset;
                dataSize = chunkSize;
                break;
            }

            offset += chunkSize;
        }

        if (audioFormat != 1 || bitsPerSample != 16 || dataOffset < 0)
        {
            throw new InvalidDataException("Only PCM16 WAV is supported");
        }

        var samples = new float[dataSize / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            short s = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(dataOffset + (i * 2), 2));
            samples[i] = s / 32768f;
        }

        return (samples, sampleRate, channels);
    }
}
