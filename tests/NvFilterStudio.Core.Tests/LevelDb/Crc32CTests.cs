using System.Text;
using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Tests.LevelDb;

public class Crc32CTests
{
    [Fact]
    public void Compute_MatchesStandardCastagnoliVector()
    {
        // The canonical CRC32C check value. If this fails the polynomial is
        // wrong and every record written would be rejected by Chromium.
        uint actual = Crc32C.Compute(Encoding.ASCII.GetBytes("123456789"));

        Assert.Equal(0xE3069283u, actual);
    }

    [Fact]
    public void Compute_EmptyInput_IsZero()
    {
        Assert.Equal(0u, Crc32C.Compute([]));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0xE3069283u)]
    [InlineData(0xFFFFFFFFu)]
    public void Mask_RoundTripsThroughUnmask(uint crc)
    {
        Assert.Equal(crc, Crc32C.Unmask(Crc32C.Mask(crc)));
    }

    [Fact]
    public void Mask_DiffersFromRawCrc()
    {
        // The whole point of masking is that a header never contains the plain
        // CRC of its own payload.
        uint crc = Crc32C.Compute(Encoding.ASCII.GetBytes("nvidia"));

        Assert.NotEqual(crc, Crc32C.Mask(crc));
    }

    [Fact]
    public void Compute_IsOrderSensitive()
    {
        uint ab = Crc32C.Compute([0x01, 0x02]);
        uint ba = Crc32C.Compute([0x02, 0x01]);

        Assert.NotEqual(ab, ba);
    }
}
