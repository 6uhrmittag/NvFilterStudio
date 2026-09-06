using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>One filter in a slot's stack.</summary>
public sealed class FilterEntry(JsonObject node)
{
    private readonly JsonObject _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>The underlying node, for callers that need verbatim access.</summary>
    public JsonObject Node => _node;

    /// <summary>
    /// The filter identifier: an absolute path into the versioned DriverStore.
    /// </summary>
    /// <remarks>
    /// Not a real file. The NvCamera directory holds the runtime and no
    /// <c>.fx</c> files at all — the effects are embedded. The path is a
    /// logical id that happens to carry the driver's DriverStore hash.
    /// </remarks>
    public string Id
    {
        get => _node["id"]?.GetValue<string>() ?? string.Empty;
        set => _node["id"] = value;
    }

    /// <summary>Shader file name, e.g. <c>Details.fx</c>. The stable identity.</summary>
    public string Shader => Path.GetFileName(Id);

    /// <summary>Localised filter name as the overlay wrote it.</summary>
    public string LocalizedName => _node["name"]?.GetValue<string>() ?? Shader;

    /// <summary>Position in the stack; applied low to high.</summary>
    public int StackIndex
    {
        get => _node["stackIdx"]?.GetValue<int>() ?? 0;
        set => _node["stackIdx"] = value;
    }

    /// <summary>Whether the overlay counts this filter as selected.</summary>
    public bool IsSelected
    {
        get => _node["isSelected"]?.GetValue<bool>() ?? false;
        set => _node["isSelected"] = value;
    }

    /// <summary>Controls in declaration order.</summary>
    public IReadOnlyList<ControlEntry> Controls =>
        _node["controls"] is JsonArray controls
            ? [.. controls.OfType<JsonObject>().Select(c => new ControlEntry(c))]
            : [];

    /// <summary>Gets a control by its id.</summary>
    public ControlEntry? GetControl(int id) => Controls.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Rewrites the DriverStore directory portion of <see cref="Id"/>.
    /// </summary>
    /// <remarks>
    /// Only for when the target machine's directory genuinely differs. Keeping
    /// the hash a store already uses is correct by default, because that is
    /// what the local NvCamera reports.
    /// </remarks>
    public void RebaseShaderDirectory(string nvCameraDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nvCameraDirectory);
        Id = Path.Combine(nvCameraDirectory, Shader);
    }

    /// <summary>A detached deep copy of this entry's node.</summary>
    public JsonObject DetachClone() =>
        (JsonObject)JsonNode.Parse(_node.ToJsonString())!;
}
