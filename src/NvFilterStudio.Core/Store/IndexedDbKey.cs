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

    /// <summary>
    /// Whether <paramref name="key"/> is a filter-preset key, tolerating a
    /// future version bump in the name.
    /// </summary>
    public static bool IsFilterPresetsKey(ReadOnlySpan<byte> key) =>
        key.IndexOf(EncodeName(FilterPresetsKeyPrefix).AsSpan()) >= 0;
}
