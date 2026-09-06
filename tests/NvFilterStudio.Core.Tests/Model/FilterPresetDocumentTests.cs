using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Model;

public class FilterPresetDocumentTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static FilterPresetDocument Sample(int sharpen = 10) =>
        FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, sharpen));

    [Fact]
    public void RoundTrip_IsByteIdentical()
    {
        string json = SyntheticStore.BuildPresetJson(ExePath, 10);

        Assert.Equal(json, FilterPresetDocument.Parse(json).ToJson());
    }

    [Fact]
    public void RoundTrip_DoesNotEscapeNonAscii()
    {
        // REGRESSION. System.Text.Json escapes non-ASCII by default, turning
        // NVIDIA's raw "Schärfen" into "Sch\u00E4rfen": still valid JSON, but it
        // inflates the record and breaks byte-fidelity with what the overlay
        // wrote. Verified byte-identical against a real 9092-char document.
        const string Json =
            "{\"filterPresets\":{\"g\":{\"name\":\"Schärfen Überstrahlung Tönung\"}}}";

        string round = FilterPresetDocument.Parse(Json).ToJson();

        Assert.Equal(Json, round);
        Assert.DoesNotContain("\\u", round, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_PreservesAwkwardFloatPrecision()
    {
        // The store is full of values like 0.19999999999999996. Reformatting
        // them would drift the profile a little on every save.
        const string Json =
            "{\"filterPresets\":{\"g\":{\"v\":0.19999999999999996,\"w\":-0.30000000000000004}}}";

        Assert.Equal(Json, FilterPresetDocument.Parse(Json).ToJson());
    }

    [Fact]
    public void RoundTrip_PreservesFieldsTheModelDoesNotUnderstand()
    {
        // The DOM approach exists for exactly this: the store holds fields this
        // project never characterised, and dropping them would corrupt a
        // profile invisibly.
        const string Json =
            "{\"filterPresets\":{\"g\":{\"futureField\":{\"nested\":[1,2,3]},\"x\":true}}}";

        Assert.Equal(Json, FilterPresetDocument.Parse(Json).ToJson());
    }

    [Fact]
    public void Parse_NonPresetDocument_Throws()
    {
        Assert.Throws<InvalidDataException>(() => FilterPresetDocument.Parse("{\"other\":1}"));
    }

    [Fact]
    public void Games_ListsEveryProfile()
    {
        GameProfile game = Assert.Single(Sample().Games());

        Assert.Equal(ExePath, game.ExePath);
        Assert.Equal("Example.exe", game.Executable);
        Assert.Equal("Example", game.DisplayName);
    }

    [Fact]
    public void FindGameByExecutable_MatchesRegardlessOfDirectory()
    {
        // The cross-machine case: same game, different drive.
        FilterPresetDocument document = Sample();

        Assert.NotNull(document.FindGameByExecutable("example.exe"));
        Assert.Null(document.FindGameByExecutable("other.exe"));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        FilterPresetDocument original = Sample(10);
        FilterPresetDocument copy = original.Clone();

        Slot slot = copy.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!;
        slot.Filters[0].GetControl(0)!.UiValue = 99;

        Assert.Contains("\"currentUIValue\":10", original.ToJson(), StringComparison.Ordinal);
        Assert.Contains("\"currentUIValue\":99", copy.ToJson(), StringComparison.Ordinal);
    }
}
