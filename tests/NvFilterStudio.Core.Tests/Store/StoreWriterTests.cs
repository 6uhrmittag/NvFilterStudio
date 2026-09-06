using NvFilterStudio.Core.LevelDb;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

public class StoreWriterTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static StoreLocator LocatorFor(SyntheticStore store) => new(store.Directory);

    private static SyntheticStore NewStore(int sharpen = 10)
    {
        SyntheticStore store = SyntheticStore.Create();
        store.WriteLog(4, (100, SyntheticStore.BuildPresetJson(ExePath, sharpen), 40));
        store.WriteManifest(1, lastSequence: 90);
        return store;
    }

    private static string EditedJson(StoreSnapshot snapshot, double sharpen)
    {
        FilterPresetDocument document = FilterPresetDocument.Parse(snapshot.Json);
        Slot slot = document.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!;
        slot.Filters[0].GetControl(0)!.UiValue = sharpen;
        return document.ToJson();
    }

    private static string TempBackupRoot() =>
        Path.Combine(Path.GetTempPath(), "NvFilterStudioTests", Path.GetRandomFileName());

    [Fact]
    public void Write_ThenRead_ReturnsTheNewValue()
    {
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();

        new StoreWriter(locator).Write(EditedJson(before, 63), before, skipBackup: true);

        StoreSnapshot after = new StoreReader(locator).Read();
        Assert.Contains("\"currentUIValue\":63", after.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ChangesExactlyOneValue()
    {
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();

        new StoreWriter(locator).Write(EditedJson(before, 63), before, skipBackup: true);

        string after = new StoreReader(locator).Read().Json;

        // Everything except the edited number must survive untouched.
        Assert.Equal(
            before.Json.Replace("\"currentUIValue\":10", "\"currentUIValue\":63", StringComparison.Ordinal)
                       .Replace("\"currentValue\":0.1,", "\"currentValue\":0.63,", StringComparison.Ordinal)
                       .Replace("\"currentValueArray\":[0.1]", "\"currentValueArray\":[0.63]", StringComparison.Ordinal),
            after);
    }

    [Fact]
    public void Write_AppendsWithoutCorruptingExistingRecords()
    {
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();

        StoreWriteResult result = new StoreWriter(locator).Write(
            EditedJson(before, 63), before, skipBackup: true);

        Assert.True(result.BytesAfter > result.BytesBefore, "the log should grow");
        Assert.Empty(LevelDbLog.FindCorruptRecords(File.ReadAllBytes(result.LogPath)));
    }

    [Fact]
    public void Write_BumpsTheValueVersion()
    {
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();

        new StoreWriter(locator).Write(EditedJson(before, 63), before, skipBackup: true);

        Assert.Equal(before.ValueVersion + 1, new StoreReader(locator).Read().ValueVersion);
    }

    [Fact]
    public void Write_OutranksTheManifestSequence()
    {
        // The silent-failure case: a batch numbered below an entry already in a
        // compacted table is shadowed rather than rejected.
        using SyntheticStore store = SyntheticStore.Create();
        store.WriteLog(4, (100, SyntheticStore.BuildPresetJson(ExePath, 10), 40));
        store.WriteManifest(1, lastSequence: 40_000);

        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();

        StoreWriteResult result = new StoreWriter(locator).Write(
            EditedJson(before, 63), before, skipBackup: true);

        Assert.True(result.Sequence > 40_000, $"sequence was {result.Sequence}");
    }

    [Fact]
    public void Write_TakesABackupThatActuallyDecodes()
    {
        // Checking the backup parses is the point: a copied-but-unreadable
        // backup is not a rollback point, and the failure would only surface
        // when it was needed.
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();
        string backupRoot = TempBackupRoot();

        try
        {
            StoreWriteResult result = new StoreWriter(locator)
                .Write(EditedJson(before, 63), before, backupRoot);

            Assert.NotNull(result.BackupDirectory);

            StoreSnapshot fromBackup = new StoreReader(new StoreLocator(result.BackupDirectory!)).Read();
            Assert.Contains("\"currentUIValue\":10", fromBackup.Json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Write_BackupRestoresTheOriginal()
    {
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();
        string backupRoot = TempBackupRoot();

        try
        {
            StoreWriteResult result = new StoreWriter(locator)
                .Write(EditedJson(before, 63), before, backupRoot);

            // Roll back the way a user would: copy the backup over the store.
            foreach (string file in Directory.EnumerateFiles(result.BackupDirectory!))
            {
                File.Copy(file, Path.Combine(store.Directory, Path.GetFileName(file)), overwrite: true);
            }

            Assert.Equal(before.Json, new StoreReader(locator).Read().Json);
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Write_WithoutKeyBytes_Refuses()
    {
        // A record recovered by scanning a table carries no key, so there is
        // nothing safe to write against.
        using SyntheticStore store = SyntheticStore.Create();
        store.WriteLog(4);
        store.WriteTable(5, SyntheticStore.BuildPresetJson(ExePath, 10), version: 30);

        StoreLocator locator = LocatorFor(store);
        StoreSnapshot fromTable = new StoreReader(locator).Read();

        Assert.Empty(fromTable.Key);
        StoreWriteBlockedException ex = Assert.Throws<StoreWriteBlockedException>(
            () => new StoreWriter(locator).Write(fromTable.Json, fromTable, skipBackup: true));

        Assert.Contains("compacted table", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_TwiceInARow_KeepsTheLatest()
    {
        using SyntheticStore store = NewStore();
        StoreLocator locator = LocatorFor(store);

        StoreSnapshot first = new StoreReader(locator).Read();
        new StoreWriter(locator).Write(EditedJson(first, 20), first, skipBackup: true);

        // A fresh read is required: the previous snapshot's sequence and
        // version have been consumed.
        StoreSnapshot second = new StoreReader(locator).Read();
        new StoreWriter(locator).Write(EditedJson(second, 30), second, skipBackup: true);

        Assert.Contains("\"currentUIValue\":30", new StoreReader(locator).Read().Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_LargeDocument_SpansBlocksAndStillReadsBack()
    {
        // A preset stack big enough to cross a 32 KiB block forces the
        // First/Middle/Last framing path on the write side.
        using SyntheticStore store = SyntheticStore.Create();
        string padded = SyntheticStore.BuildPresetJson(ExePath, 10, new string('p', 80_000));
        store.WriteLog(4, (100, padded, 40));

        StoreLocator locator = LocatorFor(store);
        StoreSnapshot before = new StoreReader(locator).Read();

        new StoreWriter(locator).Write(EditedJson(before, 44), before, skipBackup: true);

        StoreSnapshot after = new StoreReader(locator).Read();
        Assert.Contains("\"currentUIValue\":44", after.Json, StringComparison.Ordinal);
        Assert.True(after.Json.Length > LevelDbLog.BlockSize * 2);
    }
}
