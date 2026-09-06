using System.Text;
using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Store;

/// <summary>A decoded IndexedDB value holding the filter-preset JSON.</summary>
/// <param name="Json">The JSON document as stored.</param>
/// <param name="Version">IndexedDB value version, incremented on every write.</param>
public readonly record struct StoreValue(string Json, ulong Version);

/// <summary>
/// Encodes and decodes the IndexedDB value that wraps the filter-preset JSON.
/// </summary>
/// <remarks>
/// Layout, established by diffing two real store versions that differed by a
/// single slider (JSON 9088 vs 9090 bytes):
/// <code>
/// varint(version) FF 15 FE 00*12 FF 0F 22 varint(jsonLength) &lt;json&gt;
///   28 -> 2A                                80 47 -> 82 47
/// </code>
/// Only the version varint and the string-length varint differ between
/// versions; everything between is byte-for-byte constant.
/// <para>
/// <c>FF 15</c> is a structured-clone v21 header, <c>FF 0F</c> a nested v15
/// header, and <c>22</c> V8's <em>one-byte</em> (Latin-1) string tag. NVIDIA
/// stores non-ASCII raw rather than <c>\u</c>-escaped, so a German UI writes
/// <c>Schärfen</c> with byte <c>0xE4</c>.
/// </para>
/// </remarks>
public static class StoreValueCodec
{
    /// <summary>Constant bytes between the version varint and the length varint.</summary>
    private static ReadOnlySpan<byte> Preamble =>
    [
        0xFF, 0x15, 0xFE,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0x0F, 0x22,
    ];

    /// <summary>Latin-1. The payload is a V8 one-byte string, so this is exact.</summary>
    private static Encoding Latin1 => Encoding.Latin1;

    /// <summary>Marker that identifies a filter-preset document.</summary>
    public const string JsonMarker = "{\"filterPresets\":";

    /// <summary>
    /// Extracts the JSON and version from a stored value.
    /// </summary>
    /// <exception cref="InvalidDataException">No preset JSON present.</exception>
    public static StoreValue Decode(ReadOnlyMemory<byte> value)
    {
        // Latin-1 is byte-preserving, so string offsets match byte offsets.
        string text = Latin1.GetString(value.Span);
        int start = text.IndexOf(JsonMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidDataException("Stored value contains no filterPresets JSON.");
        }

        int pos = 0;
        ulong version = Varint.Read(value.Span, ref pos);
        return new StoreValue(text[start..], version);
    }

    /// <summary>
    /// Builds a stored value around <paramref name="json"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// <paramref name="json"/> contains a character above U+00FF, which would
    /// need V8's two-byte string tag (<c>0x63</c>) rather than <c>0x22</c>.
    /// Refused rather than silently mangled.
    /// </exception>
    public static byte[] Encode(string json, ulong version)
    {
        ArgumentNullException.ThrowIfNull(json);

        foreach (char c in json)
        {
            if (c > 0xFF)
            {
                throw new NotSupportedException(
                    $"JSON contains U+{(int)c:X4}, outside Latin-1. This store uses a V8 " +
                    "one-byte string; two-byte encoding is not implemented. Switch the " +
                    "NVIDIA App UI to a Latin-1 language, or extend StoreValueCodec.");
            }
        }

        byte[] jsonBytes = Latin1.GetBytes(json);

        var buffer = new List<byte>(jsonBytes.Length + 32);
        Varint.Write(buffer, version);
        buffer.AddRange(Preamble);
        Varint.Write(buffer, (ulong)jsonBytes.Length);
        buffer.AddRange(jsonBytes);

        return [.. buffer];
    }

    /// <summary>
    /// Whether a byte range looks like a preset value. Cheap pre-filter before
    /// a full decode.
    /// </summary>
    public static bool LooksLikePresetValue(ReadOnlySpan<byte> value)
    {
        if (value.Length < Preamble.Length + 4)
        {
            return false;
        }

        int pos = 0;
        if (!Varint.TryRead(value, ref pos, out _))
        {
            return false;
        }

        return value[pos..].StartsWith(Preamble);
    }
}
