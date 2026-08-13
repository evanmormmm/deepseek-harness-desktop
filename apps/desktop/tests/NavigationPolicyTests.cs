using DeepSeekHarness.Desktop;

namespace DeepSeekHarness.Desktop.Tests;

public sealed class NavigationPolicyTests
{
    private static readonly Uri HarnessOrigin = new("http://127.0.0.1:43125/");

    [Theory]
    [InlineData("http://127.0.0.1:43125/")]
    [InlineData("http://127.0.0.1:43125/session/abc?x=1")]
    [InlineData("about:blank")]
    public void AllowsOnlyHarnessDocuments(string target)
    {
        Assert.Equal(NavigationDisposition.Allow, NavigationPolicy.Classify(HarnessOrigin, new Uri(target)));
    }

    [Theory]
    [InlineData("https://deepseek.com/")]
    [InlineData("mailto:support@example.com")]
    public void SendsOrdinaryExternalLinksToTheSystem(string target)
    {
        Assert.Equal(NavigationDisposition.OpenExternal, NavigationPolicy.Classify(HarnessOrigin, new Uri(target)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:43126/")]
    [InlineData("http://localhost:43125/")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("data:text/html,hello")]
    [InlineData("javascript:alert(1)")]
    public void BlocksOtherEmbeddedNavigation(string target)
    {
        Assert.Equal(NavigationDisposition.Block, NavigationPolicy.Classify(HarnessOrigin, new Uri(target)));
    }
}
