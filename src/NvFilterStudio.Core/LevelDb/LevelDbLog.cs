using System.Buffers.Binary;

namespace NvFilterStudio.Core.LevelDb;

/// <summary>Fragment role of a physical log record.</summary>
public enum LogRecordType : byte
{
    /// <summary>Zero padding at the tail of a block; not a real record.</summary>
    Zero = 0,

    /// <summary>A complete logical batch.</summary>
    Full = 1,

    /// <summary>First fragment of a batch that spans blocks.</summary>
    First = 2,

    /// <summary>Interior fragment.</summary>
    Middle = 3,

    /// <summary>Final fragment.</summary>
    Last = 4,
}

/// <summary>One physical record as it sits in the file.</summary>
/// <param name="Offset">Byte offset of the record header.</param>
/// <param name="Type">Fragment role.</param>
/// <param name="StoredCrc">Masked CRC from the header.</param>
/// <param name="Payload">Record payload, excluding the 7-byte header.</param>
public readonly record struct LogRecord(int Offset, LogRecordType Type, uint StoredCrc, ReadOnlyMemory<byte> Payload);

/// <summary>
/// Reader and writer for LevelDB's write-ahead log framing.
/// </summary>
/// <remarks>
/// The file is a sequence of 32 KiB blocks. Each record is
/// <c>crc32c(4) length(2) type(1)</c> followed by <c>length</c> payload bytes,
/// and the CRC covers <c>type || payload</c>. A block with fewer than 7 bytes
/// left is zero-padded and the next record starts in the following block, so a
/// logical batch larger than a block is split First/Middle…/Last.
/// <para>
/// Reassembly is not optional: skipping it chops every record at each 32 KiB
/// boundary, which silently truncates longer presets rather than failing.
/// </para>
/// </remarks>
public static class LevelDbLog
{
    /// <summary>LevelDB block size.</summary>
    public const int BlockSize = 32768;

    /// <summary>Bytes of header preceding each record payload.</summary>
    public const int HeaderSize = 7;

    /// <summary>Enumerates physical records, without reassembling fragments.</summary>
    public static IReadOnlyList<LogRecord> ReadRecords(ReadOnlyMemory<byte> data)
    {
        var records = new List<LogRecord>();
        ReadOnlySpan<byte> span = data.Span;
        int pos = 0;

        while (pos < span.Length)
        {
            int offsetInBlock = pos % BlockSize;
            int remainingInBlock = BlockSize - offsetInBlock;

            if (remainingInBlock < HeaderSize)
            {
                pos += remainingInBlock;
                continue;
            }

            if (pos + HeaderSize > span.Length)
            {
                break;
            }

            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(span[(pos + 4)..]);
            var type = (LogRecordType)span[pos + 6];

            // Zero padding: skip to the next block boundary.
            if (type == LogRecordType.Zero && length == 0)
            {
                pos += remainingInBlock;
                continue;
            }

            int dataStart = pos + HeaderSize;
            if (dataStart + length > span.Length)
            {
                break; // torn tail write; everything before it is still valid
            }

            records.Add(new LogRecord(pos, type, crc, data.Slice(dataStart, length)));
            pos = dataStart + length;
        }

        return records;
    }

    /// <summary>
    /// Verifies every record's masked CRC.
    /// </summary>
    /// <returns>Offsets of records whose CRC does not match; empty when clean.</returns>
    public static IReadOnlyList<int> FindCorruptRecords(ReadOnlyMemory<byte> data)
    {
        var bad = new List<int>();
        foreach (LogRecord record in ReadRecords(data))
        {
            // The CRC covers the type byte followed by the payload.
            var buffer = new byte[1 + record.Payload.Length];
            buffer[0] = (byte)record.Type;
            record.Payload.Span.CopyTo(buffer.AsSpan(1));

            if (Crc32C.ComputeMasked(buffer) != record.StoredCrc)
            {
                bad.Add(record.Offset);
            }
        }

        return bad;
    }

    /// <summary>
    /// Reassembles physical records into logical write batches, joining
    /// First/Middle/Last fragments.
    /// </summary>
    public static IReadOnlyList<byte[]> ReadBatches(ReadOnlyMemory<byte> data)
    {
        var batches = new List<byte[]>();

        // A log that ends mid-fragment leaves a partial buffer behind, which is
        // normal for a live log being appended to. Disposal therefore happens
        // once at the end rather than only on the Last branch.
        MemoryStream? pending = null;
        try
        {
            foreach (LogRecord record in ReadRecords(data))
            {
                switch (record.Type)
                {
                    case LogRecordType.Full:
                        batches.Add(record.Payload.ToArray());
                        pending?.Dispose();
                        pending = null;
                        break;

                    case LogRecordType.First:
                        pending?.Dispose();
                        pending = new MemoryStream();
                        pending.Write(record.Payload.Span);
                        break;

                    case LogRecordType.Middle:
                        pending?.Write(record.Payload.Span);
                        break;

                    case LogRecordType.Last:
                        if (pending is not null)
                        {
                            pending.Write(record.Payload.Span);
                            batches.Add(pending.ToArray());
                            pending.Dispose();
                            pending = null;
                        }

                        break;

                    case LogRecordType.Zero:
                    default:
                        break;
                }
            }
        }
        finally
        {
            pending?.Dispose();
        }

        return batches;
    }

    /// <summary>
    /// Returns <paramref name="existingLog"/> with <paramref name="batch"/>
    /// appended as one or more correctly framed records.
    /// </summary>
    /// <remarks>
    /// Block alignment is computed from the growing output, so appending to a
    /// log that ends mid-block produces the same layout LevelDB would.
    /// </remarks>
    public static byte[] AppendBatch(ReadOnlySpan<byte> existingLog, ReadOnlySpan<byte> batch)
    {
        var output = new List<byte>(existingLog.Length + batch.Length + 64);
        output.AddRange(existingLog);

        int consumed = 0;
        bool isFirstFragment = true;

        while (consumed < batch.Length)
        {
            int offsetInBlock = output.Count % BlockSize;
            int remainingInBlock = BlockSize - offsetInBlock;

            if (remainingInBlock < HeaderSize)
            {
                output.AddRange(new byte[remainingInBlock]); // zero padding
                continue;
            }

            int capacity = remainingInBlock - HeaderSize;
            int take = Math.Min(capacity, batch.Length - consumed);
            bool isLastFragment = take == batch.Length - consumed;

            LogRecordType type = (isFirstFragment, isLastFragment) switch
            {
                (true, true) => LogRecordType.Full,
                (true, false) => LogRecordType.First,
                (false, true) => LogRecordType.Last,
                _ => LogRecordType.Middle,
            };

            var record = new byte[HeaderSize + take];
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), (ushort)take);
            record[6] = (byte)type;
            batch.Slice(consumed, take).CopyTo(record.AsSpan(HeaderSize));

            uint crc = Crc32C.ComputeMasked(record.AsSpan(6, 1 + take));
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0), crc);

            output.AddRange(record);
            consumed += take;
            isFirstFragment = false;
        }

        return [.. output];
    }
}
