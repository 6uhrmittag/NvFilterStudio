using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>Which family of slots a stack belongs to.</summary>
public enum SlotGroupKind
{
    /// <summary>Game filter slots — the ones the overlay's Game Filters page edits.</summary>
    GameFilters,

    /// <summary>Photo-mode (Ansel) slots.</summary>
    Ansel,
}

/// <summary>Presets for one game, keyed in the store by full executable path.</summary>
public sealed class GameProfile(string exePath, JsonObject node)
{
    internal const string GameFiltersProperty = "modsSlotsInfo";
    internal const string AnselProperty = "anselSlotsInfo";

    private readonly JsonObject _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>Full executable path this profile is keyed on.</summary>
    public string ExePath { get; } = exePath ?? throw new ArgumentNullException(nameof(exePath));

    /// <summary>Executable file name.</summary>
    public string Executable => Path.GetFileName(ExePath);

    /// <summary>A display name derived from the executable.</summary>
    public string DisplayName => Path.GetFileNameWithoutExtension(ExePath);

    /// <summary>Gets a slot group, or <see langword="null"/> when absent.</summary>
    public SlotGroup? GetGroup(SlotGroupKind kind) =>
        _node[PropertyFor(kind)] is JsonObject group ? new SlotGroup(kind, group) : null;

    /// <summary>Gets a slot group, creating an empty one if needed.</summary>
    public SlotGroup EnsureGroup(SlotGroupKind kind)
    {
        if (GetGroup(kind) is { } existing)
        {
            return existing;
        }

        var group = new JsonObject
        {
            ["lastSlotIdx"] = 0,
            ["slots"] = new JsonArray(),
        };

        _node[PropertyFor(kind)] = group;
        return new SlotGroup(kind, group);
    }

    /// <summary>Groups present on this profile.</summary>
    public IEnumerable<SlotGroup> Groups()
    {
        foreach (SlotGroupKind kind in Enum.GetValues<SlotGroupKind>())
        {
            if (GetGroup(kind) is { } group)
            {
                yield return group;
            }
        }
    }

    private static string PropertyFor(SlotGroupKind kind) => kind switch
    {
        SlotGroupKind.GameFilters => GameFiltersProperty,
        SlotGroupKind.Ansel => AnselProperty,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
