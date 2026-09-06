using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>A numbered set of preset slots.</summary>
public sealed class SlotGroup(SlotGroupKind kind, JsonObject node)
{
    private readonly JsonObject _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>Which family of slots this is.</summary>
    public SlotGroupKind Kind { get; } = kind;

    private JsonArray SlotsArray
    {
        get
        {
            if (_node["slots"] is not JsonArray slots)
            {
                slots = [];
                _node["slots"] = slots;
            }

            return slots;
        }
    }

    /// <summary>Highest slot index the overlay knows about.</summary>
    public int LastSlotIndex
    {
        get => _node["lastSlotIdx"]?.GetValue<int>() ?? 0;
        set => _node["lastSlotIdx"] = value;
    }

    /// <summary>Slots in document order.</summary>
    public IReadOnlyList<Slot> Slots =>
        [.. SlotsArray.OfType<JsonObject>().Select(s => new Slot(s))];

    /// <summary>Gets a slot by its id, or <see langword="null"/>.</summary>
    public Slot? GetSlot(int id) => Slots.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Gets a slot by id, creating an empty one if it does not exist.
    /// </summary>
    /// <remarks>
    /// Slot 0 is the overlay's "None" entry and is never a target for edits,
    /// but it is still represented so the array matches what the overlay wrote.
    /// </remarks>
    public Slot EnsureSlot(int id, string? label = null)
    {
        if (GetSlot(id) is { } existing)
        {
            return existing;
        }

        var slot = new JsonObject
        {
            ["filterStack"] = new JsonObject
            {
                ["filters"] = new JsonArray(),
                ["selectedFilterCount"] = 0,
                ["upButtonDisabled"] = true,
                ["downButtonDisabled"] = true,
            },
            ["id"] = id,
            ["altText"] = label ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        SlotsArray.Add(slot);

        if (id > LastSlotIndex)
        {
            LastSlotIndex = id;
        }

        return new Slot(slot);
    }
}
