namespace NvFilterStudio.Core.LevelDb;

/// <summary>
/// CRC32C (Castagnoli) with LevelDB's record masking.
/// </summary>
/// <remarks>
/// LevelDB stores a <em>masked</em> CRC in every log record header, not the raw
/// value. Getting either the polynomial or the mask wrong produces records that
/// Chromium silently discards on replay, so both are covered by tests:
/// <c>Crc32C.Compute("123456789") == 0xE3069283</c> is the standard vector, and
/// the masking is validated against thousands of records from a real store.
/// </remarks>
public static class Crc32C
{
    /// <summary>Reflected Castagnoli polynomial.</summary>
    private const uint Polynomial = 0x82F63B78u;

    /// <summary>LevelDB's mask delta, from <c>util/crc32c.h</c>.</summary>
    private const uint MaskDelta = 0xA282EAD8u;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int bit = 0; bit < 8; bit++)
            {
                c = (c & 1) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }

    /// <summary>Raw CRC32C over <paramref name="data"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// Rotates and biases a CRC so that a record header never contains the CRC
    /// of its own payload, which would make corruption self-consistent.
    /// </summary>
    public static uint Mask(uint crc) => ((crc >> 15) | (crc << 17)) + MaskDelta;

    /// <summary>Reverses <see cref="Mask"/>.</summary>
    public static uint Unmask(uint masked)
    {
        uint rot = masked - MaskDelta;
        return (rot >> 17) | (rot << 15);
    }

    /// <summary>Masked CRC as written into a LevelDB record header.</summary>
    public static uint ComputeMasked(ReadOnlySpan<byte> data) => Mask(Compute(data));
}
