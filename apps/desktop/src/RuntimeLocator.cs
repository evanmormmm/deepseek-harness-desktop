namespace DeepSeekHarness.Desktop;

/// <summary>Executable paths for the Node carrier and desktop server bridge.</summary>
internal sealed record RuntimeLayout(
    string NodePath,
    string ServerEntryPath,
    string RootPath,
    bool IsPackaged);

/// <summary>Resolves the packaged runtime first and a source checkout second.</summary>
internal static class RuntimeLocator
{
    /// <summary>Resolve a complete desktop runtime.</summary>
    /// <param name="applicationDirectory">Directory containing the desktop executable.</param>
    /// <param name="explicitRoot">Optional runtime root from <c>--runtime</c>.</param>
    /// <param name="environment">Environment lookup used for overrides and tests.</param>
    /// <returns>The validated runtime layout.</returns>
    internal static RuntimeLayout Resolve(
        string applicationDirectory,
        string? explicitRoot,
        Func<string, string?> environment)
    {
        var overrideRoot = explicitRoot ?? environment("DSH_DESKTOP_RUNTIME");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return ValidatePackaged(Path.GetFullPath(overrideRoot), "指定的桌面运行时");
        }

        var packagedRoot = Path.Combine(Path.GetFullPath(applicationDirectory), "runtime");
        if (HasPackagedFiles(packagedRoot)) return CreatePackaged(packagedRoot);

        var sourceRoot = FindSourceRoot(applicationDirectory) ?? FindSourceRoot(Environment.CurrentDirectory);
        if (sourceRoot is null)
        {
            throw new DesktopStartupException(
                $"找不到 Harness 运行时。请把 runtime 文件夹放在桌面程序旁边，或设置 DSH_DESKTOP_RUNTIME。应用目录：{applicationDirectory}");
        }

        var nodePath = environment("DSH_DESKTOP_NODE");
        if (string.IsNullOrWhiteSpace(nodePath)) nodePath = FindOnPath("node.exe", environment("PATH"));
        if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(nodePath))
        {
            throw new DesktopStartupException("源码模式找不到 node.exe。请安装 Node.js，或设置 DSH_DESKTOP_NODE。 ");
        }

        var entry = Path.Combine(sourceRoot, "apps", "cli", "lib", "desktop-bin.js");
        return new RuntimeLayout(Path.GetFullPath(nodePath), entry, sourceRoot, false);
    }

    private static RuntimeLayout ValidatePackaged(string root, string label)
    {
        var node = Path.Combine(root, "node", "node.exe");
        var entry = Path.Combine(root, "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "desktop-bin.js");
        var missing = new List<string>();
        if (!File.Exists(node)) missing.Add(node);
        if (!File.Exists(entry)) missing.Add(entry);
        if (missing.Count > 0)
        {
            throw new DesktopStartupException($"{label}不完整，缺少：{string.Join("；", missing)}");
        }

        return new RuntimeLayout(node, entry, root, true);
    }

    private static bool HasPackagedFiles(string root) =>
        File.Exists(Path.Combine(root, "node", "node.exe"))
        && File.Exists(Path.Combine(root, "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "desktop-bin.js"));

    private static RuntimeLayout CreatePackaged(string root) => new(
        Path.Combine(root, "node", "node.exe"),
        Path.Combine(root, "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "desktop-bin.js"),
        root,
        true);

    private static string? FindSourceRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            var entry = Path.Combine(directory.FullName, "apps", "cli", "lib", "desktop-bin.js");
            var manifest = Path.Combine(directory.FullName, "pnpm-workspace.yaml");
            if (File.Exists(entry) && File.Exists(manifest)) return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindOnPath(string filename, string? pathValue)
    {
        foreach (var directory in (pathValue ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), filename);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (Exception) when (directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                // Ignore one malformed ambient PATH entry and keep searching.
            }
        }

        return null;
    }
}
