using NvFilterStudio.Core.LevelDb;

namespace NvFilterStudio.Core.Tests.LevelDb;

public class LevelDbLogTests
{
    private static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 31 % 251);
        }

        return data;
    }

    [Fact]
    public void AppendBatch_SmallBatch_RoundTrips()
    {
        byte[] batch = Pattern(100);

        byte[] log = LevelDbLog.AppendBatch([], batch);
        IReadOnlyList<byte[]> batches = LevelDbLog.ReadBatches(log);

        Assert.Single(batches);
        Assert.Equal(batch, batches[0]);
    }

    [Fact]
    public void AppendBatch_SmallBatch_IsASingleFullRecord()
    {
        byte[] log = LevelDbLog.AppendBatch([], Pattern(100));

        LogRecord record = Assert.Single(LevelDbLog.ReadRecords(log));
        Assert.Equal(LogRecordType.Full, record.Type);
    }

    [Fact]
    public void AppendBatch_BatchLargerThanABlock_SplitsIntoFragments()
    {
        // Forces First/Middle/Last, the path that silently truncates presets
        // when reassembly is skipped.
        byte[] batch = Pattern((LevelDbLog.BlockSize * 2) + 5000);

        byte[] log = LevelDbLog.AppendBatch([], batch);
        IReadOnlyList<LogRecord> records = LevelDbLog.ReadRecords(log);

        Assert.Equal(LogRecordType.First, records[0].Type);
        Assert.Equal(LogRecordType.Last, records[^1].Type);
        Assert.Contains(records, r => r.Type == LogRecordType.Middle);

        byte[] reassembled = Assert.Single(LevelDbLog.ReadBatches(log));
        Assert.Equal(batch, reassembled);
    }

    [Fact]
    public void AppendBatch_ManyBatches_AllRoundTripInOrder()
    {
        byte[] log = [];
        var expected = new List<byte[]>();

        // Sizes chosen to straddle block boundaries repeatedly.
        foreach (int size in new[] { 10, 4000, 32000, 33000, 70000, 15, 32761 })
        {
            byte[] batch = Pattern(size);
            expected.Add(batch);
            log = LevelDbLog.AppendBatch(log, batch);
        }

        IReadOnlyList<byte[]> actual = LevelDbLog.ReadBatches(log);

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    [Fact]
    public void AppendBatch_EveryRecordHasAValidCrc()
    {
        byte[] log = [];
        foreach (int size in new[] { 100, 40000, 32762, 7 })
        {
            log = LevelDbLog.AppendBatch(log, Pattern(size));
        }

        Assert.Empty(LevelDbLog.FindCorruptRecords(log));
    }

    [Fact]
    public void FindCorruptRecords_DetectsAFlippedByte()
    {
        byte[] log = LevelDbLog.AppendBatch([], Pattern(500));
        log[LevelDbLog.HeaderSize + 10] ^= 0xFF;

        Assert.NotEmpty(LevelDbLog.FindCorruptRecords(log));
    }

    [Fact]
    public void ReadBatches_IgnoresTornTailWrite()
    {
        byte[] log = LevelDbLog.AppendBatch([], Pattern(200));
        byte[] truncated = [.. log, 0x01, 0x02, 0x03];

        // The good record survives; the fragment is discarded rather than
        // throwing, which is what a live log looks like mid-write.
        Assert.Single(LevelDbLog.ReadBatches(truncated));
    }

    [Fact]
    public void AppendBatch_PadsRatherThanSplittingAcrossAHeaderBoundary()
    {
        // Leave fewer than 7 bytes free in the block, so a header cannot fit.
        int fill = LevelDbLog.BlockSize - LevelDbLog.HeaderSize - 3;
        byte[] log = LevelDbLog.AppendBatch([], Pattern(fill));
        log = LevelDbLog.AppendBatch(log, Pattern(50));

        Assert.Empty(LevelDbLog.FindCorruptRecords(log));
        Assert.Equal(2, LevelDbLog.ReadBatches(log).Count);
    }
}
