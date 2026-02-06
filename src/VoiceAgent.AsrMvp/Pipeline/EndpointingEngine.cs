using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Domain;

namespace VoiceAgent.AsrMvp.Pipeline;

public sealed class EndpointingEngine
{
    private readonly EndpointingOptions _options;

    public EndpointingEngine(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.Endpointing;
    }

    public EndpointingDecision Process(SessionContext session, ReadOnlySpan<float> samples, bool speech, int chunkMs, long nowMs)
    {
        var state = session.Endpointing;

        if (!state.InSpeech && !speech)
        {
            return new EndpointingDecision
            {
                ShouldAppendAudio = false,
                ShouldFinalize = false,
                InSpeech = false,
                SegmentDurationMs = 0
            };
        }

        if (!state.InSpeech && speech)
        {
            state.InSpeech = true;
            state.PendingFinalize = false;
            state.SegmentDurationMs = 0;
            state.SpeechMs = 0;
            state.SilenceMs = 0;
            state.PendingFinalizeMs = 0;
            state.SegmentStartMs = nowMs;
        }

        if (state.InSpeech)
        {
            state.SegmentDurationMs += chunkMs;
            if (speech)
            {
                state.SpeechMs += chunkMs;
                state.SilenceMs = 0;
                if (state.PendingFinalize && state.PendingFinalizeMs <= _options.MergeBackMs)
                {
                    state.PendingFinalize = false;
                    state.PendingFinalizeMs = 0;
                }
            }
            else
            {
                state.SilenceMs += chunkMs;
                if (state.SpeechMs >= _options.MinSegmentMs && state.SilenceMs >= _options.MinSilenceMs)
                {
                    state.PendingFinalize = true;
                    state.PendingFinalizeMs += chunkMs;
                }
            }
        }

        var shouldFinalize = false;
        if (state.InSpeech)
        {
            if (state.SegmentDurationMs >= _options.MaxSegmentMs)
            {
                shouldFinalize = true;
            }
            else if (state.PendingFinalize && state.SilenceMs >= _options.EndSilenceMs)
            {
                shouldFinalize = true;
            }
        }

        if (!shouldFinalize)
        {
            session.AppendSamples(samples);
            return new EndpointingDecision
            {
                ShouldAppendAudio = true,
                ShouldFinalize = false,
                InSpeech = state.InSpeech,
                SegmentDurationMs = state.SegmentDurationMs,
                SegmentStartMs = state.SegmentStartMs,
                SegmentEndMs = nowMs
            };
        }

        session.AppendSamples(samples);
        var segment = session.DrainSegment();
        var duration = state.SegmentDurationMs;
        var startMs = state.SegmentStartMs;
        var endMs = nowMs;

        state.Reset();

        return new EndpointingDecision
        {
            ShouldAppendAudio = false,
            ShouldFinalize = true,
            InSpeech = false,
            SegmentDurationMs = duration,
            SegmentStartMs = startMs,
            SegmentEndMs = endMs,
            FinalSegmentSamples = segment
        };
    }
}
