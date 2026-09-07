using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Core.Tests.Store;

/// <summary>
/// Covers the single answer the Apply gate and the writer now share.
/// </summary>
/// <remarks>
/// They used to test different things: the gate asked only whether the log could
/// be opened exclusively, while the writer also refused whenever an NVIDIA
/// process was running. With the overlay off and the NVIDIA App merely open the
/// log is usually unlocked, so Apply enabled itself and then declined.
/// </remarks>
public class StoreWriteReadinessTests
{
    [Fact]
    public void Ready_CanWrite()
    {
        Assert.True(StoreWriteReadiness.Ready.CanWrite);
        Assert.Equal(StoreWriteBlocker.None, StoreWriteReadiness.Ready.Blocker);
    }

    [Theory]
    [InlineData(StoreWriteBlocker.StoreMissing)]
    [InlineData(StoreWriteBlocker.OverlayRunning)]
    [InlineData(StoreWriteBlocker.AppRunning)]
    [InlineData(StoreWriteBlocker.Locked)]
    public void AnyBlocker_MeansNoWrite(StoreWriteBlocker blocker)
    {
        Assert.False(new StoreWriteReadiness(blocker, []).CanWrite);
    }

    [Fact]
    public void OverlayAndApp_GiveDifferentAdvice()
    {
        // The remedies genuinely differ: the overlay is released only by the
        // setting, because killing it makes NvContainerLocalSystem restart it,
        // whereas the App just has to be closed. Telling someone to close an app
        // they already closed is how a correct message becomes useless.
        string overlay = new StoreWriteReadiness(StoreWriteBlocker.OverlayRunning, []).Describe();
        string app = new StoreWriteReadiness(StoreWriteBlocker.AppRunning, []).Describe();

        Assert.NotEqual(overlay, app);
        Assert.Contains("In-Game Overlay", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("In-Game Overlay", app, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBlocker_SaysSomething()
    {
        foreach (StoreWriteBlocker blocker in Enum.GetValues<StoreWriteBlocker>())
        {
            string text = new StoreWriteReadiness(blocker, []).Describe();

            Assert.False(string.IsNullOrWhiteSpace(text), $"{blocker} has no message");
        }
    }

    [Fact]
    public void DescribeWithHolders_NamesTheProcesses()
    {
        // What goes into the exception, and from there into a bug report.
        var readiness = new StoreWriteReadiness(
            StoreWriteBlocker.OverlayRunning,
            [("NVIDIA Overlay", 1234), ("NVIDIA App", 5678)]);

        string text = readiness.DescribeWithHolders();

        Assert.Contains("1234", text, StringComparison.Ordinal);
        Assert.Contains("5678", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeWithHolders_FallsBackWhenNoneWereFound()
    {
        // The Locked case: nothing recognisable holds it, so there are no ids to
        // list and the bare advice must still come through.
        var readiness = new StoreWriteReadiness(StoreWriteBlocker.Locked, []);

        Assert.Equal(readiness.Describe(), readiness.DescribeWithHolders());
    }

    [Fact]
    public void MissingStore_IsReportedAsMissingRatherThanLocked()
    {
        // A directory that does not exist must not be described as "something is
        // holding it open" — that sends the user hunting for a process.
        var locator = new StoreLocator(Path.Combine(Path.GetTempPath(), "NvFilterStudio-absent"));

        StoreWriteReadiness readiness = locator.CheckWriteReadiness();

        Assert.Equal(StoreWriteBlocker.StoreMissing, readiness.Blocker);
        Assert.False(readiness.CanWrite);
    }

    [Fact]
    public void ACopiedStoreIsWritable()
    {
        // Not the live store, so no NVIDIA process can be holding it. This is
        // the path the tests and the --store override take.
        string directory = Path.Combine(Path.GetTempPath(), $"NvFilterStudio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "000004.log"), [1, 2, 3]);

            StoreWriteReadiness readiness = new StoreLocator(directory).CheckWriteReadiness();

            Assert.True(readiness.CanWrite, readiness.Describe());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
