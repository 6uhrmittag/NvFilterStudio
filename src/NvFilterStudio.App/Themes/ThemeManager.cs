using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NvFilterStudio.App.Themes;

/// <summary>Which palette is showing.</summary>
public enum AppTheme
{
    /// <summary>Soft pastels on near-white.</summary>
    Light,

    /// <summary>The same pastels as accents on a deep plum ground.</summary>
    Dark,
}

/// <summary>
/// Switches the palette at runtime.
/// </summary>
/// <remarks>
/// Brush resources are <em>replaced</em> in the application dictionary, and the
/// XAML references them with <c>DynamicResource</c> so the change propagates.
/// <para>
/// Mutating <see cref="SolidColorBrush.Color"/> in place was tried first and
/// silently did nothing: WPF freezes brush resources loaded from BAML, so every
/// assignment was skipped and the app stayed light. The failure was invisible
/// because the guard against writing to a frozen brush simply moved on.
/// Replacing the resource works regardless of freezing.
/// </para>
/// </remarks>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The palette currently applied.</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>
    /// Colours per theme, keyed by the brush resource names in Palette.xaml.
    /// </summary>
    /// <remarks>
    /// Dark is not an inversion. The accents stay the same hues so the app
    /// still reads as itself, while text and grounds swap roles — and the
    /// contrast promise in Palette.xaml holds in both: body text sits well
    /// clear of its background either way.
    /// </remarks>
    private static readonly Dictionary<AppTheme, Dictionary<string, string>> Palettes = new()
    {
        [AppTheme.Light] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CanvasBrush"] = "#FFFDF9FB",
            ["CardBrush"] = "#FFFFFFFF",
            ["SunkenBrush"] = "#FFF6EEF4",
            ["BlushBrush"] = "#FFF48FB1",
            ["BlushSoftBrush"] = "#FFFFD9E4",
            ["LavenderBrush"] = "#FFB39DDB",
            ["MintBrush"] = "#FF80CBC4",
            ["ButterBrush"] = "#FFFFE0A3",
            ["InkBrush"] = "#FF3B2F36",
            ["InkSoftBrush"] = "#FF7A6A73",
            ["InkOnAccentBrush"] = "#FF3B2F36",
            ["EdgeBrush"] = "#FFEBDDE5",
            ["WarnBrush"] = "#FFFFF3D6",
            ["WarnEdgeBrush"] = "#FFE8C77A",
            ["GoodBrush"] = "#FFDFF5EE",
        },
        [AppTheme.Dark] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CanvasBrush"] = "#FF191521",
            ["CardBrush"] = "#FF231D2C",
            ["SunkenBrush"] = "#FF2E2739",
            ["BlushBrush"] = "#FFF48FB1",
            ["BlushSoftBrush"] = "#FF4B2E3D",
            ["LavenderBrush"] = "#FF9C86C4",
            ["MintBrush"] = "#FF6FB3AC",
            ["ButterBrush"] = "#FF5C4A2C",
            ["InkBrush"] = "#FFF4ECF2",
            ["InkSoftBrush"] = "#FFB3A4B4",
            // Accent fills stay light pink, so text on them must stay dark.
            ["InkOnAccentBrush"] = "#FF2A1F26",
            ["EdgeBrush"] = "#FF3A3147",
            ["WarnBrush"] = "#FF3E3320",
            ["WarnEdgeBrush"] = "#FF8C7238",
            ["GoodBrush"] = "#FF223A34",
        },
    };

    /// <summary>Applies a palette to the running application.</summary>
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach ((string key, string hex) in Palettes[theme])
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            app.Resources[key] = brush;
        }

        Current = theme;
    }

    /// <summary>Flips between light and dark.</summary>
    public static AppTheme Toggle()
    {
        AppTheme next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        Apply(next);
        return next;
    }

    /// <summary>
    /// Applies whichever palette matches the Windows app theme.
    /// </summary>
    /// <remarks>
    /// Falls back to light: a tool that opens unreadably is worse than one that
    /// opens in the wrong theme.
    /// </remarks>
    public static void ApplySystemTheme() => Apply(DetectSystemTheme());

    private static AppTheme DetectSystemTheme()
    {
        try
        {
            // 0 means dark for apps; the value is absent on older builds.
            return Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0
                ? AppTheme.Dark
                : AppTheme.Light;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException)
        {
            return AppTheme.Light;
        }
    }
}
