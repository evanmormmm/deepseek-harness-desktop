namespace DeepSeekHarness.Desktop;

/// <summary>Removes only the packaged runtime after the owned backend has reached quiescence.</summary>
internal static class RuntimeCleanup
{
    internal static void DeletePackagedRuntime(string applicationDirectory, DesktopLogger logger)
    {
        var application = Path.GetFullPath(applicationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var runtime = Path.GetFullPath(Path.Combine(application, "runtime"));
        var expectedPrefix = application + Path.DirectorySeparatorChar;
        if (!runtime.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopStartupException($"拒绝清理应用目录之外的运行时：{runtime}");
        }
        if (!Directory.Exists(runtime)) return;

        logger.Write("desktop", $"removing packaged runtime at {runtime}");
        NormalizeAttributes(runtime);
        Directory.Delete(runtime, recursive: true);
        if (Directory.Exists(runtime))
        {
            throw new DesktopStartupException($"桌面运行时未完全删除：{runtime}");
        }
        logger.Write("desktop", "packaged runtime removed");
    }

    private static void NormalizeAttributes(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
