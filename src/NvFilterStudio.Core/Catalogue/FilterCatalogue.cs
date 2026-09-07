using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Catalogue;

/// <summary>A filter definition the app can insert into a slot.</summary>
/// <param name="Shader">Shader file name, the stable identity.</param>
/// <param name="DisplayName">Friendly name for the UI.</param>
/// <param name="Skeleton">A complete filter node, ready to add to a stack.</param>
public sealed record CatalogueEntry(string Shader, string DisplayName, JsonObject Skeleton)
{
    /// <summary>The friendly name, for anything that renders this directly.</summary>
    /// <remarks>
    /// A record's generated <c>ToString</c> prints every property, which here
    /// means the whole skeleton — several kilobytes of JSON. WPF falls back to
    /// it for a combo item's automation name, so a screen reader read the entire
    /// filter definition aloud for every entry in the list.
    /// </remarks>
    public override string ToString() => DisplayName;
}

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
    /// <param name="shader">Shader file name.</param>
    /// <param name="nvCameraDirectory">
    /// Where this machine's shaders live, used to rebuild the filter id. Pass
    /// null to leave the id as stored.
    /// </param>
    /// <remarks>
    /// The id is an absolute path carrying the driver's DriverStore hash, so a
    /// shipped definition cannot hold a usable one — the seed stores a bare file
    /// name and it is rebased here. Take the directory from a filter already in
    /// the user's own document: that is what their overlay actually wrote, which
    /// beats probing the DriverStore and guessing between installed versions.
    /// </remarks>
    public JsonObject? CreateSkeleton(string shader, string? nvCameraDirectory = null)
    {
        if (!_entries.TryGetValue(shader, out CatalogueEntry? entry))
        {
            return null;
        }

        var skeleton = (JsonObject)JsonNode.Parse(entry.Skeleton.ToJsonString())!;

        if (!string.IsNullOrWhiteSpace(nvCameraDirectory))
        {
            new FilterEntry(skeleton).RebaseShaderDirectory(nvCameraDirectory);
        }

        return skeleton;
    }

    /// <summary>
    /// The directory this document's filters are loaded from, if any say.
    /// </summary>
    /// <remarks>
    /// Every filter in a store shares one DriverStore directory, so the first
    /// one that has a rooted path answers for all of them.
    /// </remarks>
    public static string? FindShaderDirectory(FilterPresetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (GameProfile game in document.Games())
        {
            foreach (SlotGroup group in game.Groups())
            {
                foreach (Slot slot in group.Slots)
                {
                    foreach (FilterEntry filter in slot.Filters)
                    {
                        if (Path.IsPathRooted(filter.Id) &&
                            Path.GetDirectoryName(filter.Id) is { Length: > 0 } directory)
                        {
                            return directory;
                        }
                    }
                }
            }
        }

        return null;
    }

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

    /// <summary>Learns one definition from the store, replacing any cached one.</summary>
    /// <remarks>
    /// The store is authoritative and a learned definition is only a cache of
    /// it, so a definition read from the document always wins. Keeping the first
    /// sighting instead meant a definition could never be corrected: a control
    /// whose bounds changed with a driver update, or a name learned from a bad
    /// record, was stuck permanently. Since the overlay honours whatever bounds
    /// it is given (#12), a stale definition produces a wrong filter rather than
    /// a corrected one, and nothing about it looks wrong.
    /// </remarks>
    /// <returns>True when this taught the catalogue something it did not have.</returns>
    public bool Learn(FilterEntry filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        string shader = filter.Shader;
        if (string.IsNullOrEmpty(shader))
        {
            return false;
        }

        bool isNew = !_entries.ContainsKey(shader);

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

        // Only a genuinely new shader counts, so callers do not rewrite the
        // cache file on every read just because definitions were refreshed.
        return isNew;
    }

    /// <summary>Resource name of the definitions shipped with the app.</summary>
    private const string SeedResource =
        "NvFilterStudio.Core.Catalogue.filter-catalogue.seed.json";

    /// <summary>
    /// Loads the definitions shipped with the app.
    /// </summary>
    /// <remarks>
    /// Definitions only ever enter a store once a filter has been used, so on a
    /// fresh machine the catalogue would be empty and adding a filter simply
    /// unavailable. These were harvested from a real store by putting every
    /// filter into one slot.
    /// <para>
    /// Anything already known wins, because both the user's own store and their
    /// cache are closer to their machine than a snapshot taken on someone
    /// else's: their driver may differ (#11), and their labels are in their own
    /// language rather than the English used here.
    /// </para>
    /// <para>
    /// The labels are English on purpose. The overlay renders whatever
    /// <c>displayName</c> it is given and never substitutes its own, so omitting
    /// them would leave the user's overlay showing blank slider labels.
    /// </para>
    /// </remarks>
    /// <returns>How many definitions the seed contributed.</returns>
    public int LoadSeed()
    {
        using Stream? stream = typeof(FilterCatalogue).Assembly
            .GetManifestResourceStream(SeedResource);

        if (stream is null)
        {
            return 0;
        }

        try
        {
            if (JsonNode.Parse(stream) is not JsonObject root)
            {
                return 0;
            }

            int added = 0;
            foreach ((string shader, JsonNode? node) in root)
            {
                if (node is JsonObject skeleton && !_entries.ContainsKey(shader))
                {
                    _entries[shader] = new CatalogueEntry(
                        shader, Share.FilterNames.ForShader(shader), skeleton);
                    added++;
                }
            }

            return added;
        }
        catch (JsonException)
        {
            // A broken seed is a build problem, not the user's; everything in it
            // can still be learned from their own store.
            return 0;
        }
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
