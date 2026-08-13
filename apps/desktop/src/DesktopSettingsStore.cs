using System.Drawing;
using System.Text.Json;

namespace DeepSeekHarness.Desktop;

/// <summary>Persists native window placement independently of Harness session data.</summary>
internal sealed class DesktopSettingsStore
{
    private readonly string _path;

    internal DesktopSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeek Harness");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "desktop-settings.json");
    }

    internal WindowPlacement Load()
    {
        try
        {
            if (!File.Exists(_path)) return WindowPlacement.Default;
            var saved = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path));
            if (saved is null || saved.Width < 960 || saved.Height < 640) return WindowPlacement.Default;
            var bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
            return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))
                ? saved
                : WindowPlacement.Default;
        }
        catch (Exception) when (File.Exists(_path))
        {
            return WindowPlacement.Default;
        }
    }

    internal void Save(Form form)
    {
        var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        var placement = new WindowPlacement(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            form.WindowState == FormWindowState.Maximized);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(placement));
        File.Move(temporary, _path, true);
    }
}

/// <summary>Serializable native window placement.</summary>
internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    internal static WindowPlacement Default => new(120, 80, 1320, 860, false);
}
