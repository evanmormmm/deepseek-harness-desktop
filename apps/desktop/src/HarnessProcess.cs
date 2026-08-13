using System.Diagnostics;
using System.Net;
using System.Text;

namespace DeepSeekHarness.Desktop;

/// <summary>Owns one desktop-only Harness child from readiness through quiescent exit.</summary>
internal sealed class HarnessProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(8);
    private readonly RuntimeLayout _runtime;
    private readonly string _workspacePath;
    private readonly DesktopLogger _logger;
    private readonly StringBuilder _diagnostics = new();
    private Process? _process;
    private readonly object _stopGate = new();
    private Task<bool>? _stopTask;

    internal HarnessProcess(RuntimeLayout runtime, string workspacePath, DesktopLogger logger)
    {
        _runtime = runtime;
        _workspacePath = workspacePath;
        _logger = logger;
    }

    internal int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

    /// <summary>Start the child and wait for both its protocol line and HTTP root.</summary>
    /// <param name="cancellationToken">Cancels desktop startup.</param>
    /// <returns>The exact loopback URL owned by this child.</returns>
    internal async Task<DesktopReady> StartAsync(CancellationToken cancellationToken)
    {
        if (_process is not null) throw new InvalidOperationException("Harness child already started.");
        if (!Directory.Exists(_workspacePath))
        {
            throw new DesktopStartupException($"默认工作区不存在：{_workspacePath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _runtime.NodePath,
            WorkingDirectory = _workspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add(_runtime.ServerEntryPath);
        startInfo.Environment["DSH_DESKTOP"] = "1";
        startInfo.Environment.Remove("NODE_OPTIONS");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process = process;
        var readySource = new TaskCompletionSource<DesktopReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, args) => HandleOutput(args.Data, readySource);
        process.ErrorDataReceived += (_, args) => HandleDiagnostic("harness:stderr", args.Data);

        try
        {
            if (!process.Start()) throw new DesktopStartupException("Harness 后端进程未启动。");
        }
        catch (Exception error) when (error is not DesktopStartupException)
        {
            throw new DesktopStartupException($"启动 Harness 后端失败：{error.Message}", error);
        }

        _logger.Write("desktop", $"started Harness pid={process.Id}, workspace={_workspacePath}, packaged={_runtime.IsPackaged}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);
        try
        {
            var exited = process.WaitForExitAsync(timeout.Token);
            var completed = await Task.WhenAny(readySource.Task, exited).ConfigureAwait(false);
            if (completed == exited)
            {
                await exited.ConfigureAwait(false);
                throw new DesktopStartupException(
                    $"Harness 在就绪前退出，状态 {process.ExitCode}。{Environment.NewLine}{DiagnosticTail()}");
            }

            var ready = await readySource.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (ready.ProcessId != process.Id)
            {
                throw new DesktopStartupException(
                    $"Harness 就绪进程不匹配：期望 {process.Id}，收到 {ready.ProcessId}。");
            }

            await VerifyHttpAsync(ready.Url, timeout.Token).ConfigureAwait(false);
            _logger.Write("desktop", $"Harness ready at {ready.Url}");
            return ready;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
            throw new DesktopStartupException($"Harness 启动超过 {StartupTimeout.TotalSeconds:0} 秒。{Environment.NewLine}{DiagnosticTail()}");
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Request application disposal, then kill the owned tree only after the bounded grace expires.</summary>
    /// <returns><see langword="true"/> when the child acknowledged shutdown without forced termination.</returns>
    internal Task<bool> StopAsync()
    {
        lock (_stopGate)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task<bool> StopCoreAsync()
    {
        var process = _process;
        if (process is null || process.HasExited) return true;
        try
        {
            await process.StandardInput.WriteLineAsync("shutdown").ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            _logger.Write("desktop", $"shutdown pipe unavailable: {error.Message}");
        }

        using var timeout = new CancellationTokenSource(ShutdownTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            _logger.Write("desktop", $"Harness exited code={process.ExitCode}");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.Write("desktop", "Harness shutdown grace expired; killing owned process tree");
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _process?.Dispose();
        _process = null;
    }

    private void HandleOutput(string? line, TaskCompletionSource<DesktopReady> readySource)
    {
        if (line is null) return;
        HandleDiagnostic("harness:stdout", line);
        if (DesktopProtocol.TryParseReady(line, out var ready) && ready is not null)
        {
            readySource.TrySetResult(ready);
        }
    }

    private void HandleDiagnostic(string source, string? line)
    {
        if (line is null) return;
        lock (_diagnostics)
        {
            _diagnostics.AppendLine(line);
            if (_diagnostics.Length > 32_768) _diagnostics.Remove(0, _diagnostics.Length - 24_576);
        }
        _logger.Write(source, line);
    }

    private string DiagnosticTail()
    {
        lock (_diagnostics) return _diagnostics.ToString().Trim();
    }

    private static async Task VerifyHttpAsync(Uri url, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new DesktopStartupException($"Harness 首页返回 HTTP {(int)response.StatusCode}。");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopStartupException($"Harness 首页返回了意外内容类型：{mediaType ?? "(none)"}。");
        }
    }
}
