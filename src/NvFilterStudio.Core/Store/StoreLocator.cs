using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NvFilterStudio.Core.Store;

/// <summary>
/// Finds the NVIDIA Overlay's IndexedDB store and reports whether it can be
/// written to.
/// </summary>
public sealed class StoreLocator
{
    /// <summary>Path of the store relative to <c>%LOCALAPPDATA%</c>.</summary>
    public const string RelativeStorePath =
        @"NVIDIA Corporation\NVIDIA Overlay\CefCache\Default\IndexedDB\https_nvfile_0.indexeddb.leveldb";

    /// <summary>
    /// Processes that hold the store open. <c>nvcontainer</c> is deliberately
    /// absent: it is a service host, does not hold this database, and cannot be
    /// stopped without elevation.
    /// </summary>
    private static readonly string[] HoldingProcessNames = ["NVIDIA Overlay", "NVIDIA App"];

    /// <summary>Creates a locator for <paramref name="storeDirectory"/>, or the live store.</summary>
    public StoreLocator(string? storeDirectory = null)
    {
        Directory = storeDirectory ?? DefaultStoreDirectory;
    }

    /// <summary>The live store directory for the current user.</summary>
    public static string DefaultStoreDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            RelativeStorePath);

    /// <summary>Directory this locator points at.</summary>
    public string Directory { get; }

    /// <summary>Whether the directory exists.</summary>
    public bool Exists => System.IO.Directory.Exists(Directory);

    /// <summary>Whether this locator points at the live store rather than a copy.</summary>
    public bool IsLiveStore =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(DefaultStoreDirectory)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The active write-ahead log: the highest-numbered <c>*.log</c>.
    /// </summary>
    /// <remarks>
    /// Never hard-code a name. LevelDB rotates on compaction — a single
    /// <c>000003.log</c> was observed becoming <c>000004.log</c> plus a
    /// compacted <c>000005.ldb</c> simply because the overlay restarted.
    /// </remarks>
    public string? ActiveLogPath =>
        EnumerateNumbered("*.log").OrderByDescending(x => x.Number).Select(x => x.Path).FirstOrDefault();

    /// <summary>Compacted table files, newest first.</summary>
    public IReadOnlyList<string> TablePaths =>
        [.. EnumerateNumbered("*.ldb").OrderByDescending(x => x.Number).Select(x => x.Path)];

    /// <summary>The MANIFEST named by <c>CURRENT</c>, when present.</summary>
    public string? ManifestPath
    {
        get
        {
            string current = Path.Combine(Directory, "CURRENT");
            if (!File.Exists(current))
            {
                return null;
            }

            try
            {
                string name = Encoding.ASCII.GetString(ReadPossiblyLockedFile(current)).Trim();
                string path = Path.Combine(Directory, name);
                return File.Exists(path) ? path : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private IEnumerable<(int Number, string Path)> EnumerateNumbered(string pattern)
    {
        if (!Exists)
        {
            yield break;
        }

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, pattern))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                yield return (number, path);
            }
        }
    }

    /// <summary>
    /// Reads a file the overlay may currently hold open.
    /// </summary>
    /// <remarks>
    /// A plain read fails while the overlay is running; the share flags are what
    /// make live inspection possible. The log is append-only, so a copy taken
    /// this way is a consistent snapshot.
    /// </remarks>
    public static byte[] ReadPossiblyLockedFile(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// Whether the store can currently be written.
    /// </summary>
    /// <remarks>
    /// Attempting an exclusive open is the only reliable test. Checking process
    /// names alone is not enough, and Chromium keeps its own in-memory memtable
    /// regardless — a write made underneath a running overlay is lost on its
    /// next flush.
    /// </remarks>
    public bool IsWritable()
    {
        string? log = ActiveLogPath;
        if (log is null)
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Names and ids of running processes known to hold the store.</summary>
    public static IReadOnlyList<(string Name, int Id)> FindHoldingProcesses()
    {
        var found = new List<(string, int)>();
        foreach (string name in HoldingProcessNames)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    found.Add((name, process.Id));
                }
            }
        }

        return found;
    }
}
