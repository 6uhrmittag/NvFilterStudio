using System.Text;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

public class StoreValueCodecTests
{
    /// <summary>A document of a chosen byte length, still starting with the marker.</summary>
    private static string JsonOfLength(int length)
    {
        const string Head = "{\"filterPresets\":{\"pad\":\"";
        const string Tail = "\"}}";
        return Head + new string('a', length - Head.Length - Tail.Length) + Tail;
    }

    [Fact]
    public void Encode_ProducesTheExactPrefixObservedOnARealStore()
    {
        // Ground truth. Two real store versions differed only by one slider:
        //   28 FF 15 FE 00*12 FF 0F 22 80 47   version 40, json 9088
        //   2A FF 15 FE 00*12 FF 0F 22 82 47   version 42, json 9090
        // Everything between the two varints is constant.
        byte[] encoded = StoreValueCodec.Encode(JsonOfLength(9088), version: 40);

        byte[] expectedPrefix =
        [
            0x28,
            0xFF, 0x15, 0xFE,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x0F, 0x22,
            0x80, 0x47,
        ];

        Assert.Equal(expectedPrefix, encoded[..21]);
        Assert.Equal(21 + 9088, encoded.Length);
    }

    [Fact]
    public void Encode_SecondObservedSampleAlsoMatches()
    {
        byte[] encoded = StoreValueCodec.Encode(JsonOfLength(9090), version: 42);

        Assert.Equal(0x2A, encoded[0]);
        Assert.Equal(new byte[] { 0x82, 0x47 }, encoded[19..21]);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(42UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]   // version varint grows to two bytes
    [InlineData(100_000UL)]
    public void EncodeThenDecode_RoundTrips(ulong version)
    {
        string json = JsonOfLength(500);

        StoreValue decoded = StoreValueCodec.Decode(StoreValueCodec.Encode(json, version));

        Assert.Equal(json, decoded.Json);
        Assert.Equal(version, decoded.Version);
    }

    [Fact]
    public void EncodeThenDecode_PreservesLatin1Unescaped()
    {
        // NVIDIA stores a German UI raw: "Schärfen" carries byte 0xE4, not ä.
        string json = "{\"filterPresets\":{\"name\":\"Schärfen Überstrahlung\"}}";

        byte[] encoded = StoreValueCodec.Encode(json, 1);

        Assert.Contains((byte)0xE4, encoded);
        Assert.DoesNotContain("\\u", Encoding.Latin1.GetString(encoded), StringComparison.Ordinal);
        Assert.Equal(json, StoreValueCodec.Decode(encoded).Json);
    }

    [Fact]
    public void Encode_AboveLatin1_ThrowsRatherThanMangling()
    {
        // A Cyrillic or CJK UI would need V8's two-byte string tag (0x63).
        // Refusing is the point: silently writing mojibake into the user's
        // store would be worse than failing.
        string json = "{\"filterPresets\":{\"name\":\"Резкость\"}}";

        NotSupportedException ex = Assert.Throws<NotSupportedException>(
            () => StoreValueCodec.Encode(json, 1));

        Assert.Contains("Latin-1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_ValueWithoutMarker_Throws()
    {
        byte[] notAPreset = [0x01, 0xFF, 0x15, 0xFE, 0x00, 0x00];

        Assert.Throws<InvalidDataException>(() => StoreValueCodec.Decode(notAPreset));
    }

    [Fact]
    public void LooksLikePresetValue_RecognisesOwnOutput()
    {
        Assert.True(StoreValueCodec.LooksLikePresetValue(StoreValueCodec.Encode(JsonOfLength(200), 5)));
    }

    [Fact]
    public void LooksLikePresetValue_RejectsUnrelatedBytes()
    {
        Assert.False(StoreValueCodec.LooksLikePresetValue([0x2A, 0x01, 0x02, 0x03, 0x04, 0x05]));
        Assert.False(StoreValueCodec.LooksLikePresetValue([]));
    }

    [Fact]
    public void Decode_ToleratesTrailingBytesAfterTheJson()
    {
        string json = JsonOfLength(300);
        byte[] padded = [.. StoreValueCodec.Encode(json, 7), 0x00, 0x00];

        // Trailing padding must not become part of the document.
        Assert.StartsWith(json, StoreValueCodec.Decode(padded).Json, StringComparison.Ordinal);
    }
}
