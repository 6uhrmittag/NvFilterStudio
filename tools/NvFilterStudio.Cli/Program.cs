using System.Globalization;
using NvFilterStudio.Core.LevelDb;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.Cli;

/// <summary>
/// Console harness for exercising the core library against a real store.
/// </summary>
/// <remarks>
/// Exists so the encoding can be proven end to end before any UI exists, and so
/// the port can be diffed against the original PowerShell tools. Read-only
/// except for <c>set</c>, which goes through the same guarded writer the app
/// uses.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex) when (ex is InvalidDataException
                                      or DirectoryNotFoundException
                                      or StoreWriteBlockedException
                                      or InvalidFilterStackException
                                      or NotSupportedException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string command = args.Length > 0 ? args[0] : "show";
        string? storeDir = ValueOf(args, "--store");
        var locator = new StoreLocator(storeDir);

        switch (command)
        {
            case "show":
                Show(locator);
                return 0;

            case "status":
                Status(locator);
                return 0;

            case "dump":
                Console.WriteLine(new StoreReader(locator).Read().Json);
                return 0;

            case "verify":
                return Verify(locator);

            case "tables":
                return Tables(locator);

            case "set":
                return Set(locator, args);

            default:
                Console.Error.WriteLine(
                    "usage: nvfs [show|status|dump|set] [--store <dir>]\n" +
                    "       nvfs set --game <name> --slot <n> --shader <X.fx> --control <id> --value <ui>");
                return 2;
        }
    }

    /// <summary>
    /// Proves that parsing and re-serialising the live document changes nothing.
    /// </summary>
    /// <remarks>
    /// The riskiest assumption in the whole app: if the JSON DOM drops a field
    /// or reformats a float, every write silently degrades the user's profile.
    /// Checked against the real document rather than a fixture, because only
    /// the real one contains NVIDIA's full field set and awkward values like
    /// 0.19999999999999996.
    /// </remarks>
    private static int Verify(StoreLocator locator)
    {
        StoreSnapshot snapshot = new StoreReader(locator).Read();
        string original = snapshot.Json;
        string reserialised = FilterPresetDocument.Parse(original).ToJson();

        Console.WriteLine($"original     {original.Length} chars");
        Console.WriteLine($"reserialised {reserialised.Length} chars");

        if (string.Equals(original, reserialised, StringComparison.Ordinal))
        {
            Console.WriteLine("round trip is byte-identical.");
            return 0;
        }

        int at = 0;
        while (at < original.Length && at < reserialised.Length && original[at] == reserialised[at])
        {
            at++;
        }

        int from = Math.Max(0, at - 60);
        Console.Error.WriteLine($"MISMATCH at char {at}");
        Console.Error.WriteLine($"  original     ...{Excerpt(original, from)}");
        Console.Error.WriteLine($"  reserialised ...{Excerpt(reserialised, from)}");
        return 1;
    }

    private static string Excerpt(string text, int from) =>
        text[from..Math.Min(text.Length, from + 140)];

    /// <summary>Parses each .ldb table and reports what it holds.</summary>
    private static int Tables(StoreLocator locator)
    {
        if (locator.TablePaths.Count == 0)
        {
            Console.WriteLine("no .ldb tables in this store");
            return 0;
        }

        foreach (string path in locator.TablePaths)
        {
            byte[] bytes = StoreLocator.ReadPossiblyLockedFile(path);
            Console.WriteLine($"{Path.GetFileName(path)}  {bytes.Length:N0} bytes  " +
                              $"table={SsTable.LooksLikeTable(bytes)}");

            try
            {
                IReadOnlyList<TableEntry> entries = [.. SsTable.ReadEntries(bytes)];
                Console.WriteLine($"  entries: {entries.Count}");
                Console.WriteLine($"  highest sequence: {SsTable.HighestSequence(bytes)}");

                var presets = entries
                    .Where(e => IndexedDbKey.IsFilterPresetsKey(e.UserKey.Span))
                    .ToList();

                Console.WriteLine($"  filter-preset entries: {presets.Count}");

                // Highest sequence wins for a given key, and entries are stored
                // in key order rather than sequence order, so sort explicitly.
                foreach (TableEntry entry in presets.OrderByDescending(e => e.Sequence).Take(4))
                {
                    string decoded;
                    try
                    {
                        decoded = $"{StoreValueCodec.Decode(entry.Value).Json.Length} json chars";
                    }
                    catch (InvalidDataException ex)
                    {
                        decoded = $"undecodable ({ex.Message})";
                    }

                    Console.WriteLine($"    seq={entry.Sequence}  key={entry.UserKey.Length}B  " +
                                      $"value={entry.Value.Length}B  {decoded}");
                }
            }
            catch (SsTableFormatException ex)
            {
                Console.WriteLine($"  could not parse: {ex.Message}");
            }
        }

        return 0;
    }

    private static void Status(StoreLocator locator)
    {
        Console.WriteLine($"store      : {locator.Directory}");
        Console.WriteLine($"exists     : {locator.Exists}");
        Console.WriteLine($"live store : {locator.IsLiveStore}");
        Console.WriteLine($"active log : {Path.GetFileName(locator.ActiveLogPath) ?? "(none)"}");
        Console.WriteLine($"tables     : {(locator.TablePaths.Count == 0 ? "(none)" : string.Join(", ", locator.TablePaths.Select(Path.GetFileName)))}");
        Console.WriteLine($"manifest   : {Path.GetFileName(locator.ManifestPath) ?? "(none)"}");
        Console.WriteLine($"writable   : {locator.IsWritable()}");

        IReadOnlyList<(string Name, int Id)> holders = StoreLocator.FindHoldingProcesses();
        Console.WriteLine(holders.Count == 0
            ? "holders    : (none)"
            : $"holders    : {string.Join(", ", holders.Select(h => $"{h.Name}({h.Id})"))}");
    }

    private static void Show(StoreLocator locator)
    {
        StoreSnapshot snapshot = new StoreReader(locator).Read();
        FilterPresetDocument document = FilterPresetDocument.Parse(snapshot.Json);

        Console.WriteLine($"source {snapshot.SourceFile}, value version {snapshot.ValueVersion}, " +
                          $"{snapshot.Json.Length} json bytes, next sequence {snapshot.NextSequence}");

        foreach (GameProfile game in document.Games())
        {
            Console.WriteLine();
            Console.WriteLine($"{game.DisplayName}  ({game.ExePath})");

            foreach (SlotGroup group in game.Groups())
            {
                foreach (Slot slot in group.Slots.Where(s => s.FilterCount > 0))
                {
                    Console.WriteLine($"  {group.Kind} slot {slot.Id} '{slot.Label}' — {slot.FilterCount} filter(s)");

                    foreach (FilterEntry filter in slot.Filters)
                    {
                        Console.WriteLine($"    [{filter.StackIndex}] {filter.Shader}  ({filter.LocalizedName})");
                        foreach (ControlEntry control in filter.Controls)
                        {
                            Console.WriteLine(string.Format(
                                CultureInfo.InvariantCulture,
                                "         id={0,-2} {1,-26} {2,7}  [{3}..{4}] raw={5}",
                                control.Id,
                                control.LocalizedName,
                                control.UiValue,
                                control.UiMinimum,
                                control.UiMaximum,
                                control.RawValue));
                        }
                    }
                }
            }
        }
    }

    private static int Set(StoreLocator locator, string[] args)
    {
        string game = Required(args, "--game");
        int slotId = int.Parse(Required(args, "--slot"), CultureInfo.InvariantCulture);
        string shader = Required(args, "--shader");
        int controlId = int.Parse(Required(args, "--control"), CultureInfo.InvariantCulture);
        double value = double.Parse(Required(args, "--value"), CultureInfo.InvariantCulture);

        StoreSnapshot snapshot = new StoreReader(locator).Read();
        FilterPresetDocument document = FilterPresetDocument.Parse(snapshot.Json);

        GameProfile profile = document.Games()
            .FirstOrDefault(g => g.DisplayName.Contains(game, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"No game matching '{game}'.");

        Slot slot = profile.GetGroup(SlotGroupKind.GameFilters)?.GetSlot(slotId)
            ?? throw new InvalidDataException($"No slot {slotId} for {profile.DisplayName}.");

        FilterEntry filter = slot.Filters
            .FirstOrDefault(f => string.Equals(f.Shader, shader, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"No {shader} in slot {slotId}.");

        ControlEntry control = filter.GetControl(controlId)
            ?? throw new InvalidDataException($"No control {controlId} on {shader}.");

        double before = control.UiValue;
        control.UiValue = value;

        StoreWriteResult result = new StoreWriter(locator).Write(document.ToJson(), snapshot);

        Console.WriteLine($"{shader} control {controlId}: {before} -> {control.UiValue}");
        Console.WriteLine($"backup   {result.BackupDirectory}");
        Console.WriteLine($"sequence {result.Sequence}, value version {result.NewValueVersion}");
        Console.WriteLine($"log      {result.BytesBefore} -> {result.BytesAfter} bytes");
        Console.WriteLine("verified: read-back matches.");
        return 0;
    }

    private static string? ValueOf(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Required(string[] args, string name) =>
        ValueOf(args, name) ?? throw new InvalidDataException($"Missing {name}.");
}
