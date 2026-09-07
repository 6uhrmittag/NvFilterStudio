using System.Globalization;

namespace NvFilterStudio.Core.Store;

/// <summary>
/// Keeps the backup folder from growing without limit.
/// </summary>
/// <remarks>
/// A full store copy is written before every write, a few MB each, and someone
/// editing filters regularly accumulates hundreds of megabytes they do not know
/// exist.
/// <para>
/// Pruning is written conservatively on purpose: <b>the backups are the safety
/// net</b>, and deleting the wrong one is worse than wasted disk. Two rules
/// follow from that — the <em>oldest</em> backup is kept forever, because it is
/// the closest thing to "how the store looked before this tool ever touched
/// it", and anything that cannot be understood is left alone rather than
/// guessed at.
/// </para>
/// </remarks>
public static class BackupRetention
{
    /// <summary>How many recent backups to keep, on top of the oldest.</summary>
    public const int DefaultKeepRecent = 10;

    /// <summary>Timestamp format used for backup directory names.</summary>
    public const string StampFormat = "yyyyMMdd-HHmmss";

    /// <summary>A backup directory that could be dated from its name.</summary>
    /// <param name="Path">Full path.</param>
    /// <param name="Taken">When it was written.</param>
    public readonly record struct Backup(string Path, DateTime Taken);

    /// <summary>
    /// Lists backups whose names carry a parseable timestamp, oldest first.
    /// </summary>
    /// <remarks>
    /// Directories that do not parse are not returned, and so are never pruned.
    /// A folder someone renamed by hand — "before-driver-update" — is exactly
    /// the one they most want kept.
    /// </remarks>
    public static IReadOnlyList<Backup> List(string backupRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);

        if (!Directory.Exists(backupRoot))
        {
            return [];
        }

        var found = new List<Backup>();

        foreach (string path in Directory.EnumerateDirectories(backupRoot))
        {
            if (TryParseStamp(Path.GetFileName(path), out DateTime taken))
            {
                found.Add(new Backup(path, taken));
            }
        }

        found.Sort((a, b) => a.Taken.CompareTo(b.Taken));
        return found;
    }

    /// <summary>
    /// Deletes old backups, keeping the oldest plus the most recent
    /// <paramref name="keepRecent"/>.
    /// </summary>
    /// <returns>Directories actually removed.</returns>
    public static IReadOnlyList<string> Prune(string backupRoot, int keepRecent = DefaultKeepRecent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepRecent);

        IReadOnlyList<Backup> all = List(backupRoot);

        // Oldest plus the newest keepRecent. Below that there is nothing to do.
        if (all.Count <= keepRecent + 1)
        {
            return [];
        }

        var removed = new List<string>();

        // Skip index 0 (the oldest) and everything in the recent tail.
        for (int i = 1; i < all.Count - keepRecent; i++)
        {
            try
            {
                Directory.Delete(all[i].Path, recursive: true);
                removed.Add(all[i].Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A backup that will not delete is not a failure worth
                // surfacing; it just stays.
            }
        }

        return removed;
    }

    /// <summary>Total size of the backup folder, in bytes.</summary>
    public static long TotalSize(string backupRoot)
    {
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
        {
            return 0;
        }

        long total = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // Vanished between enumerating and measuring.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }

    /// <summary>Formats a byte count for display.</summary>
    public static string DescribeSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        < 1024 * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"),
    };

    private static bool TryParseStamp(string name, out DateTime taken)
    {
        // Names may carry a prefix, e.g. "preimport-20260906-210530".
        int dash = name.Length - StampFormat.Length;
        string candidate = dash > 0 ? name[dash..] : name;

        return DateTime.TryParseExact(
            candidate, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out taken);
    }
}
