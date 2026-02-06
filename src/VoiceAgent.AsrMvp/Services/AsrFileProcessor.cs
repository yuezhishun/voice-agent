using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Domain;
using VoiceAgent.AsrMvp.Pipeline;
using VoiceAgent.AsrMvp.Utils;

namespace VoiceAgent.AsrMvp.Services;

public sealed class AsrFileProcessor
{
    private readonly AsrMvpOptions _options;
    private readonly IAudioPreprocessor _preprocessor;
    private readonly IAudioClassifier _classifier;
    private readonly IAudioQualityChecker _qualityChecker;
    private readonly IEnergyVad _vad;
    private readonly EndpointingEngine _endpointing;
    private readonly IStreamingAsrEngine _asr;

    public AsrFileProcessor(
        IOptions<AsrMvpOptions> options,
        IAudioPreprocessor preprocessor,
        IAudioClassifier classifier,
        IAudioQualityChecker qualityChecker,
        IEnergyVad vad,
        EndpointingEngine endpointing,
        IStreamingAsrEngine asr)
    {
        _options = options.Value;
        _preprocessor = preprocessor;
        _classifier = classifier;
        _qualityChecker = qualityChecker;
        _vad = vad;
        _endpointing = endpointing;
        _asr = asr;
    }

    public async Task<IReadOnlyList<string>> ProcessWavFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var (samples, sampleRate, channels) = WavFileReader.ReadPcm16(filePath);
        if (sampleRate != _options.SampleRate)
        {
            throw new InvalidOperationException($"Expected sample rate {_options.SampleRate}, got {sampleRate}");
        }

        var mono = channels == 1 ? samples : ToMono(samples, channels);
        var chunkSize = Math.Max(1, _options.SampleRate * _options.ChunkMs / 1000);

        var session = new SessionContext(Path.GetFileNameWithoutExtension(filePath));
        var finals = new List<string>();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (var i = 0; i < mono.Length; i += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var len = Math.Min(chunkSize, mono.Length - i);
            var chunk = new float[len];
            Array.Copy(mono, i, chunk, 0, len);

            var pre = _preprocessor.Process(chunk);
            var kind = _classifier.Classify(pre);
            var inSpeechBefore = session.Endpointing.InSpeech;

            if (kind == AudioKind.NonSpeech && !inSpeechBefore)
            {
                nowMs += _options.ChunkMs;
                continue;
            }

            var speech = kind == AudioKind.Speech && _qualityChecker.IsAcceptable(pre) && _vad.IsSpeech(pre.Samples);
            var decision = _endpointing.Process(session, pre.Samples, speech, _options.ChunkMs, nowMs);

            if (decision.ShouldFinalize && decision.FinalSegmentSamples is { Length: > 0 })
            {
                var text = await _asr.DecodeFinalAsync(session.SessionId, decision.FinalSegmentSamples, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    finals.Add(text);
                }
            }

            nowMs += _options.ChunkMs;
        }

        if (session.Endpointing.InSpeech)
        {
            var tail = session.DrainSegment();
            if (tail.Length > 0)
            {
                var text = await _asr.DecodeFinalAsync(session.SessionId, tail, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    finals.Add(text);
                }
            }
        }

        return finals;
    }

    private static float[] ToMono(float[] interleaved, int channels)
    {
        var frames = interleaved.Length / channels;
        var mono = new float[frames];

        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[(i * channels) + c];
            }

            mono[i] = sum / channels;
        }

        return mono;
    }
}
