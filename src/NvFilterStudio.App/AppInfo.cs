using System.Reflection;

namespace NvFilterStudio.App;

/// <summary>Build identity, shown in the app and stamped into crash logs.</summary>
/// <remarks>
/// Without this a bug report cannot say which build it came from, and the
/// published executable's Properties dialog is blank. The version is set in
/// <c>Directory.Build.props</c> and overridden from the tag on tag builds.
/// </remarks>
public static class AppInfo
{
    /// <summary>Informational version, e.g. <c>0.1.0</c> or <c>0.1.0+abc1234</c>.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Version rendered for the window header.</summary>
    public static string DisplayVersion => $"v{Version}";

    private static string ReadVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // SourceLink appends "+<commit sha>"; keep it out of the UI but
            // leave it in crash logs by reading this property directly.
            return informational;
        }

        // Deliberately not FileVersionInfo.GetVersionInfo(assembly.Location):
        // Location is an empty string inside a single-file app, which is
        // exactly the build people download. It would have failed only in the
        // published exe.
        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    /// <summary>Version without any build metadata, for display.</summary>
    public static string ShortVersion
    {
        get
        {
            int plus = Version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? Version : Version[..plus];
        }
    }
}
