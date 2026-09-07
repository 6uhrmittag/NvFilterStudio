using System.Globalization;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

public class BackupRetentionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "NvFilterStudioTests", Path.GetRandomFileName());

    public BackupRetentionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private string MakeBackup(DateTime taken, string prefix = "")
    {
        string name = prefix + taken.ToString(BackupRetention.StampFormat, CultureInfo.InvariantCulture);
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "000003.log"), new string('x', 512));
        return path;
    }

    [Fact]
    public void List_IsOldestFirst()
    {
        var day = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        MakeBackup(day.AddHours(2));
        MakeBackup(day);
        MakeBackup(day.AddHours(1));

        IReadOnlyList<BackupRetention.Backup> all = BackupRetention.List(_root);

        Assert.Equal(3, all.Count);
        Assert.Equal(day, all[0].Taken);
        Assert.Equal(day.AddHours(2), all[2].Taken);
    }

    [Fact]
    public void List_ParsesPrefixedNames()
    {
        // StoreWriter writes "preimport-<stamp>" in some flows.
        var when = new DateTime(2026, 5, 4, 9, 30, 0, DateTimeKind.Local);
        MakeBackup(when, "preimport-");

        Assert.Equal(when, Assert.Single(BackupRetention.List(_root)).Taken);
    }

    [Fact]
    public void Prune_KeepsTheOldestForever()
    {
        // The oldest is the closest thing to "before this tool touched
        // anything", so it survives no matter how many newer ones exist.
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        string oldest = MakeBackup(day);
        for (int i = 1; i <= 20; i++)
        {
            MakeBackup(day.AddHours(i));
        }

        BackupRetention.Prune(_root, keepRecent: 3);

        Assert.True(Directory.Exists(oldest), "the oldest backup must never be pruned");
    }

    [Fact]
    public void Prune_KeepsTheRequestedNumberOfRecentOnes()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        for (int i = 0; i < 12; i++)
        {
            MakeBackup(day.AddHours(i));
        }

        BackupRetention.Prune(_root, keepRecent: 4);

        IReadOnlyList<BackupRetention.Backup> left = BackupRetention.List(_root);

        // Four newest plus the oldest.
        Assert.Equal(5, left.Count);
        Assert.Equal(day, left[0].Taken);
        Assert.Equal(day.AddHours(11), left[^1].Taken);
    }

    [Fact]
    public void Prune_DoesNothingWhenUnderTheLimit()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        for (int i = 0; i < 4; i++)
        {
            MakeBackup(day.AddHours(i));
        }

        Assert.Empty(BackupRetention.Prune(_root, keepRecent: 10));
        Assert.Equal(4, BackupRetention.List(_root).Count);
    }

    [Fact]
    public void Prune_LeavesDirectoriesItCannotDate()
    {
        // A folder someone renamed by hand - "before-driver-update" - is
        // exactly the one they most want kept.
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        for (int i = 0; i < 20; i++)
        {
            MakeBackup(day.AddHours(i));
        }

        string keepMe = Path.Combine(_root, "before-driver-update");
        Directory.CreateDirectory(keepMe);

        BackupRetention.Prune(_root, keepRecent: 2);

        Assert.True(Directory.Exists(keepMe), "an undated directory must be left alone");
    }

    [Fact]
    public void Prune_MissingRootIsNotAnError()
    {
        Assert.Empty(BackupRetention.Prune(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void TotalSize_CountsNestedFiles()
    {
        MakeBackup(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
        MakeBackup(new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Local));

        Assert.Equal(1024, BackupRetention.TotalSize(_root));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void DescribeSize_ReadsNaturally(long bytes, string expected)
    {
        Assert.Equal(expected, BackupRetention.DescribeSize(bytes));
    }
}
