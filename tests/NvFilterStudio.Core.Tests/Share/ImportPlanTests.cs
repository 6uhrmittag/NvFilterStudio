using NvFilterStudio.Core.Catalogue;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Share;

public class ImportPlanTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static FilterPresetDocument Document(int sharpen = 10) =>
        FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, sharpen));

    private static Slot SlotOf(FilterPresetDocument document) =>
        document.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!;

    private static FilterCatalogue CatalogueFrom(FilterPresetDocument document)
    {
        var catalogue = new FilterCatalogue();
        catalogue.LearnFrom(document);
        return catalogue;
    }

    [Fact]
    public void ApplyShared_RestoresValues()
    {
        FilterPresetDocument source = Document(77);
        FilterCatalogue catalogue = CatalogueFrom(source);
        string code = ShareCode.Encode("Example", SlotOf(source));

        FilterPresetDocument target = Document(10);
        Slot targetSlot = SlotOf(target);

        int applied = ImportPlan.ApplyShared(
            ShareCode.Decode(code), targetSlot, catalogue, out IReadOnlyList<string> missing);

        Assert.Equal(1, applied);
        Assert.Empty(missing);
        Assert.Equal(77, targetSlot.Filters[0].GetControl(0)!.UiValue);
    }

    [Fact]
    public void ApplyShared_UnknownShader_IsReportedNotInvented()
    {
        // Refusing to guess is the point: writing fabricated control metadata
        // into someone's store would be worse than doing nothing.
        FilterPresetDocument document = Document();
        var empty = new FilterCatalogue();
        string code = ShareCode.Encode("Example", SlotOf(document));

        int applied = ImportPlan.ApplyShared(
            ShareCode.Decode(code), SlotOf(document), empty, out IReadOnlyList<string> missing);

        Assert.Equal(0, applied);
        Assert.Contains("Details.fx", missing);
    }

    [Fact]
    public void ApplyFile_RestoresFromAnExport()
    {
        FilterPresetDocument source = Document(64);
        string export = ExportDocument.Create(source);

        FilterPresetDocument target = Document(10);
        int applied = ImportPlan.ApplyFile(export, target, new FilterCatalogue());

        Assert.Equal(1, applied);
        Assert.Equal(64, SlotOf(target).Filters[0].GetControl(0)!.UiValue);
    }

    [Fact]
    public void ApplyFile_WorksWithNoCatalogueAtAll()
    {
        // An export carries a verbatim skeleton per filter, which is what makes
        // it restore onto a machine that has never used the filter.
        string export = ExportDocument.Create(Document(21));

        FilterPresetDocument target = Document(10);

        Assert.Equal(1, ImportPlan.ApplyFile(export, target, new FilterCatalogue()));
        Assert.Equal(21, SlotOf(target).Filters[0].GetControl(0)!.UiValue);
    }

    [Fact]
    public void ApplyFile_MatchesByExecutableWhenThePathDiffers()
    {
        // The cross-machine case: same game installed on another drive.
        string export = ExportDocument.Create(Document(55))
            .Replace(@"C:\\Games\\Example\\Example.exe", @"D:\\Steam\\Example\\Example.exe", StringComparison.Ordinal);

        FilterPresetDocument target = Document(10);

        Assert.Equal(1, ImportPlan.ApplyFile(export, target, new FilterCatalogue()));
        Assert.Equal(55, SlotOf(target).Filters[0].GetControl(0)!.UiValue);
    }

    [Fact]
    public void ApplyFile_UnknownGame_ChangesNothing()
    {
        string export = ExportDocument.Create(Document(55))
            .Replace("Example.exe", "SomethingElse.exe", StringComparison.Ordinal);

        FilterPresetDocument target = Document(10);

        Assert.Equal(0, ImportPlan.ApplyFile(export, target, new FilterCatalogue()));
        Assert.Equal(10, SlotOf(target).Filters[0].GetControl(0)!.UiValue);
    }

    [Fact]
    public void ApplyFile_NotAnExport_ThrowsClearly()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => ImportPlan.ApplyFile("{\"nope\":1}", Document(), new FilterCatalogue()));

        Assert.Contains("profiles", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyFile_InvalidJson_ThrowsClearly()
    {
        Assert.Throws<InvalidDataException>(
            () => ImportPlan.ApplyFile("not json at all", Document(), new FilterCatalogue()));
    }

    [Fact]
    public void Catalogue_SkeletonsAreResetToDefaults()
    {
        // A skeleton is a template, not a saved look. Adding a filter must not
        // silently import whoever's values happened to be in the store.
        FilterCatalogue catalogue = CatalogueFrom(Document(99));

        var entry = new FilterEntry(catalogue.CreateSkeleton("Details.fx")!);
        ControlEntry sharpen = entry.GetControl(0)!;

        Assert.Equal(sharpen.UiDefault, sharpen.UiValue);
        Assert.NotEqual(99, sharpen.UiValue);
    }

    [Fact]
    public void Catalogue_RoundTripsThroughItsCache()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            CatalogueFrom(Document()).SaveCache(path);

            var reloaded = new FilterCatalogue();
            reloaded.LoadCache(path);

            Assert.True(reloaded.Knows("Details.fx"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Catalogue_DamagedCacheIsIgnored()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            File.WriteAllText(path, "{ this is not json");

            var catalogue = new FilterCatalogue();
            catalogue.LoadCache(path);

            // Everything in the cache can be relearned from the store, so a bad
            // one is a convenience lost rather than a failure.
            Assert.Empty(catalogue.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
