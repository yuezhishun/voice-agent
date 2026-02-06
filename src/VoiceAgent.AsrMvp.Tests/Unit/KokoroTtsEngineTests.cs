using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoiceAgent.AsrMvp.Config;
using VoiceAgent.AsrMvp.Services;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class KokoroTtsEngineTests
{
    [Fact]
    public void Ctor_Throws_WhenModelDirectoryDoesNotExist()
    {
        var options = Options.Create(new AsrMvpOptions
        {
            Tts = new TtsOptions
            {
                Provider = "kokoro",
                Kokoro = new KokoroTtsOptions
                {
                    ModelDir = Path.Combine(Path.GetTempPath(), "kokoro-not-exists")
                }
            }
        });

        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            new KokoroTtsEngine(options, NullLogger<KokoroTtsEngine>.Instance));

        Assert.Contains("Kokoro model directory not found", ex.Message, StringComparison.Ordinal);
    }
}
