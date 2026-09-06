using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>Raised when an edit would produce a stack the overlay rejects.</summary>
public sealed class InvalidFilterStackException(string message) : Exception(message);

/// <summary>One preset slot and its ordered filter stack.</summary>
public sealed class Slot(JsonObject node)
{
    private readonly JsonObject _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>Slot id, matching the number shown in the overlay.</summary>
    public int Id => _node["id"]?.GetValue<int>() ?? 0;

    /// <summary>Label the overlay shows; "settings.None" for slot 0.</summary>
    public string Label => _node["altText"]?.GetValue<string>() ?? string.Empty;

    /// <summary>Whether this is the overlay's "None" slot, which is not editable.</summary>
    public bool IsNoneSlot => Id == 0;

    private JsonObject Stack
    {
        get
        {
            if (_node["filterStack"] is not JsonObject stack)
            {
                stack = new JsonObject { ["filters"] = new JsonArray() };
                _node["filterStack"] = stack;
            }

            return stack;
        }
    }

    private JsonArray FiltersArray
    {
        get
        {
            if (Stack["filters"] is not JsonArray filters)
            {
                filters = [];
                Stack["filters"] = filters;
            }

            return filters;
        }
    }

    /// <summary>Filters in stack order.</summary>
    public IReadOnlyList<FilterEntry> Filters =>
        [.. FiltersArray.OfType<JsonObject>()
            .Select(f => new FilterEntry(f))
            .OrderBy(f => f.StackIndex)];

    /// <summary>Number of filters in the stack.</summary>
    public int FilterCount => FiltersArray.Count;

    /// <summary>
    /// Appends a filter built from <paramref name="skeleton"/>.
    /// </summary>
    /// <exception cref="InvalidFilterStackException">
    /// A filter of the same shader is already present. The overlay allows only
    /// one instance of each filter type.
    /// </exception>
    public FilterEntry AddFilter(JsonObject skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        var candidate = new FilterEntry(skeleton);
        string shader = candidate.Shader;

        if (Filters.Any(f => string.Equals(f.Shader, shader, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidFilterStackException(
                $"{shader} is already in this slot. The NVIDIA overlay allows only one " +
                "instance of each filter type.");
        }

        // Place it past every existing filter before normalising. A skeleton
        // arrives with whatever stackIdx it was harvested with — usually 0 —
        // and Normalise sorts by that index, so an unadjusted skeleton would be
        // stable-sorted into the middle of the stack instead of appended.
        // Order changes the rendered image, so that would be a silent visual
        // change rather than a cosmetic one.
        candidate.StackIndex = FiltersArray.Count == 0
            ? 0
            : Filters.Max(f => f.StackIndex) + 1;

        FiltersArray.Add(skeleton);
        Normalise();
        return new FilterEntry(skeleton);
    }

    /// <summary>Removes the filter at <paramref name="stackIndex"/>.</summary>
    public void RemoveFilterAt(int stackIndex)
    {
        JsonObject? target = FiltersArray.OfType<JsonObject>()
            .FirstOrDefault(f => new FilterEntry(f).StackIndex == stackIndex);

        if (target is null)
        {
            throw new InvalidFilterStackException($"No filter at stack index {stackIndex}.");
        }

        FiltersArray.Remove(target);
        Normalise();
    }

    /// <summary>Removes every filter.</summary>
    public void Clear()
    {
        FiltersArray.Clear();
        Normalise();
    }

    /// <summary>
    /// Moves a filter within the stack.
    /// </summary>
    /// <remarks>
    /// Order is data, not presentation: the stack applies low to high, so the
    /// same values in a different order produce a different image.
    /// </remarks>
    public void MoveFilter(int fromStackIndex, int toStackIndex)
    {
        List<FilterEntry> ordered = [.. Filters];

        if (fromStackIndex < 0 || fromStackIndex >= ordered.Count)
        {
            throw new InvalidFilterStackException($"No filter at stack index {fromStackIndex}.");
        }

        int destination = Math.Clamp(toStackIndex, 0, ordered.Count - 1);
        if (destination == fromStackIndex)
        {
            return;
        }

        FilterEntry moving = ordered[fromStackIndex];
        ordered.RemoveAt(fromStackIndex);
        ordered.Insert(destination, moving);

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].StackIndex = i;
        }

        Normalise();
    }

    /// <summary>
    /// Renumbers stack indices contiguously from zero and refreshes the
    /// bookkeeping fields the overlay writes alongside the stack.
    /// </summary>
    public void Normalise()
    {
        List<FilterEntry> ordered = [.. FiltersArray.OfType<JsonObject>()
            .Select(f => new FilterEntry(f))
            .OrderBy(f => f.StackIndex)];

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].StackIndex = i;
        }

        // Rewrite the array in stack order so the document reads the way the
        // overlay writes it.
        var reordered = new JsonArray();
        foreach (FilterEntry entry in ordered)
        {
            JsonObject detached = entry.DetachClone();
            reordered.Add(detached);
        }

        Stack["filters"] = reordered;
        Stack["selectedFilterCount"] = ordered.Count(f => f.IsSelected);
        Stack["upButtonDisabled"] = ordered.Count <= 1;
        Stack["downButtonDisabled"] = ordered.Count <= 1;
    }
}
