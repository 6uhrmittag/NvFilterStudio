using System.Text.Json;
using System.Text.Json.Nodes;
using NvFilterStudio.Core.Catalogue;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Share;

/// <summary>
/// Applies a share code or an export file onto a store document.
/// </summary>
/// <remarks>
/// The two inputs differ in how self-sufficient they are. An export file
/// carries a verbatim <c>native</c> skeleton per filter, so it can rebuild a
/// stack unaided. A share code carries only shader, order and values, so it
/// must be paired with a definition the local machine already knows.
/// <para>
/// Neither path ever invents control metadata. A filter with no known
/// definition is reported back so the caller can say so, rather than writing a
/// guessed record into someone's store.
/// </para>
/// </remarks>
public static class ImportPlan
{
    /// <summary>
    /// Rebuilds <paramref name="target"/> from a share code.
    /// </summary>
    /// <param name="preset">Decoded share code.</param>
    /// <param name="target">Slot to overwrite.</param>
    /// <param name="catalogue">Known filter definitions.</param>
    /// <param name="missing">Shaders that had no known definition.</param>
    /// <returns>How many filters were applied.</returns>
    public static int ApplyShared(
        SharedPreset preset,
        Slot target,
        FilterCatalogue catalogue,
        out IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(catalogue);

        var unknown = new List<string>();
        var built = new List<JsonObject>();

        foreach (SharedFilter shared in preset.Filters.OrderBy(f => f.Order))
        {
            JsonObject? skeleton = catalogue.CreateSkeleton(shared.Shader);
            if (skeleton is null)
            {
                unknown.Add(shared.Shader);
                continue;
            }

            var entry = new FilterEntry(skeleton);
            foreach (ControlEntry control in entry.Controls)
            {
                if (shared.Values.TryGetValue(control.Id, out double value))
                {
                    control.UiValue = value;
                }
            }

            built.Add(skeleton);
        }

        // Only clear once every filter is built, so a partial failure does not
        // destroy the slot the user already had.
        target.Clear();
        foreach (JsonObject skeleton in built)
        {
            target.AddFilter(skeleton);
        }

        missing = unknown;
        return built.Count;
    }

    /// <summary>
    /// Applies an export file onto <paramref name="document"/>.
    /// </summary>
    /// <returns>How many slots were replaced.</returns>
    /// <exception cref="InvalidDataException">Not a recognisable export.</exception>
    public static int ApplyFile(string json, FilterPresetDocument document, FilterCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalogue);

        JsonNode root;
        try
        {
            root = JsonNode.Parse(json) ?? throw new InvalidDataException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("That file is not valid JSON.", ex);
        }

        if (root["profiles"] is not JsonArray profiles)
        {
            throw new InvalidDataException(
                "That file has no 'profiles' — is it an NvFilterStudio export?");
        }

        int applied = 0;

        foreach (JsonNode? profileNode in profiles)
        {
            if (profileNode is not JsonObject profile)
            {
                continue;
            }

            string exePath = profile["exePath"]?.GetValue<string>() ?? string.Empty;
            string executable = profile["executable"]?.GetValue<string>() ?? Path.GetFileName(exePath);

            // Prefer an exact path, then fall back to the file name so a
            // profile restores onto a machine where the game lives elsewhere.
            GameProfile? game = document.GetGame(exePath)
                ?? document.FindGameByExecutable(executable);

            if (game is null)
            {
                continue;
            }

            if (profile["slotGroups"] is not JsonObject groups)
            {
                continue;
            }

            foreach ((string groupName, JsonNode? groupNode) in groups)
            {
                if (groupNode is not JsonObject group ||
                    group["slots"] is not JsonArray slots)
                {
                    continue;
                }

                SlotGroupKind kind = groupName == GameProfile.AnselProperty
                    ? SlotGroupKind.Ansel
                    : SlotGroupKind.GameFilters;

                SlotGroup targetGroup = game.EnsureGroup(kind);

                foreach (JsonNode? slotNode in slots)
                {
                    if (slotNode is not JsonObject slot ||
                        slot["filters"] is not JsonArray filters ||
                        filters.Count == 0)
                    {
                        continue;
                    }

                    int id = slot["id"]?.GetValue<int>() ?? 0;
                    if (id == 0)
                    {
                        continue; // the overlay's "None" entry
                    }

                    Slot target = targetGroup.EnsureSlot(id, slot["label"]?.GetValue<string>());
                    if (ApplyExportedSlot(filters, target, catalogue))
                    {
                        applied++;
                    }
                }
            }
        }

        return applied;
    }

    private static bool ApplyExportedSlot(JsonArray filters, Slot target, FilterCatalogue catalogue)
    {
        var built = new List<JsonObject>();

        foreach (JsonNode? node in filters.OrderBy(f => f?["order"]?.GetValue<int>() ?? 0))
        {
            if (node is not JsonObject filter)
            {
                continue;
            }

            string shader = filter["shader"]?.GetValue<string>() ?? string.Empty;

            // The verbatim skeleton is what makes an export file portable, so
            // prefer it; fall back to a local definition only if absent.
            JsonObject? skeleton = filter["native"] is JsonObject native
                ? (JsonObject)JsonNode.Parse(native.ToJsonString())!
                : catalogue.CreateSkeleton(shader);

            if (skeleton is null)
            {
                continue;
            }

            // Overlay the readable settings so a hand-edited file wins over the
            // values frozen into the skeleton.
            if (filter["settings"] is JsonObject settings)
            {
                ApplySettings(settings, new FilterEntry(skeleton), shader);
            }

            built.Add(skeleton);
        }

        if (built.Count == 0)
        {
            return false;
        }

        target.Clear();
        foreach (JsonObject skeleton in built)
        {
            target.AddFilter(skeleton);
        }

        return true;
    }

    private static void ApplySettings(JsonObject settings, FilterEntry entry, string shader)
    {
        foreach (ControlEntry control in entry.Controls)
        {
            string name = FilterNames.ForControl(shader, control.Id);
            if (settings[name] is { } value)
            {
                control.UiValue = value.GetValue<double>();
            }
        }
    }
}
