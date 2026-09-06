using System.Globalization;
using System.Text;
using NvFilterStudio.Core.LevelDb;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

/// <summary>
/// Builds throwaway LevelDB stores on disk for tests.
/// </summary>
/// <remarks>
/// Everything is synthesised. Real store bytes are never committed: a live log
/// carries the machine owner's NVIDIA account id and session GUIDs, and this
/// repository is public. Generating fixtures also keeps tests hermetic — they
/// do not need NVIDIA installed.
/// </remarks>
internal sealed class SyntheticStore : IDisposable
{
    private SyntheticStore(string directory) => Directory = directory;

    public string Directory { get; }

    public static SyntheticStore Create()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NvFilterStudioTests", Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        return new SyntheticStore(dir);
    }

    /// <summary>The IndexedDB key the overlay uses, as observed on a real store.</summary>
    public static byte[] FilterPresetsKey()
    {
        byte[] name = Encoding.BigEndianUnicode.GetBytes(IndexedDbKey.FilterPresetsKeyName);

        var key = new List<byte>
        {
            0x00, 0x03, 0x0D, 0x01,           // KeyPrefix: db 3, object store 13, index 1
            0x01,                             // IDBKey type: string
            (byte)IndexedDbKey.FilterPresetsKeyName.Length,
        };
        key.AddRange(name);
        return [.. key];
    }

    /// <summary>A structurally faithful preset document.</summary>
    /// <param name="exePath">Game executable the profile is keyed on.</param>
    /// <param name="sharpen">Value for Details.fx control 0, the usual probe.</param>
    /// <param name="padding">Extra filler, to make a record deliberately large.</param>
    public static string BuildPresetJson(string exePath, int sharpen, string padding = "")
    {
        string escaped = exePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        string rawText = (sharpen / 100.0).ToString("0.####", CultureInfo.InvariantCulture);
        string ui = sharpen.ToString(CultureInfo.InvariantCulture);
        const string ShaderPath =
            @"C:\\Windows\\system32\\DriverStore\\FileRepository\\nvmdi.inf_amd64_test\\NvCamera\\Details.fx";

        // Built by concatenation on purpose: raw interpolated literals collide
        // with JSON's runs of consecutive closing braces.
        return string.Concat(
            "{\"filterPresets\":{\"", escaped, "\":{",
            "\"anselSlotsInfo\":{\"lastSlotIdx\":1,\"slots\":[]},",
            "\"modsSlotsInfo\":{\"lastSlotIdx\":3,\"slots\":[",
            "{\"filterStack\":{\"filters\":[],\"selectedFilterCount\":0,",
            "\"upButtonDisabled\":true,\"downButtonDisabled\":true},",
            "\"id\":0,\"altText\":\"settings.None\"},",
            "{\"filterStack\":{\"filters\":[{",
            "\"id\":\"", ShaderPath, "\",",
            "\"name\":\"Details\",\"isSelected\":true,\"stackIdx\":0,\"controls\":[{",
            "\"controlType\":\"slider\",\"displayName\":\"Sharpen\",",
            "\"currentValueArray\":[", rawText, "],\"id\":0,\"dataType\":\"float\",",
            "\"dimension\":0,\"measureUnit\":\"%\",",
            "\"minValue\":0,\"maxValue\":1,\"stepSize\":0.01,",
            "\"currentValue\":", rawText, ",",
            "\"uiMinValue\":0,\"uiMaxValue\":100,\"uiStepSize\":1,",
            "\"currentUIValue\":", ui, ",\"defaultValue\":50}],",
            "\"isPPEFilter\":false,\"isExpanded\":true,\"errorCodes\":[],\"isVisible\":false}],",
            "\"selectedFilterCount\":1,\"upButtonDisabled\":true,\"downButtonDisabled\":true},",
            "\"id\":3,\"altText\":\"3\"}]}}},",
            "\"padding\":\"", padding, "\"}");
    }

    /// <summary>Writes a numbered log containing one preset put per entry given.</summary>
    public void WriteLog(int number, params (ulong Sequence, string Json, ulong Version)[] puts)
    {
        byte[] log = [];
        byte[] key = FilterPresetsKey();

        foreach ((ulong sequence, string json, ulong version) in puts)
        {
            byte[] value = StoreValueCodec.Encode(json, version);
            byte[] batch = WriteBatch.BuildPut(sequence, key, value);
            log = LevelDbLog.AppendBatch(log, batch);
        }

        File.WriteAllBytes(Path.Combine(Directory, $"{number:D6}.log"), log);
    }

    /// <summary>
    /// Writes a fake compacted table. Only the encoded value is present, which
    /// mirrors how the reader recovers records from real tables: by scanning,
    /// because the sorted-block layout is not parsed.
    /// </summary>
    public void WriteTable(int number, string json, ulong version)
    {
        byte[] value = StoreValueCodec.Encode(json, version);
        var content = new List<byte>();
        content.AddRange(Encoding.ASCII.GetBytes("fake-table-header"));
        content.AddRange(value);
        content.AddRange(Encoding.ASCII.GetBytes("fake-table-footer"));

        File.WriteAllBytes(Path.Combine(Directory, $"{number:D6}.ldb"), [.. content]);
    }

    /// <summary>Writes CURRENT plus a MANIFEST carrying <paramref name="lastSequence"/>.</summary>
    public void WriteManifest(int number, ulong lastSequence)
    {
        var edit = new List<byte>();
        Varint.Write(edit, 2);                 // kLogNumber
        Varint.Write(edit, 7);
        Varint.Write(edit, 9);                 // kNextFileNumber
        Varint.Write(edit, 20);
        Varint.Write(edit, 4);                 // kLastSequence
        Varint.Write(edit, lastSequence);

        byte[] manifest = LevelDbLog.AppendBatch([], [.. edit]);
        string name = $"MANIFEST-{number:D6}";
        File.WriteAllBytes(Path.Combine(Directory, name), manifest);
        File.WriteAllText(Path.Combine(Directory, "CURRENT"), name + "\n");
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
