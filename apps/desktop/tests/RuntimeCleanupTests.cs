using DeepSeekHarness.Desktop;

namespace DeepSeekHarness.Desktop.Tests;

public sealed class RuntimeCleanupTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), $"dsh-runtime-cleanup-{Guid.NewGuid():N}");

    [Fact]
    public void DeletesOnlyTheRuntimeDirectory()
    {
        var runtimeFile = Path.Combine(_fixture, "runtime", "harness", "deep", "package.js");
        var sibling = Path.Combine(_fixture, "desktop-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeFile)!);
        File.WriteAllText(runtimeFile, "export {};");
        File.WriteAllText(sibling, "{}");
        using var logger = new DesktopLogger();

        RuntimeCleanup.DeletePackagedRuntime(_fixture, logger);

        Assert.False(Directory.Exists(Path.Combine(_fixture, "runtime")));
        Assert.True(File.Exists(sibling));
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }
}
