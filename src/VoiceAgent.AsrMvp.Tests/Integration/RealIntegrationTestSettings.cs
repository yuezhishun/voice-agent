using System.Text.Json;

namespace VoiceAgent.AsrMvp.Tests.Integration;

internal static class RealIntegrationTestSettings
{
    private static readonly Lazy<Settings> Cached = new(LoadInternal);

    public static Settings Load() => Cached.Value;

    private static Settings LoadInternal()
    {
        var repoRoot = ResolveRepoRoot();
        var localFile = Path.Combine(repoRoot, "src", "VoiceAgent.AsrMvp.Tests", "real-integration.settings.json");
        var templateFile = Path.Combine(repoRoot, "src", "VoiceAgent.AsrMvp.Tests", "real-integration.settings.json.example");

        var path = File.Exists(localFile) ? localFile : templateFile;
        if (!File.Exists(path))
        {
            return new Settings();
        }

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("RealIntegration", out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return new Settings();
        }

        var settings = new Settings
        {
            Enabled = GetBool(section, "Enabled", false),
            FunAsrWebSocketUrl = ResolvePathIfNeeded(repoRoot, GetString(section, "FunAsrWebSocketUrl")),
            RealWavDir = ResolvePathIfNeeded(repoRoot, GetString(section, "RealWavDir")),
            E2eWavFile = ResolvePathIfNeeded(repoRoot, GetString(section, "E2eWavFile")),
            ParaformerModelDir = ResolvePathIfNeeded(repoRoot, GetString(section, "ParaformerModelDir")),
            KokoroModelDir = ResolvePathIfNeeded(repoRoot, GetString(section, "KokoroModelDir")),
            SenseVoiceModelDir = ResolvePathIfNeeded(repoRoot, GetString(section, "SenseVoiceModelDir")),
            KokoroLang = GetString(section, "KokoroLang"),
            KokoroLexicon = GetString(section, "KokoroLexicon")
        };

        if (section.TryGetProperty("Glm", out var glmSection) && glmSection.ValueKind == JsonValueKind.Object)
        {
            settings.Glm = new GlmSettings
            {
                BaseUrl = GetString(glmSection, "BaseUrl"),
                ApiKey = GetString(glmSection, "ApiKey"),
                Model = GetString(glmSection, "Model")
            };
        }

        return settings;
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ResolvePathIfNeeded(string repoRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return value;
        }

        return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(repoRoot, value));
    }

    private static string GetString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String)
        {
            return node.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool GetBool(JsonElement root, string name, bool defaultValue)
    {
        if (root.TryGetProperty(name, out var node) &&
            (node.ValueKind == JsonValueKind.True || node.ValueKind == JsonValueKind.False))
        {
            return node.GetBoolean();
        }

        return defaultValue;
    }

    internal sealed class Settings
    {
        public bool Enabled { get; set; }
        public string FunAsrWebSocketUrl { get; set; } = string.Empty;
        public string RealWavDir { get; set; } = string.Empty;
        public string E2eWavFile { get; set; } = string.Empty;
        public string ParaformerModelDir { get; set; } = string.Empty;
        public string KokoroModelDir { get; set; } = string.Empty;
        public string SenseVoiceModelDir { get; set; } = string.Empty;
        public string KokoroLang { get; set; } = "zh";
        public string KokoroLexicon { get; set; } = "lexicon-zh.txt";
        public GlmSettings Glm { get; set; } = new();
    }

    internal sealed class GlmSettings
    {
        public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4/";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "glm-4.7";
    }
}
