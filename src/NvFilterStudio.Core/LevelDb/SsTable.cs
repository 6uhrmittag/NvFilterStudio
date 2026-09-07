using System.Buffers.Binary;
using System.IO.Compression;
using Snappier;

namespace NvFilterStudio.Core.LevelDb;

/// <summary>How a table block was stored.</summary>
public enum BlockCompression : byte
{
    /// <summary>Stored as-is.</summary>
    None = 0,

    /// <summary>Snappy, LevelDB's default.</summary>
    Snappy = 1,

    /// <summary>Zlib, as used by some builds.</summary>
    Zlib = 2,

    /// <summary>Zstandard. Not implemented.</summary>
    Zstd = 4,
}

/// <summary>Points at a block within a table file.</summary>
/// <param name="Offset">Byte offset of the block.</param>
/// <param name="Size">Block length, excluding the 5-byte trailer.</param>
public readonly record struct BlockHandle(long Offset, long Size);

/// <summary>One key/value pair recovered from a table.</summary>
/// <param name="InternalKey">Key including LevelDB's 8-byte suffix.</param>
/// <param name="UserKey">Key without that suffix — what callers match on.</param>
/// <param name="Sequence">Sequence number carried in the suffix.</param>
/// <param name="Value">The stored value.</param>
/// <param name="IsDeletion">
/// Whether this entry is a tombstone rather than a value. A deletion can carry
/// a higher sequence than the live record for the same key, so anything picking
/// a winner purely by sequence has to account for it.
/// </param>
public readonly record struct TableEntry(
    ReadOnlyMemory<byte> InternalKey,
    ReadOnlyMemory<byte> UserKey,
    ulong Sequence,
    ReadOnlyMemory<byte> Value,
    bool IsDeletion);

/// <summary>Raised when a table cannot be parsed.</summary>
public sealed class SsTableFormatException(string message) : Exception(message);

/// <summary>
/// Reads LevelDB sorted-table (<c>.ldb</c>) files.
/// </summary>
/// <remarks>
/// Records move into tables whenever the store compacts, which happens every
/// time the NVIDIA Overlay restarts. Scanning a table for the JSON marker
/// recovers the text but not the key, and without the key bytes a record cannot
/// be written back — so a store whose log holds no preset was readable but not
/// editable. Parsing properly fixes that.
/// <para>
/// Layout: a sequence of blocks, then a metaindex block, an index block, and a
/// 48-byte footer ending in the magic <c>0xdb4775248b80fb57</c>. Each block is
/// followed by a 5-byte trailer of compression type and CRC. Within a block,
/// keys are prefix-compressed against the previous key and restart points give
/// the offsets where that compression resets.
/// </para>
/// </remarks>
public static class SsTable
{
    /// <summary>Bytes of footer at the end of every table.</summary>
    public const int FooterSize = 48;

    /// <summary>Trailer following each block: 1 compression byte, 4 CRC bytes.</summary>
    public const int BlockTrailerSize = 5;

    /// <summary>Identifies a LevelDB table.</summary>
    public const ulong Magic = 0xDB4775248B80FB57UL;

    /// <summary>Bytes LevelDB appends to every user key: sequence and type.</summary>
    private const int InternalKeySuffix = 8;

    /// <summary>Whether <paramref name="bytes"/> ends with the table magic.</summary>
    public static bool LooksLikeTable(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= FooterSize &&
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[^8..]) == Magic;

    /// <summary>
    /// Enumerates every key/value pair in the table, in stored order.
    /// </summary>
    /// <exception cref="SsTableFormatException">
    /// The file is not a table, or uses a compression this does not implement.
    /// </exception>
    public static IEnumerable<TableEntry> ReadEntries(ReadOnlyMemory<byte> table)
    {
        BlockHandle index = ReadIndexHandle(table.Span);

        // The index block maps "last key in block" to the handle for that
        // block, so its values are handles rather than user data.
        byte[] indexBlock = ReadBlock(table, index);

        var entries = new List<TableEntry>();

        foreach ((ReadOnlyMemory<byte> _, ReadOnlyMemory<byte> value) in ReadBlockEntries(indexBlock))
        {
            int pos = 0;
            BlockHandle dataHandle = ReadHandle(value.Span, ref pos);

            byte[] dataBlock;
            try
            {
                dataBlock = ReadBlock(table, dataHandle);
            }
            catch (Exception ex) when (ex is SsTableFormatException or InvalidDataException)
            {
                // One unreadable block should not lose the rest of the table.
                continue;
            }

            foreach ((ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> data) in ReadBlockEntries(dataBlock))
            {
                if (key.Length < InternalKeySuffix)
                {
                    continue;
                }

                ReadOnlyMemory<byte> userKey = key[..^InternalKeySuffix];
                ulong tag = BinaryPrimitives.ReadUInt64LittleEndian(key.Span[^InternalKeySuffix..]);

                // Top 56 bits are the sequence, low 8 the record type.
                // Low byte is the record type: 0 deletion, 1 value.
                entries.Add(new TableEntry(key, userKey, tag >> 8, data, (tag & 0xFF) == 0));
            }
        }

        return entries;
    }

    /// <summary>Highest sequence number appearing in the table.</summary>
    public static ulong HighestSequence(ReadOnlyMemory<byte> table)
    {
        ulong highest = 0;

        foreach (TableEntry entry in ReadEntries(table))
        {
            if (entry.Sequence > highest)
            {
                highest = entry.Sequence;
            }
        }

        return highest;
    }

    private static BlockHandle ReadIndexHandle(ReadOnlySpan<byte> table)
    {
        if (!LooksLikeTable(table))
        {
            throw new SsTableFormatException("Not a LevelDB table: magic missing from the footer.");
        }

        // Footer is metaindex handle, index handle, zero padding, then magic.
        ReadOnlySpan<byte> footer = table[^FooterSize..];

        int pos = 0;
        _ = ReadHandle(footer, ref pos);       // metaindex, unused here
        return ReadHandle(footer, ref pos);
    }

    private static BlockHandle ReadHandle(ReadOnlySpan<byte> span, ref int pos)
    {
        ulong offset = Varint.Read(span, ref pos);
        ulong size = Varint.Read(span, ref pos);
        return new BlockHandle((long)offset, (long)size);
    }

    private static byte[] ReadBlock(ReadOnlyMemory<byte> table, BlockHandle handle)
    {
        long end = handle.Offset + handle.Size + BlockTrailerSize;
        if (handle.Offset < 0 || handle.Size < 0 || end > table.Length)
        {
            throw new SsTableFormatException("Block handle points outside the file.");
        }

        ReadOnlyMemory<byte> raw = table.Slice((int)handle.Offset, (int)handle.Size);
        var compression = (BlockCompression)table.Span[(int)(handle.Offset + handle.Size)];

        return compression switch
        {
            BlockCompression.None => raw.ToArray(),

            // LevelDB's default. Blocks are compressed only where it pays, so a
            // real table mixes both - the uncompressed ones are why the JSON is
            // partly visible to a plain byte scan.
            BlockCompression.Snappy => Snappy.DecompressToArray(raw.Span),
            BlockCompression.Zlib => Inflate(raw.Span),

            _ => throw new SsTableFormatException(
                $"Block uses {compression} compression, which is not implemented."),
        };
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Walks the entries of a single block, undoing key prefix compression.
    /// </summary>
    /// <remarks>
    /// Each entry stores how many bytes it shares with the previous key, so the
    /// full key has to be rebuilt as the block is walked. Restart points are
    /// where sharing resets; they are not needed for a straight scan, but the
    /// restart array at the tail must be excluded or its offsets would be
    /// parsed as entries.
    /// </remarks>
    private static IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> ReadBlockEntries(
        byte[] block)
    {
        if (block.Length < sizeof(uint))
        {
            yield break;
        }

        uint restartCount = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(block.Length - sizeof(uint)));

        long restartBytesLong = ((long)restartCount + 1) * sizeof(uint);
        if (restartBytesLong > block.Length)
        {
            yield break;
        }

        int entriesEnd = block.Length - (int)restartBytesLong;
        var previousKey = Array.Empty<byte>();
        int pos = 0;

        while (pos < entriesEnd)
        {
            ulong shared, nonShared, valueLength;

            try
            {
                shared = Varint.Read(block, ref pos);
                nonShared = Varint.Read(block, ref pos);
                valueLength = Varint.Read(block, ref pos);
            }
            catch (InvalidDataException)
            {
                yield break;
            }

            if (shared > (ulong)previousKey.Length ||
                pos + (long)nonShared + (long)valueLength > entriesEnd)
            {
                yield break;
            }

            var key = new byte[(int)shared + (int)nonShared];
            previousKey.AsSpan(0, (int)shared).CopyTo(key);
            block.AsSpan(pos, (int)nonShared).CopyTo(key.AsSpan((int)shared));
            pos += (int)nonShared;

            ReadOnlyMemory<byte> value = block.AsMemory(pos, (int)valueLength);
            pos += (int)valueLength;

            previousKey = key;
            yield return (key, value);
        }
    }
}
