using System.Text;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

public class IndexedDbKeyTests
{
    /// <summary>Builds a key the way Chromium lays one out.</summary>
    private static byte[] Key(int indexId, string name = "FilterPresets_v1")
    {
        // Prefix byte packs the three id widths, one byte each here.
        List<byte> key = [0x00, 0x03, 0x0D, (byte)indexId, 0x01, (byte)name.Length];
        key.AddRange(Encoding.BigEndianUnicode.GetBytes(name));
        return [.. key];
    }

    [Fact]
    public void IsFilterPresetsKey_AcceptsTheObjectStoreDataKey()
    {
        Assert.True(IndexedDbKey.IsFilterPresetsKey(Key(indexId: 1)));
    }

    [Fact]
    public void IsFilterPresetsKey_RejectsTheExistsEntryKey()
    {
        // Index 2 is Chromium's ExistsEntry index. It carries the same name and
        // can hold a *higher* sequence than the real record, but its value is a
        // version counter. Matching it would let the writer put the presets into
        // Chromium's internal index and leave the real record untouched.
        Assert.False(IndexedDbKey.IsFilterPresetsKey(Key(indexId: 2)));
    }

    [Fact]
    public void IsFilterPresetsKey_ToleratesAVersionBumpInTheName()
    {
        Assert.True(IndexedDbKey.IsFilterPresetsKey(Key(1, "FilterPresets_v2")));
    }

    [Fact]
    public void IsFilterPresetsKey_RejectsSomethingElse()
    {
        Assert.False(IndexedDbKey.IsFilterPresetsKey(Key(1, "SomethingElse_v1")));
        Assert.False(IndexedDbKey.IsFilterPresetsKey([]));
    }

    [Fact]
    public void ReadIndexId_HandlesWiderIds()
    {
        // Prefix byte 0b000_001_01: database 1 byte, object store 2, index 2.
        byte[] key = [0b000_001_01, 0x03, 0x0D, 0x00, 0x01, 0x00];

        Assert.Equal(1, IndexedDbKey.ReadIndexId(key));
    }

    [Fact]
    public void ReadIndexId_TruncatedKey_IsNull()
    {
        Assert.Null(IndexedDbKey.ReadIndexId([0x00, 0x03]));
        Assert.Null(IndexedDbKey.ReadIndexId([]));
    }
}
