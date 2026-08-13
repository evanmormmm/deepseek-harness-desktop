namespace DeepSeekHarness.Desktop;

/// <summary>Validated process arguments for one desktop invocation.</summary>
internal sealed record DesktopOptions(
    string WorkspacePath,
    string? RuntimeRoot,
    string? SmokeResultPath,
    bool ShutdownRequested,
    bool CleanupRuntimeRequested)
{
    internal bool IsSmokeTest => SmokeResultPath is not null;

    /// <summary>Parse desktop-only arguments without forwarding them to Harness.</summary>
    /// <param name="args">Raw application arguments.</param>
    /// <returns>Normalized desktop options.</returns>
    internal static DesktopOptions Parse(string[] args)
    {
        string? workspace = null;
        string? runtime = null;
        string? smokeResult = null;
        var shutdownRequested = false;
        var cleanupRuntimeRequested = false;

        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (name == "--shutdown")
            {
                if (shutdownRequested) throw new DesktopStartupException("参数 --shutdown 不能重复。");
                shutdownRequested = true;
                continue;
            }
            if (name == "--cleanup-runtime")
            {
                if (cleanupRuntimeRequested) throw new DesktopStartupException("参数 --cleanup-runtime 不能重复。");
                cleanupRuntimeRequested = true;
                continue;
            }

            if (name is not ("--workspace" or "--runtime" or "--smoke-result"))
            {
                throw new DesktopStartupException($"未知桌面端参数：{name}");
            }

            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new DesktopStartupException($"参数 {name} 缺少值。");
            }

            var value = Path.GetFullPath(args[index]);
            switch (name)
            {
                case "--workspace":
                    workspace = value;
                    break;
                case "--runtime":
                    runtime = value;
                    break;
                case "--smoke-result":
                    smokeResult = value;
                    break;
            }
        }

        if ((shutdownRequested || cleanupRuntimeRequested)
            && (workspace is not null || runtime is not null || smokeResult is not null
                || shutdownRequested && cleanupRuntimeRequested))
        {
            throw new DesktopStartupException("桌面端控制参数不能重复或与其他参数同时使用。");
        }

        workspace ??= ResolveDefaultWorkspace();
        return new DesktopOptions(workspace, runtime, smokeResult, shutdownRequested, cleanupRuntimeRequested);
    }

    private static string ResolveDefaultWorkspace()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_DESKTOP_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) return Path.GetFullPath(profile);

        return Path.GetFullPath(Environment.CurrentDirectory);
    }
}
