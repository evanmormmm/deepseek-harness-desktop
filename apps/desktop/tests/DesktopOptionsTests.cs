using DeepSeekHarness.Desktop;

namespace DeepSeekHarness.Desktop.Tests;

public sealed class DesktopOptionsTests
{
    [Fact]
    public void ParsesWorkspaceAndSmokeResultAsAbsolutePaths()
    {
        var workspace = Path.GetFullPath("fixture-workspace");
        var result = Path.GetFullPath("smoke.json");

        var options = DesktopOptions.Parse(["--workspace", workspace, "--smoke-result", result]);

        Assert.Equal(workspace, options.WorkspacePath);
        Assert.Equal(result, options.SmokeResultPath);
        Assert.True(options.IsSmokeTest);
        Assert.False(options.ShutdownRequested);
        Assert.False(options.CleanupRuntimeRequested);
    }

    [Fact]
    public void RejectsMissingFlagValues()
    {
        Assert.Throws<DesktopStartupException>(() => DesktopOptions.Parse(["--workspace"]));
    }

    [Fact]
    public void ParsesShutdownAsAControlOnlyRequest()
    {
        var options = DesktopOptions.Parse(["--shutdown"]);

        Assert.True(options.ShutdownRequested);
        Assert.False(options.IsSmokeTest);
        Assert.False(options.CleanupRuntimeRequested);
    }

    [Fact]
    public void RejectsShutdownCombinedWithRuntimeArguments()
    {
        Assert.Throws<DesktopStartupException>(() =>
            DesktopOptions.Parse(["--shutdown", "--workspace", Path.GetFullPath("fixture-workspace")]));
    }

    [Fact]
    public void ParsesRuntimeCleanupAsAControlOnlyRequest()
    {
        var options = DesktopOptions.Parse(["--cleanup-runtime"]);

        Assert.True(options.CleanupRuntimeRequested);
        Assert.False(options.ShutdownRequested);
    }

    [Fact]
    public void RejectsCombinedControlRequests()
    {
        Assert.Throws<DesktopStartupException>(() =>
            DesktopOptions.Parse(["--shutdown", "--cleanup-runtime"]));
    }
}
