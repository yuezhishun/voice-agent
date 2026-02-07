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

    public EndpointingDecision Process(SessionContext session, ReadOnlySpan<float> samples, bool speech, int chunkMs, long nowMs, float frameRms = 0f)
    {
        var state = session.Endpointing;
        UpdateProfile(state, speech, frameRms);
        var profile = GetActiveProfile(state);

        if (!state.InSpeech && !speech)
        {
            return new EndpointingDecision
            {
                ShouldAppendAudio = false,
                ShouldFinalize = false,
                InSpeech = false,
                SegmentDurationMs = 0,
                EndpointingProfile = state.ActiveProfile
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
                if (state.PendingFinalize && state.PendingFinalizeMs <= profile.MergeBackMs)
                {
                    state.PendingFinalize = false;
                    state.PendingFinalizeMs = 0;
                }
            }
            else
            {
                state.SilenceMs += chunkMs;
                if (state.SpeechMs >= _options.MinSegmentMs && state.SilenceMs >= profile.MinSilenceMs)
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
            else if (state.PendingFinalize && state.SilenceMs >= profile.EndSilenceMs)
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
                SegmentEndMs = nowMs,
                TrailingSilenceMs = state.SilenceMs,
                EndpointingProfile = state.ActiveProfile
            };
        }

        session.AppendSamples(samples);
        var segment = session.DrainSegment();
        var duration = state.SegmentDurationMs;
        var startMs = state.SegmentStartMs;
        var endMs = nowMs;
        var finalReason = duration >= _options.MaxSegmentMs ? "max_segment" : "endpointing";
        var trailingSilenceMs = state.SilenceMs;
        var finalProfile = state.ActiveProfile;

        state.Reset();

        return new EndpointingDecision
        {
            ShouldAppendAudio = false,
            ShouldFinalize = true,
            InSpeech = false,
            SegmentDurationMs = duration,
            SegmentStartMs = startMs,
            SegmentEndMs = endMs,
            TrailingSilenceMs = trailingSilenceMs,
            FinalReason = finalReason,
            EndpointingProfile = finalProfile,
            FinalSegmentSamples = segment
        };
    }

    private void UpdateProfile(EndpointingState state, bool speech, float frameRms)
    {
        if (!_options.DynamicProfileEnabled)
        {
            state.ActiveProfile = "quiet";
            return;
        }

        var noisy = !speech && frameRms >= _options.NoisyFrameRmsThreshold;
        var target = noisy ? 1f : 0f;
        var alpha = Math.Clamp(_options.NoisyScoreAlpha, 0f, 0.99f);
        state.NoiseScore = (alpha * state.NoiseScore) + ((1f - alpha) * target);
        state.ActiveProfile = state.NoiseScore >= _options.NoisyScoreThreshold ? "noisy" : "quiet";
    }

    private EndpointingProfileOptions GetActiveProfile(EndpointingState state)
    {
        return string.Equals(state.ActiveProfile, "noisy", StringComparison.OrdinalIgnoreCase)
            ? _options.NoisyProfile
            : _options.QuietProfile;
    }
}
