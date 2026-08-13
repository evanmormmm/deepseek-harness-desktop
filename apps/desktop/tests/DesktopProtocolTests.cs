using DeepSeekHarness.Desktop;

namespace DeepSeekHarness.Desktop.Tests;

public sealed class DesktopProtocolTests
{
    [Theory]
    [InlineData("DSH_DESKTOP_READY {\"url\":\"http://127.0.0.1:3080/\",\"pid\":42}", 3080, 42)]
    [InlineData("DSH_DESKTOP_READY {\"url\":\"http://127.0.0.1:49152\",\"pid\":100}", 49152, 100)]
    public void ParsesAuthenticatedLoopbackReadiness(string line, int port, int pid)
    {
        Assert.True(DesktopProtocol.TryParseReady(line, out var ready));
        Assert.NotNull(ready);
        Assert.Equal(port, ready.Url.Port);
        Assert.Equal(pid, ready.ProcessId);
    }

    [Theory]
    [InlineData("dsh web: http://127.0.0.1:3080")]
    [InlineData("DSH_DESKTOP_READY {\"url\":\"https://127.0.0.1:3080\",\"pid\":42}")]
    [InlineData("DSH_DESKTOP_READY {\"url\":\"http://localhost:3080\",\"pid\":42}")]
    [InlineData("DSH_DESKTOP_READY {\"url\":\"http://127.0.0.1:0\",\"pid\":42}")]
    [InlineData("DSH_DESKTOP_READY {\"url\":\"http://127.0.0.1:3080@evil.example\",\"pid\":42}")]
    [InlineData("DSH_DESKTOP_READY not-json")]
    public void RejectsUntrustedOrMalformedReadiness(string line)
    {
        Assert.False(DesktopProtocol.TryParseReady(line, out _));
    }
}
