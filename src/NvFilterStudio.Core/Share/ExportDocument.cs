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
            ["Color.fx"] = "Color",
            ["Details.fx"] = "Details",
            ["Colorblind.fx"] = "Color Blind Mode",
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
    /// Six further shaders exist (Letterbox, NightMode, SpecialFX, Watercolor,
    /// Painterly, Splitscreen). They read and write correctly but fall back to
    /// "control N" until someone maps them.
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
            ["Color.fx"] = new Dictionary<int, string>
            {
                [0] = "TintColor",
                [1] = "TintIntensity",
                [2] = "Temperature",
                [3] = "Vibrance",
            },
            ["Details.fx"] = new Dictionary<int, string>
            {
                [0] = "Sharpen",
                [1] = "Clarity",
                [2] = "HDRToning",
                [3] = "Bloom",
            },
            ["Colorblind.fx"] = new Dictionary<int, string>
            {
                [0] = "Protanopia",
                [1] = "Deuteranopia",
                [2] = "Tritanopia",
            },
        };

    /// <summary>Friendly filter name, falling back to the shader file name.</summary>
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
            sourceNode[key] = value;
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
