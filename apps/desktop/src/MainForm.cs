using System.Diagnostics;
using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DeepSeekHarness.Desktop;

/// <summary>Native lifecycle shell around the existing DeepSeek Harness Web application.</summary>
internal sealed class MainForm : Form
{
    private readonly DesktopOptions _options;
    private readonly RuntimeLayout _runtime;
    private readonly DesktopLogger _logger;
    private readonly DesktopSettingsStore _settings = new();
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel _splash = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 18, 22) };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(184, 196, 201),
        Font = new Font("Segoe UI", 10.5f),
        TextAlign = ContentAlignment.TopCenter,
        Padding = new Padding(24, 12, 24, 0),
    };
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Top,
        Height = 3,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 24,
    };
    private readonly Button _retry = new() { Text = "重试", AutoSize = true, Visible = false };
    private readonly Button _openLog = new() { Text = "打开日志", AutoSize = true, Visible = false };
    private readonly CancellationTokenSource _lifetime = new();
    private HarnessProcess? _harness;
    private DesktopReady? _ready;
    private bool _webViewLoaded;
    private bool _closing;
    private bool _allowClose;
    private bool _smokeWritten;

    internal MainForm(DesktopOptions options, RuntimeLayout runtime, DesktopLogger logger)
    {
        _options = options;
        _runtime = runtime;
        _logger = logger;
        Text = "DeepSeek Harness";
        MinimumSize = new Size(960, 640);
        BackColor = Color.FromArgb(13, 18, 22);
        StartPosition = FormStartPosition.Manual;
        ApplyPlacement(_settings.Load());
        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon is not null) Icon = icon;

        BuildSplash();
        Controls.Add(_webView);
        Controls.Add(_splash);
        Shown += async (_, _) => await StartHarnessAsync();
        FormClosing += OnFormClosing;
        _retry.Click += async (_, _) => await StartHarnessAsync();
        _openLog.Click += (_, _) => OpenExternal(new Uri(_logger.LogPath));
    }

    internal int ExitCode { get; private set; }

    internal void ActivateFromSecondary()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
        TopMost = true;
        TopMost = false;
    }

    internal void RequestClose()
    {
        if (!IsDisposed) Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Dispose();
            _webView.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildSplash()
    {
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = _splash.BackColor,
        };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 64));

        var mark = new Label
        {
            Dock = DockStyle.Fill,
            Text = "◆",
            ForeColor = Color.FromArgb(80, 210, 184),
            Font = new Font("Segoe UI Symbol", 28f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "DeepSeek Harness",
            ForeColor = Color.FromArgb(239, 244, 245),
            Font = new Font("Segoe UI Variable Display", 22f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _status.Text = "正在启动本地 Harness…";
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
        };
        buttons.Controls.Add(_retry);
        buttons.Controls.Add(_openLog);
        buttons.Layout += (_, _) =>
        {
            var width = buttons.Controls.Cast<Control>().Where(control => control.Visible).Sum(control => control.Width + control.Margin.Horizontal);
            buttons.Padding = new Padding(Math.Max(0, (buttons.ClientSize.Width - width) / 2), 2, 0, 0);
        };

        center.Controls.Add(new Panel(), 0, 0);
        center.Controls.Add(mark, 0, 1);
        center.Controls.Add(title, 0, 2);
        center.Controls.Add(_status, 0, 3);
        center.Controls.Add(buttons, 0, 4);
        center.Controls.Add(new Panel(), 0, 5);
        _splash.Controls.Add(center);
        _splash.Controls.Add(_progress);
    }

    private async Task StartHarnessAsync()
    {
        if (_closing) return;
        _retry.Visible = false;
        _openLog.Visible = false;
        _progress.Visible = true;
        _status.Text = "正在启动本地 Harness…";
        _splash.Visible = true;
        _splash.BringToFront();
        _webView.Visible = false;

        if (_harness is not null)
        {
            await _harness.DisposeAsync();
            _harness = null;
        }

        try
        {
            _harness = new HarnessProcess(_runtime, _options.WorkspacePath, _logger);
            _ready = await _harness.StartAsync(_lifetime.Token);
            _status.Text = "正在加载桌面界面…";
            await InitializeWebViewAsync(_ready.Url);
        }
        catch (OperationCanceledException) when (_closing)
        {
            // FormClosing owns the joined Harness shutdown.
        }
        catch (Exception error) when (error is not OperationCanceledException || !_closing)
        {
            _logger.Write("desktop", $"startup failed: {error}");
            ExitCode = 1;
            ShowStartupError(error.Message);
            if (_options.IsSmokeTest)
            {
                if (_harness is not null)
                {
                    await _harness.DisposeAsync();
                    _harness = null;
                }
                WriteSmokeResult(false, false, error.ToString());
                _allowClose = true;
                Close();
            }
        }
    }

    private async Task InitializeWebViewAsync(Uri url)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeek Harness",
            "WebView2");
        Directory.CreateDirectory(dataDirectory);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDirectory);
        await _webView.EnsureCoreWebView2Async(environment);
        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("DSH_DESKTOP_DEVTOOLS") == "1";
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.NavigationStarting -= OnNavigationStarting;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested -= OnNewWindowRequested;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed -= OnWebProcessFailed;
        core.ProcessFailed += OnWebProcessFailed;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.NavigationCompleted += OnNavigationCompleted;
        core.Navigate(url.AbsoluteUri);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (_ready is null || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var target))
        {
            args.Cancel = true;
            return;
        }

        var disposition = NavigationPolicy.Classify(_ready.Url, target);
        if (disposition == NavigationDisposition.Allow) return;
        args.Cancel = true;
        if (disposition == NavigationDisposition.OpenExternal) OpenExternal(target);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (_ready is null || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var target)) return;
        if (NavigationPolicy.Classify(_ready.Url, target) == NavigationDisposition.OpenExternal) OpenExternal(target);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            ShowStartupError($"桌面页面加载失败：{args.WebErrorStatus}");
            return;
        }

        _webViewLoaded = true;
        ExitCode = 0;
        _progress.Visible = false;
        _splash.Visible = false;
        _webView.Visible = true;
        _webView.BringToFront();
        _logger.Write("desktop", "WebView loaded Harness root");
        if (_options.IsSmokeTest)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(750);
                if (!IsDisposed) BeginInvoke(Close);
            });
        }
    }

    private void OnWebProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        _logger.Write("webview", $"process failed: {args.ProcessFailedKind}; {args.Reason}");
        if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            ShowStartupError("WebView2 浏览器进程已退出，请重试。");
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        Enabled = false;
        UseWaitCursor = true;
        _status.Text = "正在保存会话并关闭…";
        _splash.Visible = true;
        _splash.BringToFront();
        _progress.Visible = true;
        _lifetime.Cancel();

        var graceful = true;
        try
        {
            if (_harness is not null) graceful = await _harness.StopAsync();
            _settings.Save(this);
            WriteSmokeResult(_webViewLoaded && graceful, graceful, null);
        }
        catch (Exception error)
        {
            graceful = false;
            ExitCode = 1;
            _logger.Write("desktop", $"shutdown failed: {error}");
            WriteSmokeResult(false, false, error.ToString());
        }
        finally
        {
            if (_harness is not null) await _harness.DisposeAsync();
            _harness = null;
            _allowClose = true;
            BeginInvoke(Close);
        }
    }

    private void ShowStartupError(string message)
    {
        _progress.Visible = false;
        _status.Text = $"启动失败\r\n{message}";
        _retry.Visible = true;
        _openLog.Visible = true;
    }

    private void WriteSmokeResult(bool success, bool graceful, string? error)
    {
        if (_smokeWritten || _options.SmokeResultPath is null) return;
        _smokeWritten = true;
        new SmokeResult(
            success,
            _webViewLoaded,
            graceful,
            _ready?.Url.AbsoluteUri,
            _ready?.ProcessId,
            error).Write(_options.SmokeResultPath);
    }

    private static void OpenExternal(Uri target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target.IsFile ? target.LocalPath : target.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // The requested system handler is optional; Harness remains usable without it.
        }
    }

    private void ApplyPlacement(WindowPlacement placement)
    {
        Bounds = new Rectangle(placement.X, placement.Y, placement.Width, placement.Height);
        if (placement.Maximized) WindowState = FormWindowState.Maximized;
    }
}
