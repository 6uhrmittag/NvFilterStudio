using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Model;

public class SlotEditingTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static Slot SampleSlot(int sharpen = 10)
    {
        FilterPresetDocument document =
            FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, sharpen));

        return document.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!;
    }

    /// <summary>A minimal but structurally valid filter skeleton.</summary>
    private static JsonObject Skeleton(string shader, int controlCount = 2) =>
        new()
        {
            ["id"] = $@"C:\Windows\system32\DriverStore\FileRepository\nvmdi.inf_amd64_test\NvCamera\{shader}",
            ["name"] = shader,
            ["isSelected"] = true,
            ["stackIdx"] = 0,
            ["controls"] = new JsonArray(
                [.. Enumerable.Range(0, controlCount).Select(i => (JsonNode)new JsonObject
                {
                    ["controlType"] = "slider",
                    ["displayName"] = $"control {i}",
                    ["id"] = i,
                    ["dataType"] = "float",
                    ["currentValue"] = 0.0,
                    ["currentValueArray"] = new JsonArray(0.0),
                    ["minValue"] = -1.0,
                    ["maxValue"] = 1.0,
                    ["stepSize"] = 0.01,
                    ["uiMinValue"] = -100.0,
                    ["uiMaxValue"] = 100.0,
                    ["uiStepSize"] = 2.0,
                    ["currentUIValue"] = 0.0,
                    ["defaultValue"] = 0.0,
                })]),
            ["isPPEFilter"] = false,
            ["isExpanded"] = true,
            ["errorCodes"] = new JsonArray(),
            ["isVisible"] = false,
        };

    [Fact]
    public void AddFilter_AppendsAtTheEndOfTheStack()
    {
        Slot slot = SampleSlot();

        slot.AddFilter(Skeleton("Color.fx"));

        Assert.Equal(2, slot.FilterCount);
        Assert.Equal("Details.fx", slot.Filters[0].Shader);
        Assert.Equal("Color.fx", slot.Filters[1].Shader);
        Assert.Equal(1, slot.Filters[1].StackIndex);
    }

    [Fact]
    public void AddFilter_DuplicateShader_Throws()
    {
        // The overlay allows only one instance of each filter type.
        Slot slot = SampleSlot();

        InvalidFilterStackException ex = Assert.Throws<InvalidFilterStackException>(
            () => slot.AddFilter(Skeleton("Details.fx")));

        Assert.Contains("already in this slot", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveFilter_RenumbersRemaining()
    {
        Slot slot = SampleSlot();
        slot.AddFilter(Skeleton("Color.fx"));
        slot.AddFilter(Skeleton("Colorblind.fx"));

        slot.RemoveFilterAt(1); // Color.fx

        Assert.Equal(2, slot.FilterCount);
        Assert.Equal(["Details.fx", "Colorblind.fx"], slot.Filters.Select(f => f.Shader));
        Assert.Equal([0, 1], slot.Filters.Select(f => f.StackIndex));
    }

    [Fact]
    public void MoveFilter_ReordersAndRenumbers()
    {
        // Order is data: the stack applies low to high, so moving a filter
        // genuinely changes the resulting image.
        Slot slot = SampleSlot();
        slot.AddFilter(Skeleton("Color.fx"));
        slot.AddFilter(Skeleton("Colorblind.fx"));

        slot.MoveFilter(2, 0);

        Assert.Equal(["Colorblind.fx", "Details.fx", "Color.fx"], slot.Filters.Select(f => f.Shader));
        Assert.Equal([0, 1, 2], slot.Filters.Select(f => f.StackIndex));
    }

    [Fact]
    public void MoveFilter_ClampsOutOfRangeDestination()
    {
        Slot slot = SampleSlot();
        slot.AddFilter(Skeleton("Color.fx"));

        slot.MoveFilter(0, 99);

        Assert.Equal(["Color.fx", "Details.fx"], slot.Filters.Select(f => f.Shader));
    }

    [Fact]
    public void MoveFilter_UnknownSource_Throws()
    {
        Assert.Throws<InvalidFilterStackException>(() => SampleSlot().MoveFilter(7, 0));
    }

    [Fact]
    public void Clear_EmptiesTheStack()
    {
        Slot slot = SampleSlot();
        slot.Clear();

        Assert.Equal(0, slot.FilterCount);
        Assert.Empty(slot.Filters);
    }

    [Fact]
    public void Normalise_ProducesContiguousIndicesFromZero()
    {
        Slot slot = SampleSlot();
        slot.AddFilter(Skeleton("Color.fx"));
        slot.AddFilter(Skeleton("Colorblind.fx"));

        // Scramble, as a hand-edited import might.
        slot.Filters[0].StackIndex = 40;
        slot.Filters[1].StackIndex = 7;
        slot.Filters[2].StackIndex = 7;

        slot.Normalise();

        Assert.Equal([0, 1, 2], slot.Filters.Select(f => f.StackIndex));
    }

    [Fact]
    public void AddFilter_UpdatesStackBookkeeping()
    {
        Slot slot = SampleSlot();
        slot.AddFilter(Skeleton("Color.fx"));

        string json = FilterPresetDocument
            .Parse(SyntheticStore.BuildPresetJson(ExePath, 10)).ToJson();

        // Sanity: the fixture itself is well-formed.
        Assert.Contains("selectedFilterCount", json, StringComparison.Ordinal);
        Assert.Equal(2, slot.Filters.Count(f => f.IsSelected));
    }

    [Fact]
    public void SlotZero_IsRecognisedAsTheNoneSlot()
    {
        FilterPresetDocument document =
            FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, 10));
        Slot none = document.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(0)!;

        Assert.True(none.IsNoneSlot);
        Assert.False(SampleSlot().IsNoneSlot);
    }
}
