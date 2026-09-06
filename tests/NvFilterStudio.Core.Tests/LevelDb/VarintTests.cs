using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Tests.LevelDb;

public class VarintTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(127UL)]        // last single-byte value
    [InlineData(128UL)]        // first two-byte value
    [InlineData(16383UL)]      // last two-byte value
    [InlineData(16384UL)]      // first three-byte value
    [InlineData(9088UL)]       // a real observed JSON length
    [InlineData(9090UL)]
    [InlineData(ulong.MaxValue)]
    public void RoundTrips(ulong value)
    {
        byte[] encoded = Varint.ToBytes(value);

        int offset = 0;
        ulong decoded = Varint.Read(encoded, ref offset);

        Assert.Equal(value, decoded);
        Assert.Equal(encoded.Length, offset);
        Assert.Equal(encoded.Length, Varint.SizeOf(value));
    }

    [Fact]
    public void Encoding_MatchesObservedStoreBytes()
    {
        // The real store encoded JSON lengths 9088 and 9090 as 80 47 and 82 47.
        Assert.Equal(new byte[] { 0x80, 0x47 }, Varint.ToBytes(9088));
        Assert.Equal(new byte[] { 0x82, 0x47 }, Varint.ToBytes(9090));
    }

    [Fact]
    public void Read_AdvancesOffsetPastOnlyTheVarint()
    {
        byte[] buffer = [.. Varint.ToBytes(300), 0xAA, 0xBB];

        int offset = 0;
        ulong value = Varint.Read(buffer, ref offset);

        Assert.Equal(300UL, value);
        Assert.Equal(0xAA, buffer[offset]);
    }

    [Fact]
    public void Read_TruncatedInput_Throws()
    {
        // Continuation bit set but nothing follows.
        byte[] truncated = [0x80];
        int offset = 0;

        Assert.Throws<InvalidDataException>(() =>
        {
            int local = offset;
            Varint.Read(truncated, ref local);
        });
    }

    [Fact]
    public void Read_OverlongEncoding_Throws()
    {
        byte[] overlong = [.. Enumerable.Repeat((byte)0x80, 11), (byte)0x01];
        int offset = 0;

        Assert.Throws<InvalidDataException>(() =>
        {
            int local = offset;
            Varint.Read(overlong, ref local);
        });
    }

    [Fact]
    public void TryRead_BadInput_LeavesOffsetUntouched()
    {
        byte[] truncated = [0xFF, 0x80];
        int offset = 0;

        bool ok = Varint.TryRead(truncated, ref offset, out ulong value);

        Assert.False(ok);
        Assert.Equal(0, offset);
        Assert.Equal(0UL, value);
    }
}
