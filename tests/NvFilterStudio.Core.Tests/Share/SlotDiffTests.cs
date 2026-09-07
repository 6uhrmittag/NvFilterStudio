using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Share;

public class SlotDiffTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static Slot SlotWith(int sharpen)
    {
        FilterPresetDocument document =
            FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, sharpen));

        return document.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!;
    }

    private static JsonObject Skeleton(string shader) =>
        new()
        {
            ["id"] = $@"C:\NvCamera\{shader}",
            ["name"] = shader,
            ["isSelected"] = true,
            ["stackIdx"] = 0,
            ["controls"] = new JsonArray(new JsonObject
            {
                ["controlType"] = "slider",
                ["displayName"] = "control 0",
                ["id"] = 0,
                ["dataType"] = "float",
                ["currentValue"] = 0.0,
                ["currentValueArray"] = new JsonArray(0.0),
                ["minValue"] = 0.0,
                ["maxValue"] = 1.0,
                ["stepSize"] = 0.01,
                ["uiMinValue"] = 0.0,
                ["uiMaxValue"] = 100.0,
                ["uiStepSize"] = 1.0,
                ["currentUIValue"] = 0.0,
                ["defaultValue"] = 0.0,
            }),
            ["isPPEFilter"] = false,
            ["isExpanded"] = true,
            ["errorCodes"] = new JsonArray(),
            ["isVisible"] = false,
        };

    [Fact]
    public void Compare_IdenticalSlots_ReportsOnlyUnchanged()
    {
        IReadOnlyList<FilterChange> changes = SlotDiff.Compare(SlotWith(10), SlotWith(10));

        Assert.All(changes, c => Assert.Equal(FilterChangeKind.Unchanged, c.Change));
        Assert.Empty(SlotDiff.Describe(changes));
    }

    [Fact]
    public void Compare_ValueChange_NamesTheControlAndBothValues()
    {
        IReadOnlyList<FilterChange> changes = SlotDiff.Compare(SlotWith(10), SlotWith(55));

        FilterChange change = Assert.Single(changes);
        Assert.Equal(FilterChangeKind.Changed, change.Change);
        Assert.Contains("Sharpen 10→55", change.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_IgnoresFloatRepresentationNoise()
    {
        // Raw values carry noise like 0.19999999999999996. Comparing on those
        // would report a change where the UI shows the same number.
        Slot before = SlotWith(20);
        Slot after = SlotWith(20);
        after.Filters[0].GetControl(0)!.UiValue = 20.0000001;

        Assert.All(SlotDiff.Compare(before, after), c => Assert.Equal(FilterChangeKind.Unchanged, c.Change));
    }

    [Fact]
    public void Compare_AddedFilter()
    {
        Slot after = SlotWith(10);
        after.AddFilter(Skeleton("Color.fx"));

        FilterChange added = Assert.Single(
            SlotDiff.Compare(SlotWith(10), after), c => c.Change == FilterChangeKind.Added);

        Assert.Equal("Color.fx", added.Shader);
    }

    [Fact]
    public void Compare_RemovedFilter()
    {
        Slot before = SlotWith(10);
        Slot after = SlotWith(10);
        after.Clear();

        FilterChange removed = Assert.Single(SlotDiff.Compare(before, after));

        Assert.Equal(FilterChangeKind.Removed, removed.Change);
    }

    [Fact]
    public void Compare_MovedFilter_ReportsThePositions()
    {
        Slot before = SlotWith(10);
        before.AddFilter(Skeleton("Color.fx"));

        Slot after = SlotWith(10);
        after.AddFilter(Skeleton("Color.fx"));
        after.MoveFilter(1, 0);

        IReadOnlyList<FilterChange> changes = SlotDiff.Compare(before, after);

        Assert.Contains(changes, c => c.Change == FilterChangeKind.Moved);
        Assert.Contains(changes, c => c.Detail.Contains('→'));
    }

    [Fact]
    public void Describe_MakesRemovalsStandOut()
    {
        // Removing a filter is the one change that cannot be eyeballed back
        // into place, so it is worth shouting about in a confirmation.
        Slot before = SlotWith(10);
        Slot after = SlotWith(10);
        after.Clear();

        Assert.Contains("REMOVE", SlotDiff.Describe(SlotDiff.Compare(before, after)), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_CountsWhatStaysTheSame()
    {
        Slot before = SlotWith(10);
        before.AddFilter(Skeleton("Color.fx"));

        Slot after = SlotWith(55);
        after.AddFilter(Skeleton("Color.fx"));

        Assert.Contains("1 filter unchanged", SlotDiff.Describe(SlotDiff.Compare(before, after)),
            StringComparison.Ordinal);
    }
}
