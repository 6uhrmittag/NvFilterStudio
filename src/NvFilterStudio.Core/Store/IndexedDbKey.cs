using System.Text;

namespace NvFilterStudio.Core.Store;

/// <summary>
/// The IndexedDB key the NVIDIA Overlay stores filter presets under.
/// </summary>
/// <remarks>
/// Observed layout for <c>FilterPresets_v1</c>:
/// <code>
/// 00 03 0D 01   KeyPrefix: database 3, object store 13, index 1 (object data)
/// 01            IDBKey type: string
/// 10            length in characters (16)
/// 00 46 00 69…  the name in UTF-16 BIG endian
/// </code>
/// Chromium encodes IndexedDB string keys big-endian so that byte order matches
/// code-point order — note this differs from the little-endian UTF-16 used
/// elsewhere in the store.
/// <para>
/// Rather than rebuilding this key, the writer reuses the key bytes read from
/// the existing record verbatim. Matching is done by searching for the
/// big-endian name, which is robust to a future prefix change.
/// </para>
/// </remarks>
public static class IndexedDbKey
{
    /// <summary>Key name currently used by the NVIDIA Overlay.</summary>
    public const string FilterPresetsKeyName = "FilterPresets_v1";

    /// <summary>Prefix shared by any <c>FilterPresets_v*</c> key name.</summary>
    public const string FilterPresetsKeyPrefix = "FilterPresets_v";

    /// <summary>The key name encoded as it appears inside an IndexedDB key.</summary>
    public static byte[] EncodeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Encoding.BigEndianUnicode.GetBytes(name);
    }

    /// <summary>Index id Chromium uses for the object store's own data.</summary>
    /// <remarks>
    /// Index 2 is the <c>ExistsEntry</c> index, which stores a small version
    /// counter under the same name. A real store holds both, and the exists
    /// entry can carry the <em>higher</em> sequence of the two.
    /// </remarks>
    public const int ObjectStoreDataIndexId = 1;

    /// <summary>
    /// Whether <paramref name="key"/> is a filter-preset key, tolerating a
    /// future version bump in the name.
    /// </summary>
    /// <remarks>
    /// The name is searched for rather than the whole key rebuilt, so a change
    /// of database or object-store id does not break matching. The index id is
    /// checked though: without it this also matches the <c>ExistsEntry</c> key,
    /// whose value is a version counter rather than a document. Reading is
    /// currently saved by the value check rejecting it, but the writer reuses
    /// the key bytes it read — so a looser match here is one refactor away from
    /// writing the presets into Chromium's internal index and leaving the real
    /// record untouched.
    /// </remarks>
    public static bool IsFilterPresetsKey(ReadOnlySpan<byte> key) =>
        key.IndexOf(EncodeName(FilterPresetsKeyPrefix).AsSpan()) >= 0 &&
        ReadIndexId(key) == ObjectStoreDataIndexId;

    /// <summary>
    /// Reads the index id from a key's <c>KeyPrefix</c>, or null if malformed.
    /// </summary>
    /// <remarks>
    /// The first byte packs the widths of the three ids that follow:
    /// <code>
    /// bits 7-5  database id length - 1
    /// bits 4-2  object store id length - 1
    /// bits 1-0  index id length - 1
    /// </code>
    /// each id then following as that many little-endian bytes.
    /// </remarks>
    public static int? ReadIndexId(ReadOnlySpan<byte> key)
    {
        if (key.Length < 1)
        {
            return null;
        }

        int databaseBytes = ((key[0] >> 5) & 0x07) + 1;
        int storeBytes = ((key[0] >> 2) & 0x07) + 1;
        int indexBytes = (key[0] & 0x03) + 1;

        int offset = 1 + databaseBytes + storeBytes;
        if (offset + indexBytes > key.Length)
        {
            return null;
        }

        int indexId = 0;
        for (int i = indexBytes - 1; i >= 0; i--)
        {
            indexId = (indexId << 8) | key[offset + i];
        }

        return indexId;
    }
}
