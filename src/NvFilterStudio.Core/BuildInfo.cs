using System.Reflection;

namespace NvFilterStudio.Core;

/// <summary>Build identity, shown in the app and stamped into crash logs.</summary>
/// <remarks>
/// Without this a bug report cannot say which build it came from, and the
/// published executable's Properties dialog is blank. The version is set in
/// <c>Directory.Build.props</c> and overridden from the tag on tag builds.
/// <para>
/// Lives in Core so the app and the console tool report the same string. The
/// bug report form asks people to paste <c>nvfs status</c>, which is not much
/// use if it cannot say which build produced it.
/// </para>
/// </remarks>
public static class BuildInfo
{
    /// <summary>Informational version, e.g. <c>0.2.0</c> or <c>0.2.0+abc1234</c>.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Version without any build metadata, for display.</summary>
    public static string ShortVersion
    {
        get
        {
            int plus = Version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? Version : Version[..plus];
        }
    }

    private static string ReadVersion()
    {
        // The entry assembly, not the executing one: this type lives in Core,
        // and Core's own version is not what a bug report is about. Falls back
        // for hosts that have no entry assembly, such as a test runner.
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;

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
}
