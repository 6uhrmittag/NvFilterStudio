using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Share;

/// <summary>One filter inside a share code.</summary>
/// <param name="Shader">Shader file name, the stable identity.</param>
/// <param name="Order">Position in the stack, applied low to high.</param>
/// <param name="Values">Control id to UI value.</param>
public sealed record SharedFilter(string Shader, int Order, IReadOnlyDictionary<int, double> Values);

/// <summary>A decoded share code.</summary>
/// <param name="Game">Display name of the game it came from, for context only.</param>
/// <param name="Label">Label of the slot it came from.</param>
/// <param name="Filters">Filters in stack order.</param>
public sealed record SharedPreset(string Game, string Label, IReadOnlyList<SharedFilter> Filters);

/// <summary>Raised when a share code cannot be read.</summary>
public sealed class InvalidShareCodeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InvalidShareCodeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception, preserving the underlying cause.</summary>
    public InvalidShareCodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Compact, pasteable representation of one slot's look.
/// </summary>
/// <remarks>
/// A full export is tens of kilobytes because every filter carries a verbatim
/// skeleton, which is unusable in a chat message. A share code carries only the
/// shape of the look — which filters, in what order, at what values — and is
/// rebuilt on import against the recipient's own filter definitions.
/// <para>
/// Format: <c>NVF1:</c> followed by URL-safe base64 of Deflate-compressed
/// minimal JSON. The version prefix means the format can change without
/// old codes being misread as new ones.
/// </para>
/// </remarks>
public static class ShareCode
{
    /// <summary>Prefix identifying version 1 codes.</summary>
    public const string Prefix = "NVF1:";

    /// <summary>Builds a share code for one slot.</summary>
    public static string Encode(string game, Slot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var filters = new JsonArray();
        foreach (FilterEntry filter in slot.Filters)
        {
            var values = new JsonObject();
            foreach (ControlEntry control in filter.Controls)
            {
                values[control.Id.ToString(CultureInfo.InvariantCulture)] = control.UiValue;
            }

            filters.Add(new JsonObject
            {
                ["s"] = filter.Shader,
                ["o"] = filter.StackIndex,
                ["v"] = values,
            });
        }

        var payload = new JsonObject
        {
            ["g"] = game,
            ["l"] = slot.Label,
            ["f"] = filters,
        };

        byte[] json = Encoding.UTF8.GetBytes(payload.ToJsonString());

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(json);
        }

        return Prefix + ToBase64Url(output.ToArray());
    }

    /// <summary>Reads a share code.</summary>
    /// <exception cref="InvalidShareCodeException">Malformed or unknown version.</exception>
    public static SharedPreset Decode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        string trimmed = code.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidShareCodeException(
                $"Not a share code. Expected it to start with '{Prefix}'.");
        }

        byte[] compressed;
        try
        {
            compressed = FromBase64Url(trimmed[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new InvalidShareCodeException("Share code is not valid base64 — was it truncated?", ex);
        }

        string json;
        try
        {
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidShareCodeException("Share code is damaged — was it truncated?", ex);
        }

        try
        {
            JsonObject payload = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidShareCodeException("Share code payload is not an object.");

            var filters = new List<SharedFilter>();
            foreach (JsonNode? node in payload["f"] as JsonArray ?? [])
            {
                if (node is not JsonObject filter)
                {
                    continue;
                }

                var values = new Dictionary<int, double>();
                foreach ((string key, JsonNode? value) in filter["v"] as JsonObject ?? [])
                {
                    if (int.TryParse(key, CultureInfo.InvariantCulture, out int id) && value is not null)
                    {
                        values[id] = value.GetValue<double>();
                    }
                }

                filters.Add(new SharedFilter(
                    filter["s"]?.GetValue<string>() ?? string.Empty,
                    filter["o"]?.GetValue<int>() ?? 0,
                    values));
            }

            return new SharedPreset(
                payload["g"]?.GetValue<string>() ?? string.Empty,
                payload["l"]?.GetValue<string>() ?? string.Empty,
                [.. filters.OrderBy(f => f.Order)]);
        }
        catch (JsonException ex)
        {
            // A partially-copied code often still inflates, just into cut-off
            // JSON — so this path is reached far more often by a truncated
            // paste than by genuine corruption. Say the likely thing.
            throw new InvalidShareCodeException(
                "Share code is incomplete — it looks truncated. Copy the whole code, " +
                "including everything after 'NVF1:'.", ex);
        }
    }

    // Base64url: '+' and '/' are mangled by chat clients and URLs, and '=' is
    // noise when the length is implied.
    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
