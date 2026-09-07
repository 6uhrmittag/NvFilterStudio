using System.Text;
using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Store;

/// <summary>How V8 encoded the JSON string inside a stored value.</summary>
public enum StoreStringEncoding
{
    /// <summary>Tag <c>0x22</c>: one byte per character, Latin-1.</summary>
    OneByte,

    /// <summary>Tag <c>0x63</c>: two bytes per character, UTF-16 little-endian.</summary>
    TwoByte,
}

/// <summary>A decoded IndexedDB value holding the filter-preset JSON.</summary>
/// <param name="Json">The JSON document as stored.</param>
/// <param name="Version">IndexedDB value version, incremented on every write.</param>
/// <param name="Encoding">Which V8 string form the document was stored in.</param>
public readonly record struct StoreValue(string Json, ulong Version, StoreStringEncoding Encoding);

/// <summary>
/// Encodes and decodes the IndexedDB value that wraps the filter-preset JSON.
/// </summary>
/// <remarks>
/// Layout, established by diffing two real store versions that differed by a
/// single slider (JSON 9088 vs 9090 bytes):
/// <code>
/// varint(version) FF 15 FE 00*12 FF 0F &lt;tag&gt; varint(byteLength) &lt;payload&gt;
///   28 -> 2A                                22    80 47 -> 82 47
/// </code>
/// <c>FF 15</c> is a structured-clone v21 header and <c>FF 0F</c> a nested v15
/// header. The tag says how the string was encoded:
/// <list type="bullet">
/// <item><c>0x22</c> — one byte per character (Latin-1). What a German or
/// English NVIDIA App produces, with non-ASCII stored raw rather than
/// <c>\u</c>-escaped, so <c>Schärfen</c> carries byte <c>0xE4</c>.</item>
/// <item><c>0x63</c> — two bytes per character (UTF-16LE). What V8 must use
/// once any character exceeds U+00FF, so a Cyrillic, CJK or Polish App
/// produces this.</item>
/// </list>
/// In both cases the varint is a length in <em>bytes</em>, not characters.
/// <para>
/// The encoding used on write follows the content, not the content's origin: a
/// document is written back as two-byte only if it actually needs to be. That
/// keeps a Latin-1 store byte-identical to what NVIDIA itself writes.
/// </para>
/// </remarks>
public static class StoreValueCodec
{
    /// <summary>Constant bytes between the version varint and the string tag.</summary>
    private static ReadOnlySpan<byte> Preamble =>
    [
        0xFF, 0x15, 0xFE,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0x0F,
    ];

    /// <summary>V8 tag for a one-byte (Latin-1) string.</summary>
    private const byte OneByteStringTag = 0x22;

    /// <summary>V8 tag for a two-byte (UTF-16LE) string.</summary>
    private const byte TwoByteStringTag = 0x63;

    /// <summary>Marker that identifies a filter-preset document.</summary>
    public const string JsonMarker = "{\"filterPresets\":";

    /// <summary>
    /// Extracts the JSON, version and string encoding from a stored value.
    /// </summary>
    /// <remarks>
    /// Parsed structurally rather than by scanning for the marker. A two-byte
    /// document is UTF-16LE, so a Latin-1 scan would not find the marker at all
    /// and the value would look like "not a preset" rather than like something
    /// that needs different handling.
    /// </remarks>
    /// <exception cref="InvalidDataException">Not a preset value.</exception>
    public static StoreValue Decode(ReadOnlyMemory<byte> value)
    {
        ReadOnlySpan<byte> span = value.Span;

        int pos = 0;
        ulong version = Varint.Read(span, ref pos);

        if (!span[pos..].StartsWith(Preamble))
        {
            throw new InvalidDataException("Stored value does not carry the preset preamble.");
        }

        pos += Preamble.Length;

        if (pos >= span.Length)
        {
            throw new InvalidDataException("Stored value ends before its string tag.");
        }

        byte tag = span[pos++];
        StoreStringEncoding encoding = tag switch
        {
            OneByteStringTag => StoreStringEncoding.OneByte,
            TwoByteStringTag => StoreStringEncoding.TwoByte,
            _ => throw new InvalidDataException(
                $"Unexpected V8 string tag 0x{tag:X2}; expected 0x22 or 0x63."),
        };

        long byteLength = (long)Varint.Read(span, ref pos);
        if (byteLength < 0 || pos + byteLength > span.Length)
        {
            throw new InvalidDataException("Stored string length runs past the end of the value.");
        }

        ReadOnlySpan<byte> payload = span.Slice(pos, (int)byteLength);
        string json = encoding == StoreStringEncoding.OneByte
            ? Encoding.Latin1.GetString(payload)
            : Encoding.Unicode.GetString(payload);

        if (!json.StartsWith(JsonMarker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Stored value contains no filterPresets JSON.");
        }

        return new StoreValue(json, version, encoding);
    }

    /// <summary>
    /// Builds a stored value around <paramref name="json"/>, choosing the string
    /// encoding the content requires.
    /// </summary>
    public static byte[] Encode(string json, ulong version)
    {
        ArgumentNullException.ThrowIfNull(json);

        bool needsTwoByte = RequiresTwoByte(json);

        byte[] payload = needsTwoByte
            ? Encoding.Unicode.GetBytes(json)   // UTF-16LE
            : Encoding.Latin1.GetBytes(json);

        var buffer = new List<byte>(payload.Length + 32);
        Varint.Write(buffer, version);
        buffer.AddRange(Preamble);
        buffer.Add(needsTwoByte ? TwoByteStringTag : OneByteStringTag);

        // Length is in bytes for both forms, so a two-byte string reports twice
        // its character count.
        Varint.Write(buffer, (ulong)payload.Length);
        buffer.AddRange(payload);

        return [.. buffer];
    }

    /// <summary>Whether <paramref name="json"/> needs V8's two-byte string form.</summary>
    public static bool RequiresTwoByte(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        foreach (char c in json)
        {
            if (c > 0xFF)
            {
                return true;
            }
        }

        return false;
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

        if (!value[pos..].StartsWith(Preamble))
        {
            return false;
        }

        pos += Preamble.Length;
        return pos < value.Length &&
               (value[pos] == OneByteStringTag || value[pos] == TwoByteStringTag);
    }
}
