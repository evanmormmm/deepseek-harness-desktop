using System.Text;

namespace DeepSeekHarness.Desktop;

/// <summary>Small append-only desktop lifecycle log that never receives model traffic.</summary>
internal sealed class DesktopLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    internal DesktopLogger()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeek Harness",
            "logs");
        Directory.CreateDirectory(directory);
        LogPath = System.IO.Path.Combine(directory, "desktop.log");
        RotateIfNeeded(LogPath);
        _writer = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    internal string LogPath { get; }

    internal void Write(string source, string message)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTimeOffset.Now:O} [{source}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_gate) _writer.Dispose();
    }

    private static void RotateIfNeeded(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 2 * 1024 * 1024) return;
        var previous = $"{path}.previous";
        File.Move(path, previous, true);
    }
}
