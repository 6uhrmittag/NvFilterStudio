namespace NvFilterStudio.Core.LevelDb;

/// <summary>VersionEdit field tags used in a MANIFEST.</summary>
internal enum VersionEditTag
{
    Comparator = 1,
    LogNumber = 2,
    PrevLogNumber = 3,
    LastSequence = 4,
    CompactPointer = 5,
    DeletedFile = 6,
    NewFile = 7,
    NextFileNumber = 9,
}

/// <summary>
/// Reads <c>kLastSequence</c> out of a LevelDB MANIFEST.
/// </summary>
/// <remarks>
/// The MANIFEST is itself a LevelDB log whose records are VersionEdits: a
/// stream of <c>(tag, payload)</c> pairs whose payload shape depends on the tag.
/// <para>
/// This matters because compaction moves data into <c>.ldb</c> tables that carry
/// their own sequence numbers. A write numbered only from the log can therefore
/// be outranked by a table entry. The MANIFEST is the authoritative high-water
/// mark, so it is consulted before choosing a sequence.
/// </para>
/// <para>
/// Every tag must be skipped correctly, including ones we do not care about:
/// a mis-sized skip desynchronises the varint stream and yields a plausible but
/// wrong sequence rather than an error.
/// </para>
/// </remarks>
public static class ManifestReader
{
    /// <summary>
    /// Highest <c>kLastSequence</c> across all VersionEdits, or
    /// <see langword="null"/> when the MANIFEST holds none.
    /// </summary>
    public static ulong? ReadLastSequence(ReadOnlyMemory<byte> manifestBytes)
    {
        ulong best = 0;
        bool found = false;

        foreach (byte[] record in LevelDbLog.ReadBatches(manifestBytes))
        {
            if (TryReadLastSequence(record, out ulong sequence) && sequence > best)
            {
                best = sequence;
                found = true;
            }
        }

        return found ? best : null;
    }

    private static bool TryReadLastSequence(ReadOnlySpan<byte> edit, out ulong lastSequence)
    {
        lastSequence = 0;
        bool found = false;
        int pos = 0;

        while (pos < edit.Length)
        {
            if (!Varint.TryRead(edit, ref pos, out ulong rawTag))
            {
                break;
            }

            switch ((VersionEditTag)rawTag)
            {
                case VersionEditTag.Comparator:
                    // length-prefixed string
                    if (!SkipLengthPrefixed(edit, ref pos))
                    {
                        return found;
                    }

                    break;

                case VersionEditTag.CompactPointer:
                    // varint level, then a length-prefixed key
                    if (!SkipVarint(edit, ref pos) || !SkipLengthPrefixed(edit, ref pos))
                    {
                        return found;
                    }

                    break;

                case VersionEditTag.LogNumber:
                case VersionEditTag.PrevLogNumber:
                case VersionEditTag.NextFileNumber:
                    if (!SkipVarint(edit, ref pos))
                    {
                        return found;
                    }

                    break;

                case VersionEditTag.LastSequence:
                    if (!Varint.TryRead(edit, ref pos, out ulong sequence))
                    {
                        return found;
                    }

                    lastSequence = sequence;
                    found = true;
                    break;

                case VersionEditTag.DeletedFile:
                    if (!SkipVarint(edit, ref pos) || !SkipVarint(edit, ref pos))
                    {
                        return found;
                    }

                    break;

                case VersionEditTag.NewFile:
                    // level, file number, file size, then smallest and largest keys
                    if (!SkipVarint(edit, ref pos) ||
                        !SkipVarint(edit, ref pos) ||
                        !SkipVarint(edit, ref pos) ||
                        !SkipLengthPrefixed(edit, ref pos) ||
                        !SkipLengthPrefixed(edit, ref pos))
                    {
                        return found;
                    }

                    break;

                default:
                    // Unknown tag: the payload shape is unknown, so the stream
                    // can no longer be walked safely.
                    return found;
            }
        }

        return found;
    }

    private static bool SkipVarint(ReadOnlySpan<byte> data, ref int pos) =>
        Varint.TryRead(data, ref pos, out _);

    private static bool SkipLengthPrefixed(ReadOnlySpan<byte> data, ref int pos)
    {
        if (!Varint.TryRead(data, ref pos, out ulong length))
        {
            return false;
        }

        if (pos + (int)length > data.Length)
        {
            return false;
        }

        pos += (int)length;
        return true;
    }
}
