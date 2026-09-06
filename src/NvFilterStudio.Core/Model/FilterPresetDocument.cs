using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>
/// The filter-preset document, wrapping the store's JSON as a mutable DOM.
/// </summary>
/// <remarks>
/// Deliberately a <see cref="JsonNode"/> tree rather than a set of POCOs.
/// The store holds fields this project has not characterised, and mapping to
/// typed objects would silently drop them on the way back — corrupting a
/// profile in a way nothing would notice until the overlay rendered it wrong.
/// Editing the DOM in place changes only what was asked for.
/// </remarks>
public sealed class FilterPresetDocument
{
    private const string FilterPresetsProperty = "filterPresets";

    private readonly JsonObject _root;

    private FilterPresetDocument(JsonObject root) => _root = root;

    /// <summary>Parses a document.</summary>
    /// <exception cref="InvalidDataException">Not a filter-preset document.</exception>
    public static FilterPresetDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? node = JsonNode.Parse(json)
            ?? throw new InvalidDataException("Document is empty.");

        if (node is not JsonObject root || root[FilterPresetsProperty] is not JsonObject)
        {
            throw new InvalidDataException("Document has no filterPresets object.");
        }

        return new FilterPresetDocument(root);
    }

    private JsonObject Presets => (JsonObject)_root[FilterPresetsProperty]!;

    /// <summary>Executable paths that have presets, in document order.</summary>
    public IReadOnlyList<string> GameKeys => [.. Presets.Select(p => p.Key)];

    /// <summary>Gets a game profile by exact executable path.</summary>
    public GameProfile? GetGame(string exePath) =>
        Presets[exePath] is JsonObject game ? new GameProfile(exePath, game) : null;

    /// <summary>
    /// Finds a profile by executable file name, for restoring onto a machine
    /// where the game lives on a different drive.
    /// </summary>
    public GameProfile? FindGameByExecutable(string executableName)
    {
        ArgumentNullException.ThrowIfNull(executableName);

        foreach ((string key, JsonNode? value) in Presets)
        {
            if (value is JsonObject game &&
                string.Equals(Path.GetFileName(key), executableName, StringComparison.OrdinalIgnoreCase))
            {
                return new GameProfile(key, game);
            }
        }

        return null;
    }

    /// <summary>All game profiles.</summary>
    public IEnumerable<GameProfile> Games()
    {
        foreach ((string key, JsonNode? value) in Presets)
        {
            if (value is JsonObject game)
            {
                yield return new GameProfile(key, game);
            }
        }
    }

    /// <summary>Adds an empty profile for <paramref name="exePath"/> if absent.</summary>
    public GameProfile EnsureGame(string exePath)
    {
        if (GetGame(exePath) is { } existing)
        {
            return existing;
        }

        var game = new JsonObject();
        Presets[exePath] = game;
        return new GameProfile(exePath, game);
    }

    /// <summary>
    /// Options chosen to reproduce NVIDIA's own output byte for byte.
    /// </summary>
    /// <remarks>
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> matters more
    /// than it looks. By default <c>System.Text.Json</c> escapes non-ASCII, so
    /// a German UI's <c>Schärfen</c> comes back as <c>Schärfen</c> — still
    /// valid JSON, but it inflates the record and stops the document being
    /// byte-identical to what the overlay wrote. Matching exactly keeps the
    /// round-trip test meaningful: any difference at all is then a real defect
    /// rather than a formatting artefact.
    /// <para>
    /// "Unsafe" refers to HTML-injection contexts. This value is written into a
    /// local binary store and never into markup, so the relaxed encoder is the
    /// correct choice here.
    /// </para>
    /// <para>
    /// Escaping instead would sidestep the Latin-1 limit in
    /// <c>StoreValueCodec</c>, since <c>\uXXXX</c> is pure ASCII. That is a
    /// plausible future route for non-Latin-1 UI languages, but it deviates
    /// from NVIDIA's format and is unverified against the overlay.
    /// </para>
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Serialises back to the compact form the store uses.
    /// </summary>
    /// <remarks>
    /// No indentation: the stored document is compact, and its byte length is
    /// what the value's length varint describes.
    /// </remarks>
    public string ToJson() => _root.ToJsonString(SerializerOptions);

    /// <summary>An independent copy, for edit-then-discard flows.</summary>
    public FilterPresetDocument Clone() => Parse(ToJson());
}
