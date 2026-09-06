namespace NvFilterStudio.Core.LevelDb;

/// <summary>
/// Base-128 varints, LSB first with the high bit as a continuation flag.
/// </summary>
/// <remarks>
/// Used for key and value lengths inside write batches, for the V8 string
/// length inside a stored value, and for every field in a MANIFEST VersionEdit.
/// A desynchronised varint does not throw — it silently yields a plausible
/// wrong number — which is why <see cref="Read"/> is strict about overlong
/// encodings and buffer overruns.
/// </remarks>
public static class Varint
{
    /// <summary>Maximum bytes a 64-bit varint can occupy.</summary>
    public const int MaxBytes = 10;

    /// <summary>
    /// Reads a varint from <paramref name="data"/> starting at
    /// <paramref name="offset"/>, advancing <paramref name="offset"/> past it.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The varint runs past the end of the buffer or exceeds 64 bits.
    /// </exception>
    public static ulong Read(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        int shift = 0;

        while (true)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException("Varint runs past the end of the buffer.");
            }

            if (shift > 63)
            {
                throw new InvalidDataException("Varint is longer than 64 bits.");
            }

            byte b = data[offset++];
            result |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }
    }

    /// <summary>
    /// Reads a varint without throwing. Returns <see langword="false"/> and
    /// leaves <paramref name="offset"/> untouched if the encoding is bad.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        int start = offset;
        try
        {
            value = Read(data, ref offset);
            return true;
        }
        catch (InvalidDataException)
        {
            offset = start;
            value = 0;
            return false;
        }
    }

    /// <summary>Encoded length of <paramref name="value"/> in bytes.</summary>
    public static int SizeOf(ulong value)
    {
        int n = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            n++;
        }

        return n;
    }

    /// <summary>Appends <paramref name="value"/> to <paramref name="destination"/>.</summary>
    public static void Write(List<byte> destination, ulong value)
    {
        ArgumentNullException.ThrowIfNull(destination);

        while (value >= 0x80)
        {
            destination.Add((byte)(value | 0x80));
            value >>= 7;
        }

        destination.Add((byte)value);
    }

    /// <summary>Encodes <paramref name="value"/> to a new array.</summary>
    public static byte[] ToBytes(ulong value)
    {
        var buffer = new List<byte>(MaxBytes);
        Write(buffer, value);
        return [.. buffer];
    }
}
