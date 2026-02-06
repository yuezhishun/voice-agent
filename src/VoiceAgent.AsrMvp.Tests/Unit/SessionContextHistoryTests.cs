using VoiceAgent.AsrMvp.Domain;

namespace VoiceAgent.AsrMvp.Tests.Unit;

public sealed class SessionContextHistoryTests
{
    [Fact]
    public void ReturnsRecentWindowInOrder()
    {
        var session = new SessionContext("s-history");
        session.AddUserTurn("u1");
        session.AddAssistantTurn("a1");
        session.AddUserTurn("u2");
        session.AddAssistantTurn("a2");

        var window = session.GetHistoryWindow(2);

        Assert.Equal(2, window.Count);
        Assert.Equal("u2", window[0].Text);
        Assert.Equal("a2", window[1].Text);
    }
}
