using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Pipeline;

public sealed class TranscriptStabilizer
{
    private readonly int _tailRollbackChars;

    public TranscriptStabilizer(IOptions<AsrMvpOptions> options)
    {
        // MVP approximation: 1s roughly maps to ~4 Chinese chars in conversational speed.
        _tailRollbackChars = Math.Max(8, options.Value.TailRollbackSeconds * 4);
    }

    public string Stabilize(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return previous;
        }

        var frozenPrefixLength = Math.Max(0, previous.Length - _tailRollbackChars);
        var frozenPrefix = previous[..frozenPrefixLength];

        if (current.StartsWith(frozenPrefix, StringComparison.Ordinal))
        {
            return current;
        }

        var common = LongestCommonPrefix(previous, current);
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
