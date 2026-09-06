using System.Buffers.Binary;

namespace NvFilterStudio.Core.LevelDb;

/// <summary>Entry kind inside a write batch.</summary>
public enum BatchEntryType : byte
{
    /// <summary>Key removed.</summary>
    Deletion = 0,

    /// <summary>Key written.</summary>
    Value = 1,
}

/// <summary>One key/value operation from a write batch.</summary>
/// <param name="Type">Put or delete.</param>
/// <param name="Key">Encoded IndexedDB key.</param>
/// <param name="Value">Payload; empty for a deletion.</param>
public readonly record struct BatchEntry(BatchEntryType Type, ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value);

/// <summary>
/// Parser and builder for LevelDB write batches.
/// </summary>
/// <remarks>
/// Layout: <c>sequence(8) count(4)</c> then <c>count</c> entries of
/// <c>type(1) varint(keyLen) key [varint(valueLen) value]</c>.
/// <para>
/// The sequence number decides who wins: for one user key, the entry with the
/// highest sequence is the live one. A batch numbered below an entry already
/// compacted into a table is silently shadowed — the file grows, a read-back of
/// the log looks correct, and the application still shows the old value.
/// </para>
/// </remarks>
public static class WriteBatch
{
    /// <summary>Bytes preceding the first entry.</summary>
    public const int HeaderSize = 12;

    /// <summary>Sequence number stamped on a batch.</summary>
    public static ulong ReadSequence(ReadOnlySpan<byte> batch) =>
        batch.Length < HeaderSize ? 0 : BinaryPrimitives.ReadUInt64LittleEndian(batch);

    /// <summary>Declared entry count.</summary>
    public static uint ReadCount(ReadOnlySpan<byte> batch) =>
        batch.Length < HeaderSize ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(batch[8..]);

    /// <summary>
    /// Parses the entries of one batch. Stops quietly at the first malformed
    /// entry, keeping whatever parsed cleanly, because a torn tail record is
    /// normal in a live log.
    /// </summary>
    public static IReadOnlyList<BatchEntry> ReadEntries(ReadOnlyMemory<byte> batch)
    {
        var entries = new List<BatchEntry>();
        if (batch.Length < HeaderSize)
        {
            return entries;
        }

        ReadOnlySpan<byte> span = batch.Span;
        uint count = ReadCount(span);
        int pos = HeaderSize;

        for (uint i = 0; i < count; i++)
        {
            if (pos >= span.Length)
            {
                break;
            }

            var type = (BatchEntryType)span[pos];
            pos++;

            if (!Varint.TryRead(span, ref pos, out ulong keyLength) ||
                pos + (int)keyLength > span.Length)
            {
                break;
            }

            ReadOnlyMemory<byte> key = batch.Slice(pos, (int)keyLength);
            pos += (int)keyLength;

            if (type == BatchEntryType.Deletion)
            {
                entries.Add(new BatchEntry(type, key, ReadOnlyMemory<byte>.Empty));
                continue;
            }

            if (!Varint.TryRead(span, ref pos, out ulong valueLength) ||
                pos + (int)valueLength > span.Length)
            {
                break;
            }

            entries.Add(new BatchEntry(type, key, batch.Slice(pos, (int)valueLength)));
            pos += (int)valueLength;
        }

        return entries;
    }

    /// <summary>Builds a single-put batch.</summary>
    public static byte[] BuildPut(ulong sequence, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var buffer = new List<byte>(HeaderSize + key.Length + value.Length + (2 * Varint.MaxBytes));

        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt64LittleEndian(header, sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 1);
        buffer.AddRange(header);

        buffer.Add((byte)BatchEntryType.Value);
        Varint.Write(buffer, (ulong)key.Length);
        buffer.AddRange(key);
        Varint.Write(buffer, (ulong)value.Length);
        buffer.AddRange(value);

        return [.. buffer];
    }
}
