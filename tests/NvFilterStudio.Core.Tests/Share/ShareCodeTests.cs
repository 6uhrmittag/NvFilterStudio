using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Share;

public class ShareCodeTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static Slot SampleSlot(int sharpen = 10)
    {
        FilterPresetDocument document =
            FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, sharpen));

        return document.Games().First().GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!;
    }

    [Fact]
    public void RoundTrip_PreservesValuesAndOrder()
    {
        string code = ShareCode.Encode("Example", SampleSlot(42));

        SharedPreset decoded = ShareCode.Decode(code);

        Assert.Equal("Example", decoded.Game);
        SharedFilter filter = Assert.Single(decoded.Filters);
        Assert.Equal("Details.fx", filter.Shader);
        Assert.Equal(0, filter.Order);
        Assert.Equal(42, filter.Values[0]);
    }

    [Fact]
    public void Encode_StartsWithTheVersionPrefix()
    {
        // The prefix is what lets the format change later without old codes
        // being misread as new ones.
        Assert.StartsWith("NVF1:", ShareCode.Encode("Example", SampleSlot()), StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_IsShortEnoughToPasteIntoChat()
    {
        // The whole reason share codes exist: a full export is tens of KB.
        string code = ShareCode.Encode("Example", SampleSlot());

        Assert.True(code.Length < 500, $"share code was {code.Length} chars");
    }

    [Fact]
    public void Encode_UsesOnlyUrlAndChatSafeCharacters()
    {
        // '+' and '/' get mangled by chat clients and URLs.
        string body = ShareCode.Encode("Example", SampleSlot())[ShareCode.Prefix.Length..];

        Assert.DoesNotContain('+', body);
        Assert.DoesNotContain('/', body);
        Assert.DoesNotContain('=', body);
    }

    [Fact]
    public void Decode_ToleratesSurroundingWhitespace()
    {
        // Pasted from chat, it will arrive with newlines around it.
        string code = ShareCode.Encode("Example", SampleSlot(33));

        Assert.Equal(33, ShareCode.Decode($"\n  {code}  \n").Filters[0].Values[0]);
    }

    [Fact]
    public void Decode_MissingPrefix_Throws()
    {
        InvalidShareCodeException ex = Assert.Throws<InvalidShareCodeException>(
            () => ShareCode.Decode("just some text"));

        Assert.Contains("NVF1:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_TruncatedCode_ThrowsClearly()
    {
        string code = ShareCode.Encode("Example", SampleSlot());
        string truncated = code[..(code.Length / 2)];

        InvalidShareCodeException ex = Assert.Throws<InvalidShareCodeException>(
            () => ShareCode.Decode(truncated));

        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_GarbageAfterPrefix_Throws()
    {
        Assert.Throws<InvalidShareCodeException>(() => ShareCode.Decode("NVF1:!!!!not-base64!!!!"));
    }

    [Fact]
    public void RoundTrip_MultipleFiltersKeepTheirOrder()
    {
        Slot slot = SampleSlot();
        FilterPresetDocument other =
            FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, 5));

        // Reuse a real filter node as a second entry, renamed to another shader.
        FilterEntry source = other.Games().First()
            .GetGroup(SlotGroupKind.GameFilters)!.GetSlot(3)!.Filters[0];
        System.Text.Json.Nodes.JsonObject clone = source.DetachClone();
        clone["id"] = @"C:\NvCamera\Color.fx";
        slot.AddFilter(clone);

        SharedPreset decoded = ShareCode.Decode(ShareCode.Encode("Example", slot));

        Assert.Equal(["Details.fx", "Color.fx"], decoded.Filters.Select(f => f.Shader));
        Assert.Equal([0, 1], decoded.Filters.Select(f => f.Order));
    }
}
