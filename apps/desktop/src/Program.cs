namespace DeepSeekHarness.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        DesktopOptions options;
        try
        {
            options = DesktopOptions.Parse(args);
        }
        catch (DesktopStartupException error)
        {
            MessageBox.Show(error.Message, "DeepSeek Harness", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 2;
            return;
        }

        using var logger = new DesktopLogger();
        logger.Write("desktop", $"launch version={Application.ProductVersion}, base={AppContext.BaseDirectory}");
        SingleInstance? instance = null;
        try
        {
            if (!options.IsSmokeTest)
            {
                instance = SingleInstance.Create();
                if (options.ShutdownRequested || options.CleanupRuntimeRequested)
                {
                    if (!instance.SignalShutdown(TimeSpan.FromSeconds(20)))
                    {
                        logger.Write("desktop", "shutdown control timed out waiting for the primary instance");
                        Environment.ExitCode = 3;
                    }
                    else if (options.CleanupRuntimeRequested)
                    {
                        RuntimeCleanup.DeletePackagedRuntime(AppContext.BaseDirectory, logger);
                    }
                    return;
                }
                if (!instance.IsPrimary)
                {
                    instance.SignalActivation();
                    return;
                }
            }

            var runtime = RuntimeLocator.Resolve(AppContext.BaseDirectory, options.RuntimeRoot, Environment.GetEnvironmentVariable);
            using var form = new MainForm(options, runtime, logger);
            instance?.Attach(form, form.ActivateFromSecondary, form.RequestClose);
            Application.Run(form);
            Environment.ExitCode = form.ExitCode;
        }
        catch (Exception error)
        {
            logger.Write("desktop", $"fatal: {error}");
            if (options.SmokeResultPath is not null)
            {
                new SmokeResult(false, false, false, null, null, error.ToString()).Write(options.SmokeResultPath);
            }
            else
            {
                MessageBox.Show(
                    $"DeepSeek Harness 桌面端启动失败。\r\n\r\n{error.Message}\r\n\r\n日志：{logger.LogPath}",
                    "DeepSeek Harness",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            instance?.Dispose();
        }
    }
}
