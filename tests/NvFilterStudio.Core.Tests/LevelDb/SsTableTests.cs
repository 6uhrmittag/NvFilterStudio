using System.Buffers.Binary;
using System.Text;
using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Tests.LevelDb;

public class SsTableTests
{
    /// <summary>
    /// Builds a minimal but valid LevelDB table.
    /// </summary>
    /// <remarks>
    /// Written rather than captured. A real table would carry the machine
    /// owner's NVIDIA account id, and this repository is public.
    /// </remarks>
    private static byte[] BuildTable(
        IReadOnlyList<(string Key, ulong Sequence, byte[] Value)> entries,
        BlockCompression compression = BlockCompression.None)
    {
        // --- data block: entries then a restart array -----------------------
        var data = new List<byte>();
        var previous = Array.Empty<byte>();
        byte[]? lastInternalKey = null;

        foreach ((string key, ulong sequence, byte[] value) in entries)
        {
            byte[] internalKey = InternalKey(key, sequence);
            lastInternalKey = internalKey;

            int shared = 0;
            while (shared < previous.Length && shared < internalKey.Length &&
                   previous[shared] == internalKey[shared])
            {
                shared++;
            }

            Varint.Write(data, (ulong)shared);
            Varint.Write(data, (ulong)(internalKey.Length - shared));
            Varint.Write(data, (ulong)value.Length);
            data.AddRange(internalKey[shared..]);
            data.AddRange(value);

            previous = internalKey;
        }

        AppendRestartArray(data);
        byte[] dataBlock = MaybeCompress([.. data], compression);

        var table = new List<byte>();
        int dataOffset = table.Count;
        table.AddRange(dataBlock);
        AppendTrailer(table, compression);

        // --- index block: last key in the block -> its handle ---------------
        var index = new List<byte>();
        var handle = new List<byte>();
        Varint.Write(handle, (ulong)dataOffset);
        Varint.Write(handle, (ulong)dataBlock.Length);

        byte[] indexKey = lastInternalKey ?? InternalKey("z", 0);
        Varint.Write(index, 0);
        Varint.Write(index, (ulong)indexKey.Length);
        Varint.Write(index, (ulong)handle.Count);
        index.AddRange(indexKey);
        index.AddRange(handle);
        AppendRestartArray(index);

        // Metaindex is required by the layout but may be empty.
        var meta = new List<byte>();
        AppendRestartArray(meta);

        int metaOffset = table.Count;
        table.AddRange(meta);
        AppendTrailer(table, BlockCompression.None);

        int indexOffset = table.Count;
        table.AddRange(index);
        AppendTrailer(table, BlockCompression.None);

        // --- footer: two handles, zero padding, magic -----------------------
        var footer = new List<byte>();
        Varint.Write(footer, (ulong)metaOffset);
        Varint.Write(footer, (ulong)meta.Count);
        Varint.Write(footer, (ulong)indexOffset);
        Varint.Write(footer, (ulong)index.Count);

        while (footer.Count < SsTable.FooterSize - 8)
        {
            footer.Add(0);
        }

        Span<byte> magic = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(magic, SsTable.Magic);
        footer.AddRange(magic);

        table.AddRange(footer);
        return [.. table];
    }

    private static byte[] MaybeCompress(byte[] block, BlockCompression compression) =>
        compression == BlockCompression.Snappy ? Snappier.Snappy.CompressToArray(block) : block;

    private static void AppendRestartArray(List<byte> block)
    {
        // One restart point at offset 0, then the count.
        Span<byte> tail = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(tail, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(tail[4..], 1);
        block.AddRange(tail);
    }

    private static void AppendTrailer(List<byte> table, BlockCompression compression)
    {
        table.Add((byte)compression);
        table.AddRange(new byte[4]); // CRC, not verified on read
    }

    /// <summary>User key plus LevelDB's 8-byte sequence and type suffix.</summary>
    private static byte[] InternalKey(string userKey, ulong sequence)
    {
        byte[] user = Encoding.ASCII.GetBytes(userKey);
        var key = new byte[user.Length + 8];
        user.CopyTo(key, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(user.Length), (sequence << 8) | 1);
        return key;
    }

    [Fact]
    public void LooksLikeTable_RecognisesTheMagic()
    {
        byte[] table = BuildTable([("a", 1, [0x01])]);

        Assert.True(SsTable.LooksLikeTable(table));
    }

    [Fact]
    public void LooksLikeTable_RejectsSomethingElse()
    {
        Assert.False(SsTable.LooksLikeTable(Encoding.ASCII.GetBytes("not a table at all, really")));
        Assert.False(SsTable.LooksLikeTable([]));
    }

    [Fact]
    public void ReadEntries_RecoversKeysValuesAndSequences()
    {
        byte[] table = BuildTable(
        [
            ("alpha", 10, Encoding.ASCII.GetBytes("one")),
            ("beta", 20, Encoding.ASCII.GetBytes("two")),
        ]);

        IReadOnlyList<TableEntry> entries = [.. SsTable.ReadEntries(table)];

        Assert.Equal(2, entries.Count);
        Assert.Equal("alpha", Encoding.ASCII.GetString(entries[0].UserKey.Span));
        Assert.Equal(10UL, entries[0].Sequence);
        Assert.Equal("two", Encoding.ASCII.GetString(entries[1].Value.Span));
    }

    [Fact]
    public void ReadEntries_UndoesKeyPrefixCompression()
    {
        // Keys sharing a prefix are the case prefix compression exists for, and
        // rebuilding them wrongly yields plausible but incorrect keys.
        byte[] table = BuildTable(
        [
            ("FilterPresets_v1", 1, [0xAA]),
            ("FilterPresets_v2", 2, [0xBB]),
            ("FilterPresets_v3", 3, [0xCC]),
        ]);

        IReadOnlyList<TableEntry> entries = [.. SsTable.ReadEntries(table)];

        Assert.Equal(
            ["FilterPresets_v1", "FilterPresets_v2", "FilterPresets_v3"],
            entries.Select(e => Encoding.ASCII.GetString(e.UserKey.Span)));
    }

    [Fact]
    public void ReadEntries_HandlesSnappyBlocks()
    {
        // LevelDB's default, and what a real NVIDIA store turned out to use.
        byte[] value = Encoding.ASCII.GetBytes(new string('a', 4096));
        byte[] table = BuildTable([("compressible", 7, value)], BlockCompression.Snappy);

        TableEntry entry = Assert.Single(SsTable.ReadEntries(table));

        Assert.Equal(value, entry.Value.ToArray());
        Assert.Equal(7UL, entry.Sequence);
    }

    [Fact]
    public void HighestSequence_TakesTheLargest()
    {
        byte[] table = BuildTable(
        [
            ("a", 5, [0x01]),
            ("b", 900, [0x02]),
            ("c", 40, [0x03]),
        ]);

        Assert.Equal(900UL, SsTable.HighestSequence(table));
    }

    [Fact]
    public void ReadEntries_NotATable_Throws()
    {
        Assert.Throws<SsTableFormatException>(
            () => SsTable.ReadEntries(Encoding.ASCII.GetBytes("nope")).ToList());
    }

    [Fact]
    public void ReadEntries_TruncatedTable_ThrowsRatherThanReturningJunk()
    {
        // Losing the footer loses the magic, so this is caught as "not a table"
        // rather than parsed into whatever the bytes happen to look like. The
        // caller treats that as "no record here" and moves on to the next file.
        byte[] table = BuildTable([("a", 1, [0x01])]);
        byte[] truncated = table[..(table.Length / 2)];

        Assert.Throws<SsTableFormatException>(() => SsTable.ReadEntries(truncated).ToList());
    }

    [Fact]
    public void ReadEntries_EmptyTable_YieldsNothing()
    {
        Assert.Empty(SsTable.ReadEntries(BuildTable([])));
    }
}
