using DeepSeekHarness.Desktop;

namespace DeepSeekHarness.Desktop.Tests;

public sealed class RuntimeLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dsh-desktop-runtime-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesThePackagedSiblingRuntime()
    {
        var app = Path.Combine(_root, "app");
        var node = Path.Combine(app, "runtime", "node", "node.exe");
        var entry = Path.Combine(app, "runtime", "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "desktop-bin.js");
        Directory.CreateDirectory(Path.GetDirectoryName(node)!);
        Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
        File.WriteAllText(node, "fixture");
        File.WriteAllText(entry, "fixture");

        var layout = RuntimeLocator.Resolve(app, null, _ => null);

        Assert.Equal(node, layout.NodePath);
        Assert.Equal(entry, layout.ServerEntryPath);
        Assert.True(layout.IsPackaged);
    }

    [Fact]
    public void EnvironmentOverrideFailsLoudWhenIncomplete()
    {
        var overrideRoot = Path.Combine(_root, "missing-runtime");
        Directory.CreateDirectory(overrideRoot);

        var error = Assert.Throws<DesktopStartupException>(
            () => RuntimeLocator.Resolve(_root, overrideRoot, _ => null));

        Assert.Contains("node.exe", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("desktop-bin.js", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
