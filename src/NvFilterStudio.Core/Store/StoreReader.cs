using System.Text;
using System.Text.Json;
using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Store;

/// <summary>The live filter-preset record plus what a writer needs to supersede it.</summary>
/// <param name="Json">The preset document.</param>
/// <param name="ValueVersion">IndexedDB value version of the record read.</param>
/// <param name="Key">Key bytes, reused verbatim when writing.</param>
/// <param name="SourceFile">File the record came from, for diagnostics.</param>
/// <param name="NextSequence">
/// A sequence number guaranteed to outrank every existing entry, including ones
/// already compacted into tables.
/// </param>
public sealed record StoreSnapshot(
    string Json,
    ulong ValueVersion,
    byte[] Key,
    string SourceFile,
    ulong NextSequence);

/// <summary>Reads the current filter presets out of a store directory.</summary>
public sealed class StoreReader(StoreLocator locator)
{
    private readonly StoreLocator _locator = locator ?? throw new ArgumentNullException(nameof(locator));

    /// <summary>
    /// Reads the newest filter-preset record.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The store holds no readable preset record.
    /// </exception>
    public StoreSnapshot Read()
    {
        if (!_locator.Exists)
        {
            throw new DirectoryNotFoundException(
                $"NVIDIA Overlay IndexedDB not found at: {_locator.Directory}");
        }

        string? logPath = _locator.ActiveLogPath
            ?? throw new InvalidDataException($"No numbered .log file in {_locator.Directory}");

        byte[] logBytes = StoreLocator.ReadPossiblyLockedFile(logPath);

        // --- sequence high-water mark -------------------------------------
        // Both sources matter. The log alone is not enough once compaction has
        // moved entries into tables: those carry their own sequence numbers and
        // would shadow a lower-numbered append, which fails silently.
        ulong sequence = HighestSequenceInLog(logBytes);
        if (_locator.ManifestPath is { } manifestPath)
        {
            byte[] manifestBytes = StoreLocator.ReadPossiblyLockedFile(manifestPath);
            if (ManifestReader.ReadLastSequence(manifestBytes) is { } manifestSequence &&
                manifestSequence > sequence)
            {
                sequence = manifestSequence;
            }
        }

        // --- record selection ---------------------------------------------
        // Precedence, not size. A record in the log is always newer than one in
        // a table, because compaction moves older data out and later writes go
        // to a fresh log. Picking the largest candidate instead breaks the
        // moment a table appears.
        if (TryReadFromLog(logBytes, Path.GetFileName(logPath), sequence) is { } fromLog)
        {
            return fromLog;
        }

        foreach (string tablePath in _locator.TablePaths)
        {
            byte[] tableBytes = StoreLocator.ReadPossiblyLockedFile(tablePath);
            if (TryScanForRecord(tableBytes, Path.GetFileName(tablePath), sequence) is { } fromTable)
            {
                return fromTable;
            }
        }

        throw new InvalidDataException(
            "No parseable filterPresets record found. Has any game filter ever been saved to a slot?");
    }

    /// <summary>Highest sequence any batch in the log reaches.</summary>
    private static ulong HighestSequenceInLog(ReadOnlyMemory<byte> logBytes)
    {
        ulong highest = 0;
        foreach (byte[] batch in LevelDbLog.ReadBatches(logBytes))
        {
            if (batch.Length < WriteBatch.HeaderSize)
            {
                continue;
            }

            // A batch consumes one sequence number per entry.
            ulong end = WriteBatch.ReadSequence(batch) + WriteBatch.ReadCount(batch);
            if (end > highest)
            {
                highest = end;
            }
        }

        return highest;
    }

    /// <summary>
    /// Finds the last filter-preset put in the log by parsing batch entries,
    /// which also yields the exact key bytes to reuse when writing.
    /// </summary>
    private static StoreSnapshot? TryReadFromLog(ReadOnlyMemory<byte> logBytes, string source, ulong sequence)
    {
        BatchEntry? newest = null;

        foreach (byte[] batch in LevelDbLog.ReadBatches(logBytes))
        {
            foreach (BatchEntry entry in WriteBatch.ReadEntries(batch))
            {
                // Identity comes from the key and the encoding preamble, never
                // from the value's size. A size threshold would skip a
                // legitimately small document - a fresh install with one filter
                // in one slot - and the PowerShell reference tool has exactly
                // that bug.
                if (entry.Type != BatchEntryType.Value ||
                    !IndexedDbKey.IsFilterPresetsKey(entry.Key.Span) ||
                    !StoreValueCodec.LooksLikePresetValue(entry.Value.Span))
                {
                    continue;
                }

                newest = entry; // later batches supersede earlier ones
            }
        }

        if (newest is not { } put)
        {
            return null;
        }

        StoreValue value = StoreValueCodec.Decode(put.Value);
        if (!IsParseableJson(value.Json))
        {
            return null;
        }

        return new StoreSnapshot(value.Json, value.Version, put.Key.ToArray(), source, sequence + 1);
    }

    /// <summary>
    /// Recovers a record from raw bytes by scanning for the JSON marker.
    /// </summary>
    /// <remarks>
    /// Used for <c>.ldb</c> tables, whose sorted-block layout (index blocks,
    /// restart points, optional compression) is not parsed. Records that happen
    /// to be split across blocks or compressed are therefore invisible here —
    /// a known limitation, and the reason the log is always preferred.
    /// </remarks>
    private static StoreSnapshot? TryScanForRecord(ReadOnlyMemory<byte> bytes, string source, ulong sequence)
    {
        string text = Encoding.Latin1.GetString(bytes.Span);

        foreach (int offset in JsonBraceScanner.FindAll(text, StoreValueCodec.JsonMarker).Reverse())
        {
            string? candidate = JsonBraceScanner.ExtractObject(text, offset);
            if (candidate is null || !IsParseableJson(candidate))
            {
                continue;
            }

            // Key bytes are unavailable from a raw scan; the caller must have a
            // log-sourced key to write. Reads still work.
            return new StoreSnapshot(candidate, 0, [], source, sequence + 1);
        }

        return null;
    }

    private static bool IsParseableJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
