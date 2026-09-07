using System.Text.Json.Nodes;
using NvFilterStudio.Core.Catalogue;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Tests.Share;

public class FilterCatalogueTests
{
    private static FilterEntry Filter(string shader, double uiMax, string displayName) =>
        new(new JsonObject
        {
            ["id"] = $@"C:\DriverStore\nvmdi.inf_amd64_test\NvCamera\{shader}",
            ["name"] = shader,
            ["isSelected"] = false,
            ["stackIdx"] = 0,
            ["controls"] = new JsonArray(new JsonObject
            {
                ["controlType"] = "slider",
                ["displayName"] = displayName,
                ["id"] = 0,
                ["dataType"] = "float",
                ["currentValue"] = 0.5,
                ["currentValueArray"] = new JsonArray(0.5),
                ["minValue"] = 0.0,
                ["maxValue"] = 1.0,
                ["stepSize"] = 0.01,
                ["uiMinValue"] = 0.0,
                ["uiMaxValue"] = uiMax,
                ["uiStepSize"] = 1.0,
                ["currentUIValue"] = 50.0,
                ["defaultValue"] = 50.0,
            }),
            ["isPPEFilter"] = false,
            ["isExpanded"] = false,
            ["errorCodes"] = new JsonArray(),
            ["isVisible"] = false,
        });

    [Fact]
    public void Learn_RefreshesAnExistingDefinition()
    {
        // The store is authoritative and the catalogue only caches it. Keeping
        // the first sighting meant a definition could never be corrected — a
        // bound changed by a driver update, or a name learned from a bad record,
        // was stuck permanently.
        var catalogue = new FilterCatalogue();

        catalogue.Learn(Filter("Ghost.fx", uiMax: 100, displayName: "wrong-name"));
        catalogue.Learn(Filter("Ghost.fx", uiMax: 250, displayName: "right-name"));

        JsonObject skeleton = catalogue.CreateSkeleton("Ghost.fx")!;
        JsonNode control = skeleton["controls"]![0]!;

        Assert.Equal("right-name", control["displayName"]!.GetValue<string>());
        Assert.Equal(250, control["uiMaxValue"]!.GetValue<double>());
    }

    [Fact]
    public void Learn_ReportsOnlyGenuinelyNewShaders()
    {
        // The return value drives whether the cache file gets rewritten, so a
        // refresh of something already known must not count as a discovery.
        var catalogue = new FilterCatalogue();

        Assert.True(catalogue.Learn(Filter("Ghost.fx", 100, "a")));
        Assert.False(catalogue.Learn(Filter("Ghost.fx", 250, "b")));
    }

    [Fact]
    public void CreateSkeleton_HandsOutADetachedCopy()
    {
        // Two adds of the same filter must not share a node, or editing one
        // would silently edit the other.
        var catalogue = new FilterCatalogue();
        catalogue.Learn(Filter("Ghost.fx", 100, "a"));

        JsonObject first = catalogue.CreateSkeleton("Ghost.fx")!;
        JsonObject second = catalogue.CreateSkeleton("Ghost.fx")!;
        first["controls"]![0]!["displayName"] = "changed";

        Assert.Equal("a", second["controls"]![0]!["displayName"]!.GetValue<string>());
        Assert.Equal("a", catalogue.CreateSkeleton("Ghost.fx")!["controls"]![0]!["displayName"]!.GetValue<string>());
    }
}
