using System.Text.Json.Nodes;
using NvFilterStudio.Core.Catalogue;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Catalogue;

/// <summary>
/// Covers the filter definitions shipped with the app.
/// </summary>
/// <remarks>
/// These assert properties of the shipped file itself, so a bad regeneration
/// fails the build rather than reaching a user's overlay.
/// </remarks>
public class SeedCatalogueTests
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
    public void LoadSeed_ShipsEveryKnownFilter()
    {
        var catalogue = new FilterCatalogue();

        int added = catalogue.LoadSeed();

        Assert.Equal(18, added);
        Assert.Equal(18, catalogue.Entries.Count);
        Assert.True(catalogue.Knows("OldFilm.fx"));
        Assert.True(catalogue.Knows("BeautifyDOF.fx"));
    }

    [Fact]
    public void LoadSeed_NeverOverridesWhatTheStoreTaught()
    {
        // The user's own store is closer to their machine than a snapshot taken
        // on someone else's: their driver may differ, and their labels are in
        // their own language rather than the English shipped here.
        var catalogue = new FilterCatalogue();
        catalogue.Learn(Filter("OldFilm.fx", uiMax: 250, displayName: "from-the-store"));

        catalogue.LoadSeed();

        JsonNode control = catalogue.CreateSkeleton("OldFilm.fx")!["controls"]![0]!;
        Assert.Equal("from-the-store", control["displayName"]!.GetValue<string>());
        Assert.Equal(250, control["uiMaxValue"]!.GetValue<double>());
    }

    [Fact]
    public void Seed_CarriesNoMachineSpecificPath()
    {
        // A real id embeds the driver's DriverStore hash, which is worthless on
        // anyone else's machine.
        var catalogue = new FilterCatalogue();
        catalogue.LoadSeed();

        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            string id = entry.Skeleton["id"]!.GetValue<string>();

            Assert.False(Path.IsPathRooted(id), $"{entry.Shader} ships an absolute path");
            Assert.DoesNotContain("DriverStore", id, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Seed_LabelsEveryControlInEnglish()
    {
        // The overlay renders whatever displayName it is given and never
        // substitutes its own, so a blank would show as a blank and the
        // harvested German would show as German.
        var catalogue = new FilterCatalogue();
        catalogue.LoadSeed();

        char[] german = ['ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü', 'ß'];

        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            Assert.NotEmpty(entry.Skeleton["name"]!.GetValue<string>());

            foreach (JsonNode? control in entry.Skeleton["controls"]!.AsArray())
            {
                string label = control!["displayName"]!.GetValue<string>();

                Assert.NotEmpty(label);
                Assert.Equal(-1, label.IndexOfAny(german));
            }
        }
    }

    [Fact]
    public void Seed_KeepsBooleanControlsAsBooleans()
    {
        // Regenerating the seed through anything that assumes every control is
        // numeric would quietly turn these into floats.
        var catalogue = new FilterCatalogue();
        catalogue.LoadSeed();

        var beautify = new FilterEntry(catalogue.CreateSkeleton("BeautifyDOF.fx")!);
        ControlEntry toggle = beautify.GetControl(2)!;

        Assert.Equal(ControlKind.Boolean, toggle.Kind);
    }

    [Fact]
    public void CreateSkeleton_RebasesOntoTheLocalShaderDirectory()
    {
        var catalogue = new FilterCatalogue();
        catalogue.LoadSeed();
        const string directory = @"C:\Windows\system32\DriverStore\FileRepository\nvmdi.inf_amd64_other\NvCamera";

        JsonObject skeleton = catalogue.CreateSkeleton("OldFilm.fx", directory)!;

        Assert.Equal(Path.Combine(directory, "OldFilm.fx"), skeleton["id"]!.GetValue<string>());
    }

    [Fact]
    public void CreateSkeleton_WithoutADirectory_LeavesTheIdAlone()
    {
        var catalogue = new FilterCatalogue();
        catalogue.LoadSeed();

        JsonObject skeleton = catalogue.CreateSkeleton("OldFilm.fx")!;

        Assert.Equal("OldFilm.fx", skeleton["id"]!.GetValue<string>());
    }

    [Fact]
    public void FindShaderDirectory_TakesItFromTheDocument()
    {
        // Better than probing the DriverStore, which would mean guessing between
        // installed driver versions; the document records what the overlay
        // itself actually wrote.
        FilterPresetDocument document = FilterPresetDocument.Parse(
            SyntheticStore.BuildPresetJson(@"C:\Games\Example\Example.exe", 10));

        string? directory = FilterCatalogue.FindShaderDirectory(document);

        Assert.NotNull(directory);
        Assert.EndsWith("NvCamera", directory, StringComparison.OrdinalIgnoreCase);
    }
}
