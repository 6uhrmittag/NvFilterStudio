using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

public class StoreReaderTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static StoreReader ReaderFor(SyntheticStore store) =>
        new(new StoreLocator(store.Directory));

    [Fact]
    public void Read_ReturnsTheOnlyRecord()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(3, (100, SyntheticStore.BuildPresetJson(ExePath, 10), 40));

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.Contains("\"currentUIValue\":10", snapshot.Json, StringComparison.Ordinal);
        Assert.Equal(40UL, snapshot.ValueVersion);
        Assert.NotEmpty(snapshot.Key);
    }

    [Fact]
    public void Read_LaterBatchInTheSameLogWins()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(
            3,
            (100, SyntheticStore.BuildPresetJson(ExePath, 10), 40),
            (105, SyntheticStore.BuildPresetJson(ExePath, 55), 41));

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.Contains("\"currentUIValue\":55", snapshot.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_LogBeatsALargerStaleTable()
    {
        // REGRESSION. The first implementation picked the longest candidate
        // across all files. That works only while the store is log-only; after
        // compaction a table holds many superseded copies, and a larger stale
        // one would silently win. Precedence is structural, not size-based.
        using var store = SyntheticStore.Create();

        string stale = SyntheticStore.BuildPresetJson(ExePath, 99, new string('x', 20_000));
        store.WriteTable(5, stale, version: 30);

        string live = SyntheticStore.BuildPresetJson(ExePath, 10);
        store.WriteLog(4, (200, live, 44));

        Assert.True(stale.Length > live.Length, "fixture must make the stale record the larger one");

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.Contains("\"currentUIValue\":10", snapshot.Json, StringComparison.Ordinal);
        Assert.EndsWith(".log", snapshot.SourceFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FallsBackToTableWhenTheLogHasNoRecord()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(4); // empty log, as just after a compaction
        store.WriteTable(5, SyntheticStore.BuildPresetJson(ExePath, 77), version: 30);

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.Contains("\"currentUIValue\":77", snapshot.Json, StringComparison.Ordinal);
        Assert.EndsWith(".ldb", snapshot.SourceFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_PrefersTheHighestNumberedLog()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(3, (100, SyntheticStore.BuildPresetJson(ExePath, 11), 40));
        store.WriteLog(4, (300, SyntheticStore.BuildPresetJson(ExePath, 22), 41));

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.Contains("\"currentUIValue\":22", snapshot.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_NextSequenceOutranksTheLog()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(3, (500, SyntheticStore.BuildPresetJson(ExePath, 10), 40));

        StoreSnapshot snapshot = ReaderFor(store).Read();

        // The batch held one entry, so it consumed sequence 500.
        Assert.True(snapshot.NextSequence > 500);
    }

    [Fact]
    public void Read_ManifestSequenceWinsWhenHigherThanTheLog()
    {
        // REGRESSION. Numbering from the log alone lets an entry already
        // compacted into a table shadow the write. The failure is silent: the
        // file grows, the read-back looks right, and the app shows old values.
        using var store = SyntheticStore.Create();
        store.WriteLog(4, (100, SyntheticStore.BuildPresetJson(ExePath, 10), 40));
        store.WriteManifest(1, lastSequence: 50_000);

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.Equal(50_001UL, snapshot.NextSequence);
    }

    [Fact]
    public void Read_IgnoresManifestSequenceWhenTheLogIsAhead()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(4, (90_000, SyntheticStore.BuildPresetJson(ExePath, 10), 40));
        store.WriteManifest(1, lastSequence: 50_000);

        StoreSnapshot snapshot = ReaderFor(store).Read();

        Assert.True(snapshot.NextSequence > 90_000);
    }

    [Fact]
    public void Read_EmptyStore_Throws()
    {
        using var store = SyntheticStore.Create();
        store.WriteLog(3);

        Assert.Throws<InvalidDataException>(() => ReaderFor(store).Read());
    }

    [Fact]
    public void Read_MissingDirectory_Throws()
    {
        var reader = new StoreReader(new StoreLocator(Path.Combine(Path.GetTempPath(), "nv-does-not-exist")));

        Assert.Throws<DirectoryNotFoundException>(reader.Read);
    }
}
