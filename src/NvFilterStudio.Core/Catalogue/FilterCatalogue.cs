using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Catalogue;

/// <summary>A filter definition the app can insert into a slot.</summary>
/// <param name="Shader">Shader file name, the stable identity.</param>
/// <param name="DisplayName">Friendly name for the UI.</param>
/// <param name="Skeleton">A complete filter node, ready to add to a stack.</param>
public sealed record CatalogueEntry(string Shader, string DisplayName, JsonObject Skeleton);

/// <summary>
/// Known filter definitions, so filters can be added to a slot.
/// </summary>
/// <remarks>
/// Adding a filter means writing a complete node: every control with its
/// bounds, step and default. The store only holds that for filters that have
/// been in a slot, so definitions are learned and remembered rather than
/// invented.
/// <para>
/// Sources, in precedence order: whatever the current document contains, then
/// anything previously learned and cached. Nothing is ever fabricated — a
/// filter with no definition is reported as unavailable so the UI can tell the
/// user how to teach it one, rather than writing a guess into their store.
/// </para>
/// </remarks>
public sealed class FilterCatalogue
{
    private static readonly JsonSerializerOptions CacheOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<string, CatalogueEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default cache location.</summary>
    public static string DefaultCachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NvFilterStudio",
            "filter-catalogue.json");

    /// <summary>Known shaders, alphabetically.</summary>
    public IReadOnlyList<CatalogueEntry> Entries =>
        [.. _entries.Values.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether a definition is available for <paramref name="shader"/>.</summary>
    public bool Knows(string shader) => _entries.ContainsKey(shader);

    /// <summary>Gets a fresh, detached skeleton ready to insert.</summary>
    public JsonObject? CreateSkeleton(string shader) =>
        _entries.TryGetValue(shader, out CatalogueEntry? entry)
            ? (JsonObject)JsonNode.Parse(entry.Skeleton.ToJsonString())!
            : null;

    /// <summary>
    /// Learns every filter definition present in <paramref name="document"/>.
    /// </summary>
    /// <returns>How many definitions were newly learned.</returns>
    public int LearnFrom(FilterPresetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        int learned = 0;

        foreach (GameProfile game in document.Games())
        {
            foreach (SlotGroup group in game.Groups())
            {
                foreach (Slot slot in group.Slots)
                {
                    foreach (FilterEntry filter in slot.Filters)
                    {
                        if (Learn(filter))
                        {
                            learned++;
                        }
                    }
                }
            }
        }

        return learned;
    }

    /// <summary>Learns one definition. Existing entries are kept.</summary>
    public bool Learn(FilterEntry filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        string shader = filter.Shader;
        if (string.IsNullOrEmpty(shader) || _entries.ContainsKey(shader))
        {
            return false;
        }

        JsonObject skeleton = filter.DetachClone();

        // A skeleton is a template, not a saved look: reset every control to
        // its default so adding a filter never silently imports someone else's
        // values.
        var template = new FilterEntry(skeleton);
        foreach (ControlEntry control in template.Controls)
        {
            control.ResetToDefault();
        }

        template.StackIndex = 0;
        template.IsSelected = true;

        _entries[shader] = new CatalogueEntry(shader, Share.FilterNames.ForShader(shader), skeleton);
        return true;
    }

    /// <summary>Loads previously learned definitions, ignoring a missing or damaged cache.</summary>
    public void LoadCache(string? path = null)
    {
        string cachePath = path ?? DefaultCachePath;
        if (!File.Exists(cachePath))
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(cachePath)) is not JsonObject root)
            {
                return;
            }

            foreach ((string shader, JsonNode? node) in root)
            {
                if (node is JsonObject skeleton && !_entries.ContainsKey(shader))
                {
                    _entries[shader] = new CatalogueEntry(
                        shader, Share.FilterNames.ForShader(shader), skeleton);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged cache is a convenience lost, not a failure: everything
            // in it can be relearned from the store.
        }
    }

    /// <summary>Persists what has been learned.</summary>
    public void SaveCache(string? path = null)
    {
        string cachePath = path ?? DefaultCachePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            var root = new JsonObject();
            foreach ((string shader, CatalogueEntry entry) in _entries)
            {
                root[shader] = (JsonObject)JsonNode.Parse(entry.Skeleton.ToJsonString())!;
            }

            File.WriteAllText(cachePath, root.ToJsonString(CacheOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting the user over.
        }
    }
}
