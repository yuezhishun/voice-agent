using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;

namespace VoiceAgent.AsrMvp.Services;

public sealed class TranscriptPostProcessor
{
    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

    private readonly PostProcessOptions _options;

    public TranscriptPostProcessor(IOptions<AsrMvpOptions> options)
    {
        _options = options.Value.PostProcess;
    }

    public string Process(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = input;
        if (_options.EnableNormalization)
        {
            text = Normalize(text);
        }

        if (_options.EnablePunctuation)
        {
            text = RestorePunctuation(text);
        }

        return text;
    }

    public string ProcessPartial(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return _options.EnableNormalization ? Normalize(input) : input.Trim();
    }

    private static string Normalize(string text)
    {
        text = text.Replace('\u3000', ' ');
        text = MultiSpace.Replace(text, " ");
        return text.Trim();
    }

    private static string RestorePunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var tail = trimmed[^1];
        if (tail is '。' or '！' or '？' or '.' or '!' or '?')
        {
            return trimmed;
        }

        // Very small heuristic for MVP:
        if (trimmed.StartsWith("请问", StringComparison.Ordinal) ||
            trimmed.StartsWith("能否", StringComparison.Ordinal) ||
            trimmed.Contains("吗", StringComparison.Ordinal) ||
            trimmed.Contains("么", StringComparison.Ordinal) ||
            trimmed.Contains("是否", StringComparison.Ordinal))
        {
            return trimmed + "？";
        }

        return trimmed + "。";
    }
}
