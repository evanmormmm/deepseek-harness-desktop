using System.Security.Principal;

namespace DeepSeekHarness.Desktop;

/// <summary>Per-user single-instance ownership with activation and shutdown signaling.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly EventWaitHandle _shutdown;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task? _waitTask;
    private readonly object _callbackGate = new();
    private Control? _dispatcher;
    private Action? _activate;
    private Action? _close;
    private bool _activationPending;
    private bool _shutdownPending;

    private SingleInstance(
        Mutex mutex,
        EventWaitHandle activation,
        EventWaitHandle shutdown,
        bool isPrimary)
    {
        _mutex = mutex;
        _activation = activation;
        _shutdown = shutdown;
        IsPrimary = isPrimary;
        _waitTask = isPrimary ? Task.Run(WaitForSignal) : null;
    }

    internal bool IsPrimary { get; }

    internal static SingleInstance Create()
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var suffix = user.Replace('\\', '_').Replace('/', '_');
        var mutex = new Mutex(true, $"Local\\DeepSeekHarness.Desktop.{suffix}", out var created);
        var activation = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"Local\\DeepSeekHarness.Desktop.Activate.{suffix}");
        var shutdown = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"Local\\DeepSeekHarness.Desktop.Shutdown.{suffix}");
        return new SingleInstance(mutex, activation, shutdown, created);
    }

    internal void Attach(Control dispatcher, Action activate, Action close)
    {
        lock (_callbackGate)
        {
            _dispatcher = dispatcher;
            _activate = activate;
            _close = close;
            if (_shutdownPending) dispatcher.BeginInvoke(close);
            else if (_activationPending) dispatcher.BeginInvoke(activate);
            _shutdownPending = false;
            _activationPending = false;
        }
    }

    internal void SignalActivation()
    {
        if (!IsPrimary) _activation.Set();
    }

    internal bool SignalShutdown(TimeSpan timeout)
    {
        if (IsPrimary) return true;
        _shutdown.Set();
        try
        {
            if (!_mutex.WaitOne(timeout)) return false;
            _mutex.ReleaseMutex();
            return true;
        }
        catch (AbandonedMutexException)
        {
            _mutex.ReleaseMutex();
            return true;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _waitTask?.Wait(TimeSpan.FromSeconds(2));
        _activation.Dispose();
        _shutdown.Dispose();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex.Dispose();
        _cancellation.Dispose();
    }

    private void WaitForSignal()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                var signal = WaitHandle.WaitAny([_activation, _shutdown, _cancellation.Token.WaitHandle]);
                if (signal == 2) return;
                lock (_callbackGate)
                {
                    var dispatcher = _dispatcher;
                    var callback = signal == 1 ? _close : _activate;
                    if (dispatcher is not null && callback is not null && !dispatcher.IsDisposed)
                    {
                        dispatcher.BeginInvoke(callback);
                    }
                    else if (signal == 1)
                    {
                        _shutdownPending = true;
                    }
                    else
                    {
                        _activationPending = true;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }
}
