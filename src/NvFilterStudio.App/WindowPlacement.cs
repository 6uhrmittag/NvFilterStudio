using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace NvFilterStudio.App;

/// <summary>Saved window geometry.</summary>
/// <param name="Left">Left edge in virtual-screen coordinates.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Width">Window width.</param>
/// <param name="Height">Window height.</param>
/// <param name="Maximised">Whether the window was maximised.</param>
public sealed record WindowGeometry(double Left, double Top, double Width, double Height, bool Maximised);

/// <summary>
/// Remembers where the window was.
/// </summary>
/// <remarks>
/// Matching a profile against someone's screenshot means resizing and moving
/// this window constantly, and reopening centred at a fixed size every time is
/// a small, repeated annoyance.
/// <para>
/// A restored position is only honoured if it still lands on a connected
/// display. Otherwise unplugging a second monitor would strand the window
/// off-screen with no obvious way back.
/// </para>
/// </remarks>
public static class WindowPlacement
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NvFilterStudio",
            "window.json");

    /// <summary>Restores geometry, ignoring anything unusable.</summary>
    public static void Restore(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowGeometry? saved = Read();
        if (saved is null || saved.Width < window.MinWidth || saved.Height < window.MinHeight)
        {
            return;
        }

        if (!IsOnScreen(saved))
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = saved.Left;
        window.Top = saved.Top;
        window.Width = saved.Width;
        window.Height = saved.Height;
        window.WindowState = saved.Maximised ? WindowState.Maximized : WindowState.Normal;
    }

    /// <summary>Saves geometry, ignoring failures.</summary>
    public static void Save(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            // RestoreBounds is the un-maximised rectangle, which is what should
            // come back when the window is un-maximised later.
            Rect bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.Width, window.Height)
                : window.RestoreBounds;

            var geometry = new WindowGeometry(
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                window.WindowState == WindowState.Maximized);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(geometry, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Losing a window position is not worth interrupting anyone over.
        }
    }

    private static WindowGeometry? Read()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<WindowGeometry>(File.ReadAllText(SettingsPath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether enough of the window would land on the virtual desktop to be
    /// grabbed with a mouse.
    /// </summary>
    private static bool IsOnScreen(WindowGeometry geometry)
    {
        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double right = left + SystemParameters.VirtualScreenWidth;
        double bottom = top + SystemParameters.VirtualScreenHeight;

        // Require the title bar to be reachable rather than the whole window,
        // so a slightly off-edge position is still restored.
        const double TitleBarHeight = 32;
        double centreX = geometry.Left + (geometry.Width / 2);

        return centreX > left && centreX < right &&
               geometry.Top + TitleBarHeight > top && geometry.Top < bottom;
    }
}
