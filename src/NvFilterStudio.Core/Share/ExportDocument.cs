using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Share;

/// <summary>Friendly names for the filters whose controls have been mapped.</summary>
public static class FilterNames
{
    /// <summary>Shader file name to the label the NVIDIA App shows.</summary>
    public static IReadOnlyDictionary<string, string> ByShader { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Adjustments.fx"] = "Brightness/Contrast",
            ["BeautifyDOF.fx"] = "Auto Depth of Field",
            ["BlacknWhite.fx"] = "Black and White",
            ["Color.fx"] = "Color",
            ["Colorblind.fx"] = "Color Blind Mode",
            ["DOF.fx"] = "Depth of Field",
            ["Details.fx"] = "Details",
            ["Letterbox.fx"] = "Letterbox",
            ["NightMode.fx"] = "Night Mode",
            ["NvNewSharpen.fx"] = "Sharpen+",
            ["NvTiltShift.fx"] = "Tilt-Shift",
            ["NvVignette.fx"] = "Vignette",
            ["OldFilm.fx"] = "Old Film",
            ["Painterly.fx"] = "Painterly",
            ["Sharpen.fx"] = "Sharpen",
            ["SpecialFX.fx"] = "Special FX",
            ["Splitscreen.fx"] = "Splitscreen",
            ["Watercolor.fx"] = "Watercolor",
        };

    /// <summary>
    /// Control ids to English names, per shader.
    /// </summary>
    /// <remarks>
    /// A control's stable identity is (shader, id). The store's own
    /// <c>displayName</c> follows the App's UI language and is not reliable —
    /// on a German install <c>Adjustments.fx</c> control 2 is labelled
    /// <c>Hoogtepunten</c>, which is Dutch.
    /// <para>
    /// Harvested by putting all eighteen filters into one slot and reading the
    /// result back, so the ids and the control counts are observed fact. The
    /// English names are <em>translations of the German labels</em> from that
    /// machine, not text seen in an English NVIDIA App — a few will read a
    /// little off, and a correction from anyone running one is welcome.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> ControlsByShader { get; } =
        new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Adjustments.fx"] = new Dictionary<int, string>
            {
                [0] = "Exposure",
                [1] = "Contrast",
                [2] = "Highlights",
                [3] = "Shadows",
                [4] = "Gamma",
            },
            ["BeautifyDOF.fx"] = new Dictionary<int, string>
            {
                [0] = "Speed",
                [1] = "Intensity",
                [2] = "InvertZAxis",
                [3] = "InvertYAxis",
            },
            ["BlacknWhite.fx"] = new Dictionary<int, string>
            {
                [0] = "Intensity",
                [1] = "EnableDepth",
                [2] = "EdgeDistance",
                [3] = "InvertZAxis",
                [4] = "InvertYAxis",
            },
            ["Color.fx"] = new Dictionary<int, string>
            {
                [0] = "TintColor",
                [1] = "TintIntensity",
                [2] = "Temperature",
                [3] = "Vibrance",
            },
            ["Colorblind.fx"] = new Dictionary<int, string>
            {
                [0] = "Protanopia",
                [1] = "Deuteranopia",
                [2] = "Tritanopia",
            },
            ["DOF.fx"] = new Dictionary<int, string>
            {
                [0] = "FocusDepth",
                [1] = "FarBlurCurve",
                [2] = "NearBlurCurve",
                [3] = "BlurRadius",
                [4] = "InvertZAxis",
                [5] = "InvertYAxis",
            },
            ["Details.fx"] = new Dictionary<int, string>
            {
                [0] = "Sharpen",
                [1] = "Clarity",
                [2] = "HDRToning",
                [3] = "Bloom",
            },
            ["Letterbox.fx"] = new Dictionary<int, string>
            {
                [0] = "HorizontalScale",
                [1] = "VerticalScale",
            },
            ["NightMode.fx"] = new Dictionary<int, string>
            {
                [0] = "Intensity",
            },
            ["NvNewSharpen.fx"] = new Dictionary<int, string>
            {
                [0] = "Intensity",
                [1] = "TextureDetail",
            },
            ["NvTiltShift.fx"] = new Dictionary<int, string>
            {
                [0] = "Axis",
                [1] = "BlurSize",
                [2] = "BlurCurve",
            },
            ["NvVignette.fx"] = new Dictionary<int, string>
            {
                [0] = "Intensity",
            },
            ["OldFilm.fx"] = new Dictionary<int, string>
            {
                [0] = "Gamma",
                [1] = "Exposure",
                [2] = "Contrast",
                [3] = "VignetteStrength",
                [4] = "FilterStrength",
                [5] = "GrimeStrength",
            },
            ["Painterly.fx"] = new Dictionary<int, string>
            {
                [0] = "Iterations",
                [1] = "SampleDirections",
                [2] = "Radius",
                [3] = "EdgeSharpness",
            },
            ["Sharpen.fx"] = new Dictionary<int, string>
            {
                [0] = "Intensity",
                [1] = "IgnoreFilmGrain",
            },
            ["SpecialFX.fx"] = new Dictionary<int, string>
            {
                [0] = "Retro",
                [1] = "Sketch",
                [2] = "Halftone",
                [3] = "Sepia",
            },
            ["Splitscreen.fx"] = new Dictionary<int, string>
            {
                [0] = "SplitAndCompare",
                [1] = "Position",
                [2] = "Rotation",
                [3] = "DividerWidth",
                [4] = "DividerColor",
                [5] = "GradientFade",
                [6] = "Zoom",
            },
            ["Watercolor.fx"] = new Dictionary<int, string>
            {
                [0] = "Gamma",
                [1] = "Exposure",
                [2] = "Contrast",
                [3] = "Saturation",
                [4] = "TintIntensity",
                [5] = "PencilIntensity",
                [6] = "PencilBlur",
                [7] = "PencilSoftness",
                [8] = "ColorDetail",
                [9] = "ColorBlur",
            },
        };

    /// <summary>Friendly name for a shader, or the file name when unmapped.</summary>
    public static string ForShader(string shader) =>
        ByShader.TryGetValue(shader, out string? name) ? name : shader;

    /// <summary>Friendly control name, falling back to "control N".</summary>
    public static string ForControl(string shader, int controlId) =>
        ControlsByShader.TryGetValue(shader, out IReadOnlyDictionary<int, string>? map) &&
        map.TryGetValue(controlId, out string? name)
            ? name
            : $"control {controlId}";
}

/// <summary>
/// The portable JSON export.
/// </summary>
/// <remarks>
/// Deliberately schema-compatible with the PowerShell reference tools, so files
/// produced by either are interchangeable.
/// <para>
/// Every filter carries a verbatim <c>native</c> block alongside the readable
/// <c>settings</c>. Import overlays <c>settings</c> onto <c>native</c>, which
/// is what lets a profile be restored onto a machine whose store has never held
/// that filter. It is also why the file is large — see
/// <see cref="ShareCode"/> for the pasteable form.
/// </para>
/// </remarks>
public static class ExportDocument
{
    /// <summary>Schema version written into every export.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Builds an export from a store document.</summary>
    /// <param name="document">Source presets.</param>
    /// <param name="gameFilter">Optional substring match on the executable path.</param>
    /// <param name="source">Provenance recorded in the file; never includes account ids.</param>
    public static string Create(
        FilterPresetDocument document,
        string? gameFilter = null,
        IReadOnlyDictionary<string, string>? source = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var profiles = new JsonArray();

        foreach (GameProfile game in document.Games())
        {
            if (gameFilter is not null &&
                !game.ExePath.Contains(gameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var groups = new JsonObject();
            foreach (SlotGroup group in game.Groups())
            {
                groups[PropertyName(group.Kind)] = BuildGroup(group);
            }

            profiles.Add(new JsonObject
            {
                ["game"] = game.DisplayName,
                ["executable"] = game.Executable,
                ["exePath"] = game.ExePath,
                ["slotGroups"] = groups,
            });
        }

        var sourceNode = new JsonObject();
        foreach ((string key, string value) in source ?? new Dictionary<string, string>())
        {
            sourceNode[key] = RedactUserPath(value);
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["exportedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["source"] = sourceNode,
            ["profiles"] = profiles,
        };

        return root.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Replaces the user's own profile directory with the variable that names it.
    /// </summary>
    /// <remarks>
    /// Exports exist to be sent to other people, and the store lives under
    /// <c>%LOCALAPPDATA%</c> — so recording its literal path put the sender's
    /// Windows account name in every file they shared. The path is worth keeping
    /// for diagnostics; the account name is not.
    /// <para>
    /// Applied here rather than at the call site so it cannot be forgotten by a
    /// future caller: everything written into <c>source</c> goes through it.
    /// </para>
    /// </remarks>
    internal static string RedactUserPath(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Longest first: LocalApplicationData sits inside the profile, so
        // replacing the profile first would leave the more specific one unmatched.
        foreach ((Environment.SpecialFolder folder, string name) in (
            (Environment.SpecialFolder, string)[])
            [
                (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
                (Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
                (Environment.SpecialFolder.UserProfile, "%USERPROFILE%"),
            ])
        {
            string path = Environment.GetFolderPath(folder);

            if (path.Length > 0 && value.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(name, value.AsSpan(path.Length));
            }
        }

        return value;
    }

    private static JsonObject BuildGroup(SlotGroup group)
    {
        var slots = new JsonArray();

        foreach (Slot slot in group.Slots)
        {
            var filters = new JsonArray();
            foreach (FilterEntry filter in slot.Filters)
            {
                var settings = new JsonObject();
                var controls = new JsonArray();

                foreach (ControlEntry control in filter.Controls)
                {
                    string name = FilterNames.ForControl(filter.Shader, control.Id);
                    settings[name] = control.UiValue;

                    controls.Add(new JsonObject
                    {
                        ["controlId"] = control.Id,
                        ["name"] = name,
                        ["localizedName"] = control.LocalizedName,
                        ["uiValue"] = control.UiValue,
                        ["rawValue"] = control.RawValue,
                        ["uiMin"] = control.UiMinimum,
                        ["uiMax"] = control.UiMaximum,
                        ["uiStep"] = control.UiStep,
                        ["defaultUiValue"] = control.UiDefault,
                    });
                }

                filters.Add(new JsonObject
                {
                    ["order"] = filter.StackIndex,
                    ["name"] = FilterNames.ForShader(filter.Shader),
                    ["shader"] = filter.Shader,
                    ["shaderPath"] = filter.Id,
                    ["localizedName"] = filter.LocalizedName,
                    ["isSelected"] = filter.IsSelected,
                    ["settings"] = settings,
                    ["controls"] = controls,
                    ["native"] = filter.DetachClone(),
                });
            }

            slots.Add(new JsonObject
            {
                ["id"] = slot.Id,
                ["label"] = slot.Label,
                ["filterCount"] = slot.FilterCount,
                ["filters"] = filters,
            });
        }

        return new JsonObject
        {
            ["lastSlotIdx"] = group.LastSlotIndex,
            ["slots"] = slots,
        };
    }

    private static string PropertyName(SlotGroupKind kind) => kind switch
    {
        SlotGroupKind.GameFilters => GameProfile.GameFiltersProperty,
        SlotGroupKind.Ansel => GameProfile.AnselProperty,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
