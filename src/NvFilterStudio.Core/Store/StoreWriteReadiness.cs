namespace NvFilterStudio.Core.Store;

/// <summary>What stands between the store and a write.</summary>
public enum StoreWriteBlocker
{
    /// <summary>Nothing; a write can proceed.</summary>
    None,

    /// <summary>No store directory, or no log inside it.</summary>
    StoreMissing,

    /// <summary>The overlay is switched on. Only the setting releases it.</summary>
    OverlayRunning,

    /// <summary>The NVIDIA App is open. Closing the window is enough.</summary>
    AppRunning,

    /// <summary>Nothing recognisable holds it, but the log will not open for writing.</summary>
    Locked,
}

/// <summary>
/// Whether the store can be written, and what the user would have to do about it.
/// </summary>
/// <remarks>
/// One answer for both the Apply button and the writer. They used to test
/// different things — the button asked only whether the log could be opened
/// exclusively, while the writer *also* refused whenever any NVIDIA process was
/// running. With the overlay off but the NVIDIA App still open the log is
/// usually unlocked, so Apply enabled itself and then declined when pressed.
/// <para>
/// The blocker is distinguished rather than reported as one "NVIDIA is running"
/// state because the remedies differ: the overlay is released only by the
/// setting — killing it just makes <c>NvContainerLocalSystem</c> restart it —
/// whereas the App only has to be closed.
/// </para>
/// </remarks>
/// <param name="Blocker">What is in the way.</param>
/// <param name="Holders">Names and ids of the processes found, for diagnostics.</param>
public sealed record StoreWriteReadiness(
    StoreWriteBlocker Blocker,
    IReadOnlyList<(string Name, int Id)> Holders)
{
    /// <summary>A readiness meaning "go ahead".</summary>
    public static StoreWriteReadiness Ready { get; } = new(StoreWriteBlocker.None, []);

    /// <summary>Whether a write can proceed.</summary>
    public bool CanWrite => Blocker is StoreWriteBlocker.None;

    /// <summary>What the user has to do, phrased for them rather than for a log.</summary>
    public string Describe() => Blocker switch
    {
        StoreWriteBlocker.None => "Ready to apply.",

        StoreWriteBlocker.StoreMissing =>
            "No NVIDIA filter store found. This needs the NVIDIA App with the " +
            "In-Game Overlay enabled at least once.",

        // Named separately because closing the overlay's window achieves
        // nothing: NvContainerLocalSystem restarts it within seconds, and only
        // the setting actually releases the database.
        StoreWriteBlocker.OverlayRunning =>
            "The In-Game Overlay is running, so filters are read-only.\n" +
            "Turn it off in NVIDIA App → Settings → Features → In-Game Overlay. " +
            "I'll notice on my own.",

        StoreWriteBlocker.AppRunning =>
            "The NVIDIA App is open, so filters are read-only.\n" +
            "Close it and I'll notice on my own.",

        _ =>
            "Something else is holding the filter store open, so filters are " +
            "read-only. Closing the NVIDIA App usually clears it.",
    };

    /// <summary>The same thing with process ids, for an error message or a bug report.</summary>
    public string DescribeWithHolders() =>
        Holders.Count == 0
            ? Describe()
            : $"{Describe()} ({string.Join(", ", Holders.Select(h => $"{h.Name} {h.Id}"))})";
}
