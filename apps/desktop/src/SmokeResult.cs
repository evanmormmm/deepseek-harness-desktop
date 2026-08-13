using System.Text.Json;

namespace DeepSeekHarness.Desktop;

/// <summary>Machine-readable result from the packaged desktop lifecycle smoke.</summary>
internal sealed record SmokeResult(
    bool Success,
    bool WebViewLoaded,
    bool GracefulShutdown,
    string? Url,
    int? BackendProcessId,
    string? Error)
{
    internal void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
