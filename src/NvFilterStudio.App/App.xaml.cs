using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using NvFilterStudio.Core;

namespace NvFilterStudio.App;

/// <summary>Application entry point.</summary>
public partial class App : Application
{
    /// <summary>Where crash reports are written.</summary>
    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NvFilterStudio",
            "logs");

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // This reads an undocumented binary format written by software whose
        // version varies per machine, so unexpected input is likely rather than
        // hypothetical. The stock .NET crash dialog says nothing useful and
        // leaves nothing to quote in a bug report.
        DispatcherUnhandledException += OnUnhandledException;

        // Match the desktop before the first frame, so the window never
        // flashes white on a dark desktop. `--theme light|dark` overrides,
        // which is how the themes get screenshotted without changing the
        // machine's own settings.
        ArgumentNullException.ThrowIfNull(e);
        if (ThemeFromArguments(e.Args) is { } forced)
        {
            Themes.ThemeManager.Apply(forced);
        }
        else
        {
            Themes.ThemeManager.ApplySystemTheme();
        }

        base.OnStartup(e);
    }

    /// <summary>Reads <c>--theme light|dark</c>, or null when absent or unknown.</summary>
    private static Themes.AppTheme? ThemeFromArguments(string[] args)
    {
        int index = Array.FindIndex(
            args, a => string.Equals(a, "--theme", StringComparison.OrdinalIgnoreCase));

        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        return args[index + 1].ToLowerInvariant() switch
        {
            "dark" => Themes.AppTheme.Dark,
            "light" => Themes.AppTheme.Light,
            _ => null,
        };
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? logPath = TryWriteLog(e.Exception);

        string detail = logPath is null
            ? "The full error could not be written to a log file."
            : $"The full error is in:\n{logPath}";

        MessageBox.Show(
            $"Something went wrong.\n\n{e.Exception.Message}\n\n{detail}\n\n" +
            "Your filters have not been changed — NvFilterStudio only writes when " +
            "you press Apply, and it backs up first.",
            "NvFilterStudio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep running. The window state is still valid and edits are still in
        // memory; tearing down here would throw away exactly the work the
        // failure interrupted.
        e.Handled = true;
    }

    private static string? TryWriteLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            string path = Path.Combine(
                LogDirectory,
                $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");

            var report = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"NvFilterStudio {BuildInfo.Version}")
                .AppendLine(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O}")
                .AppendLine(CultureInfo.InvariantCulture, $"OS {Environment.OSVersion}")
                .AppendLine()
                .AppendLine(exception.ToString());

            File.WriteAllText(path, report.ToString());
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to log must not become a second crash.
            return null;
        }
    }
}
