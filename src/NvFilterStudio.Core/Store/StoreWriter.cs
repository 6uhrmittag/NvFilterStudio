using System.Globalization;
using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Store;

/// <summary>Raised when the store cannot safely be written.</summary>
public sealed class StoreWriteBlockedException(string message) : Exception(message);

/// <summary>Outcome of a successful write.</summary>
/// <param name="LogPath">The log that was appended to.</param>
/// <param name="BackupDirectory">Where the pre-write backup was placed.</param>
/// <param name="Sequence">Sequence number the batch was stamped with.</param>
/// <param name="NewValueVersion">Value version written.</param>
/// <param name="BytesBefore">Log size before the append.</param>
/// <param name="BytesAfter">Log size after the append.</param>
public sealed record StoreWriteResult(
    string LogPath,
    string? BackupDirectory,
    ulong Sequence,
    ulong NewValueVersion,
    long BytesBefore,
    long BytesAfter);

/// <summary>
/// Writes filter presets back by appending a LevelDB write batch.
/// </summary>
/// <remarks>
/// LevelDB replays the log in order and, for one user key, the highest sequence
/// number wins — so an append is a legitimate update rather than a patch.
/// <para>
/// Writing is impossible while the overlay runs. Chromium holds the database
/// open with its own memtable, so an append is invisible to it and is discarded
/// on its next flush. That failure is silent, which is why
/// <see cref="Write"/> refuses rather than trying.
/// </para>
/// </remarks>
public sealed class StoreWriter(StoreLocator locator)
{
    private readonly StoreLocator _locator = locator ?? throw new ArgumentNullException(nameof(locator));

    /// <summary>Default backup root: <c>%LOCALAPPDATA%\NvFilterStudio\backups</c>.</summary>
    public static string DefaultBackupRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NvFilterStudio",
            "backups");

    /// <summary>
    /// Replaces the stored preset document with <paramref name="json"/>.
    /// </summary>
    /// <param name="json">The complete new filter-preset document.</param>
    /// <param name="snapshot">The record being superseded; supplies key and version.</param>
    /// <param name="backupRoot">Backup location, or <see langword="null"/> for the default.</param>
    /// <param name="skipBackup">Skip the pre-write backup. Not recommended.</param>
    /// <exception cref="StoreWriteBlockedException">The store is not writable.</exception>
    public StoreWriteResult Write(
        string json,
        StoreSnapshot snapshot,
        string? backupRoot = null,
        bool skipBackup = false)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Key.Length == 0)
        {
            throw new StoreWriteBlockedException(
                "The current record was recovered from a compacted table, so its exact key " +
                "bytes are unknown. Open the NVIDIA overlay once to make it rewrite the log, " +
                "then retry.");
        }

        string logPath = _locator.ActiveLogPath
            ?? throw new StoreWriteBlockedException($"No numbered .log file in {_locator.Directory}");

        if (_locator.IsLiveStore)
        {
            EnsureLiveStoreWritable(logPath);
        }

        string? backupDirectory = skipBackup ? null : CreateVerifiedBackup(backupRoot ?? DefaultBackupRoot);

        byte[] logBytes = StoreLocator.ReadPossiblyLockedFile(logPath);
        long before = logBytes.Length;

        ulong newVersion = snapshot.ValueVersion + 1;
        byte[] value = StoreValueCodec.Encode(json, newVersion);
        byte[] batch = WriteBatch.BuildPut(snapshot.NextSequence, snapshot.Key, value);
        byte[] updated = LevelDbLog.AppendBatch(logBytes, batch);

        File.WriteAllBytes(logPath, updated);

        VerifyReadBack(json);

        return new StoreWriteResult(
            logPath, backupDirectory, snapshot.NextSequence, newVersion, before, updated.Length);
    }

    private void EnsureLiveStoreWritable(string logPath)
    {
        IReadOnlyList<(string Name, int Id)> holders = StoreLocator.FindHoldingProcesses();
        if (holders.Count > 0)
        {
            string list = string.Join(", ", holders.Select(h => $"{h.Name} ({h.Id})"));
            throw new StoreWriteBlockedException(
                $"NVIDIA is still running: {list}. Turn the In-Game Overlay off in " +
                "NVIDIA App -> Settings -> Features, then close the NVIDIA App.");
        }

        if (!_locator.IsWritable())
        {
            throw new StoreWriteBlockedException(
                $"{logPath} is still locked by another process. Turn the In-Game Overlay off " +
                "in NVIDIA App -> Settings -> Features, then close the NVIDIA App.");
        }
    }

    /// <summary>
    /// Copies the store, then proves the copy decodes rather than assuming the
    /// file copy succeeded.
    /// </summary>
    private string CreateVerifiedBackup(string backupRoot)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string destination = Path.Combine(backupRoot, stamp);
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(_locator.Directory))
        {
            try
            {
                byte[] bytes = StoreLocator.ReadPossiblyLockedFile(file);
                File.WriteAllBytes(Path.Combine(destination, Path.GetFileName(file)), bytes);
            }
            catch (IOException)
            {
                // LOCK is a zero-byte file that cannot be copied; nothing else
                // in the store should fail, and the verification below catches
                // any case where something important did.
            }
        }

        try
        {
            _ = new StoreReader(new StoreLocator(destination)).Read();
        }
        catch (Exception ex) when (ex is InvalidDataException or DirectoryNotFoundException)
        {
            throw new StoreWriteBlockedException(
                $"Backup at {destination} does not decode, so it is not a usable rollback " +
                "point. Refusing to write.");
        }

        // Prune only after the new backup is verified, so a failure here can
        // never leave the user with fewer safety nets than they started with.
        BackupRetention.Prune(backupRoot);

        return destination;
    }

    private void VerifyReadBack(string expectedJson)
    {
        StoreSnapshot check = new StoreReader(_locator).Read();
        if (!string.Equals(check.Json, expectedJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Verification failed: the record read back does not match what was written.");
        }
    }
}
