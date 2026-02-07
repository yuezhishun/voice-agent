using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Pipeline;

public sealed class TranscriptStabilizer
{
    private readonly bool _enabled;
    private readonly int _tailRollbackChars;
    private readonly int _minFrozenPrefixChars;
    private readonly int _maxTailRewriteChars;

    public TranscriptStabilizer(IOptions<AsrMvpOptions> options)
    {
        _enabled = options.Value.TranscriptStability.Enabled;
        // MVP approximation: 1s roughly maps to ~4 Chinese chars in conversational speed.
        _tailRollbackChars = Math.Max(8, options.Value.TailRollbackSeconds * 4);
        _minFrozenPrefixChars = Math.Max(0, options.Value.TranscriptStability.MinFrozenPrefixChars);
        _maxTailRewriteChars = Math.Max(1, options.Value.TranscriptStability.MaxTailRewriteChars);
    }

    public string Stabilize(string previous, string current)
    {
        if (!_enabled)
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(previous))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return previous;
        }

        var frozenPrefixLength = Math.Max(_minFrozenPrefixChars, previous.Length - _tailRollbackChars);
        var frozenPrefix = previous[..frozenPrefixLength];

        if (current.StartsWith(frozenPrefix, StringComparison.Ordinal))
        {
            return current;
        }

        var common = LongestCommonPrefix(previous, current);
        var rewrittenTailChars = (previous.Length - common) + (current.Length - common);
        if (rewrittenTailChars > _maxTailRewriteChars)
        {
            return previous;
        }

        if (common >= frozenPrefixLength)
        {
            return current;
        }

        var suffix = current.Length > common ? current[common..] : string.Empty;
        return frozenPrefix + suffix;
    }

    private static int LongestCommonPrefix(string left, string right)
    {
        var max = Math.Min(left.Length, right.Length);
        var i = 0;
        while (i < max && left[i] == right[i])
        {
            i++;
        }

        return i;
    }
}
