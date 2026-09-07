using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NvFilterStudio.Core;
using NvFilterStudio.Core.Catalogue;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;
using NvFilterStudio.Core.Store;

namespace NvFilterStudio.App.ViewModels;

/// <summary>Backing view model for the main window.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly FilterCatalogue _catalogue = new();
    private readonly DispatcherTimer _writeWatch;
    private readonly EditHistory _history = new();

    /// <summary>
    /// Whether a run of slider edits has already been recorded.
    /// </summary>
    /// <remarks>
    /// Dragging a slider raises a change per tick, so recording each would
    /// bury everything else in history and make undo useless. A continuous
    /// run of value edits collapses into one step, reset by any structural
    /// change or by moving to another slot.
    /// </remarks>
    private bool _valueEditRecorded;

    private StoreLocator _locator = new();
    private StoreSnapshot? _snapshot;
    private FilterPresetDocument? _document;

    public MainViewModel()
    {
        // Order matters, weakest first: each of these only fills gaps left by
        // the ones before, and reading the store later refreshes anything it
        // covers. The user's own store is the best source (their driver, their
        // language), their cache next, and the shipped seed only ever covers
        // filters they have never used.
        _catalogue.LoadSeed();
        _catalogue.LoadCache();

        // The overlay can be switched off at any moment, so the Apply gate
        // watches rather than making the user press refresh.
        _writeWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _writeWatch.Tick += (_, _) => RefreshWritability();
        _writeWatch.Start();

        Reload();
    }

    /// <summary>Games with presets in the store.</summary>
    public ObservableCollection<GameProfile> Games { get; } = [];

    /// <summary>Slots of the selected game, excluding the overlay's "None" entry.</summary>
    public ObservableCollection<SlotOption> Slots { get; } = [];

    /// <summary>Filters in the selected slot, in stack order.</summary>
    public ObservableCollection<FilterViewModel> Filters { get; } = [];

    /// <summary>Filters that could be added to the selected slot.</summary>
    public ObservableCollection<CatalogueEntry> AddableFilters { get; } = [];

    [ObservableProperty]
    private GameProfile? _selectedGame;

    [ObservableProperty]
    private SlotOption? _selectedSlot;

    [ObservableProperty]
    private CatalogueEntry? _filterToAdd;

    /// <summary>
    /// Whether photo-mode slots are being shown instead of game filters.
    /// </summary>
    /// <remarks>
    /// The store keeps two independent sets per game. Game filters are what
    /// almost everyone wants, so they stay the default and Ansel is a toggle
    /// rather than a peer - visible enough to find, quiet enough not to be
    /// picked by accident.
    /// </remarks>
    [ObservableProperty]
    private bool _showPhotoMode;

    [ObservableProperty]
    private string _status = "Reading your filters…";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _canWrite;

    [ObservableProperty]
    private string? _loadError;

    /// <summary>Guidance shown while the store is held open.</summary>
    /// <remarks>
    /// Comes from the same readiness check that gates Apply, so it names the
    /// condition actually in the way. The old text listed every remedy at once,
    /// which meant telling someone to close an App they had already closed.
    /// </remarks>
    [ObservableProperty]
    private string _blockedHint = string.Empty;

    /// <summary>Whether the read succeeded.</summary>
    public bool IsLoaded => _document is not null;

    /// <summary>Which slot family the UI is editing.</summary>
    private SlotGroupKind ActiveGroup =>
        ShowPhotoMode ? SlotGroupKind.Ansel : SlotGroupKind.GameFilters;

    /// <summary>
    /// Build version, shown in the header so a screenshot identifies its build.
    /// </summary>
    public string DisplayVersion => $"v{BuildInfo.ShortVersion}";

    /// <summary>
    /// How much disk the automatic backups are using.
    /// </summary>
    /// <remarks>
    /// Shown because they are otherwise invisible: a full store copy is written
    /// before every write, and nothing in the UI would tell you they exist.
    /// </remarks>
    [ObservableProperty]
    private string _backupSummary = string.Empty;

    /// <summary>Opens the backup folder in Explorer.</summary>
    [RelayCommand]
    private void OpenBackups()
    {
        string path = StoreWriter.DefaultBackupRoot;

        try
        {
            Directory.CreateDirectory(path);
            using var explorer = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception)
        {
            Status = $"Could not open {path}.";
        }
    }

    private void RefreshBackupSummary()
    {
        long bytes = BackupRetention.TotalSize(StoreWriter.DefaultBackupRoot);
        int count = BackupRetention.List(StoreWriter.DefaultBackupRoot).Count;

        BackupSummary = count == 0
            ? "no backups yet"
            : $"{count} backup{(count == 1 ? string.Empty : "s")}, {BackupRetention.DescribeSize(bytes)}";
    }

    /// <summary>
    /// Asks the user to confirm throwing away unsaved edits. Set by the view.
    /// </summary>
    /// <remarks>
    /// A callback rather than a direct <c>MessageBox</c> call so the decision
    /// stays in the view model, where it can be tested, while the view owns how
    /// the question is presented.
    /// </remarks>
    public Func<string, bool>? ConfirmDiscard { get; set; }

    // ---------------------------------------------------------------- loading

    /// <summary>
    /// Re-reads the store, asking first if that would discard edits.
    /// </summary>
    [RelayCommand]
    public void Reload()
    {
        // Re-reading replaces the whole document, so any edit not yet applied
        // is gone. Editing and then reloading is an easy accident, and losing a
        // hand-tuned profile is the exact failure this tool exists to prevent.
        if (HasUnsavedChanges &&
            ConfirmDiscard?.Invoke(
                "Re-reading from NVIDIA will discard your unapplied changes.\n\nDiscard them?") == false)
        {
            return;
        }

        ReloadWithoutAsking();
    }

    /// <summary>Re-reads the store while keeping the user where they were.</summary>
    /// <remarks>
    /// A plain reload picks the first populated slot, so applying an edit to
    /// slot 3 left you looking at slot 1. Tuning a profile means applying over
    /// and over, which made that a re-navigation every single time.
    /// </remarks>
    private void ReloadKeepingPlace()
    {
        string? exePath = SelectedGame?.ExePath;
        int? slotId = SelectedSlot?.Slot.Id;

        ReloadWithoutAsking();

        // Only restore what still exists. The store is re-read from disk, and
        // nothing guarantees the same game and slot are still there.
        if (exePath is not null &&
            Games.FirstOrDefault(g => g.ExePath == exePath) is { } game)
        {
            SelectedGame = game;
        }

        if (slotId is { } id && Slots.FirstOrDefault(s => s.Slot.Id == id) is { } slot)
        {
            SelectedSlot = slot;
        }
    }

    private void ReloadWithoutAsking()
    {
        try
        {
            _locator = new StoreLocator();
            _snapshot = new StoreReader(_locator).Read();
            _document = FilterPresetDocument.Parse(_snapshot.Json);
            LoadError = null;

            // Every read is a chance to learn filter definitions, which is what
            // makes "add filter" possible later.
            if (_catalogue.LearnFrom(_document) > 0)
            {
                _catalogue.SaveCache();
            }

            Games.Clear();
            foreach (GameProfile game in _document.Games())
            {
                Games.Add(game);
            }

            SelectedGame = Games.FirstOrDefault(g => g.Groups()
                .Any(gr => gr.Slots.Any(s => s.FilterCount > 0))) ?? Games.FirstOrDefault();

            _history.Clear();
            _valueEditRecorded = false;
            RefreshHistoryFlags();
            HasUnsavedChanges = false;
            Status = $"Loaded {Games.Count} game{(Games.Count == 1 ? string.Empty : "s")} ♡";
        }
        catch (Exception ex) when (ex is InvalidDataException or DirectoryNotFoundException or IOException)
        {
            _document = null;
            _snapshot = null;
            LoadError = ex.Message;
            Status = "Could not read the filter store.";
        }

        RefreshWritability();
        RefreshBackupSummary();
        OnPropertyChanged(nameof(IsLoaded));
    }

    private void RefreshWritability()
    {
        // The same check StoreWriter performs. Asking only whether the log could
        // be opened let Apply enable itself while the NVIDIA App was still open,
        // and the write then refused for a reason the button had never shown.
        StoreWriteReadiness readiness = _locator.CheckWriteReadiness();
        BlockedHint = readiness.Describe();

        if (readiness.CanWrite != CanWrite)
        {
            CanWrite = readiness.CanWrite;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedGameChanged(GameProfile? value)
    {
        Slots.Clear();
        if (value?.GetGroup(ActiveGroup) is { } group)
        {
            foreach (Slot slot in group.Slots.Where(s => !s.IsNoneSlot))
            {
                Slots.Add(new SlotOption(slot));
            }
        }

        SelectedSlot = Slots.FirstOrDefault(s => s.Slot.FilterCount > 0) ?? Slots.FirstOrDefault();
    }

    partial void OnShowPhotoModeChanged(bool value)
    {
        // Same game, different slot family, so the slot strip is rebuilt.
        _valueEditRecorded = false;
        OnSelectedGameChanged(SelectedGame);
    }

    partial void OnSelectedSlotChanged(SlotOption? value)
    {
        // Editing a different slot is a different action, so it starts a new
        // undo step rather than joining the previous one.
        _valueEditRecorded = false;
        RebuildFilters();
    }

    private void RebuildFilters()
    {
        Filters.Clear();

        if (SelectedSlot is { } option)
        {
            foreach (FilterEntry filter in option.Slot.Filters)
            {
                Filters.Add(new FilterViewModel(filter, MarkDirty));
            }
        }

        UpdateStackFlags();
        RefreshAddableFilters();
    }

    private void UpdateStackFlags()
    {
        for (int i = 0; i < Filters.Count; i++)
        {
            Filters[i].Order = i;
            Filters[i].IsFirst = i == 0;
            Filters[i].IsLast = i == Filters.Count - 1;
        }
    }

    private void RefreshAddableFilters()
    {
        AddableFilters.Clear();

        // Only one instance of each filter type is allowed in a stack, so
        // anything already present is not offered.
        HashSet<string> present = [.. Filters.Select(f => f.Shader)];

        foreach (CatalogueEntry entry in _catalogue.Entries)
        {
            if (!present.Contains(entry.Shader))
            {
                AddableFilters.Add(entry);
            }
        }

        FilterToAdd = AddableFilters.FirstOrDefault();
    }

    /// <summary>Glyph for the theme toggle: shows what it will switch to.</summary>
    [ObservableProperty]
    private string _themeGlyph = "☽";

    /// <summary>Flips between the light and dark palettes.</summary>
    [RelayCommand]
    private void ToggleTheme() =>
        ThemeGlyph = Themes.ThemeManager.Toggle() == Themes.AppTheme.Dark ? "☀" : "☽";

    /// <summary>Whether a step can be undone.</summary>
    [ObservableProperty]
    private bool _canUndo;

    /// <summary>Whether a step can be redone.</summary>
    [ObservableProperty]
    private bool _canRedo;

    /// <summary>
    /// Records the current state before a change is made.
    /// </summary>
    /// <param name="description">
    /// What is about to happen, phrased so it reads after "Undid".
    /// </param>
    private void RecordUndo(string description)
    {
        if (_document is null)
        {
            return;
        }

        _history.Record(Snapshot(description));
        RefreshHistoryFlags();

        // Whatever run of slider edits was in progress is now its own step.
        _valueEditRecorded = false;
    }

    private EditSnapshot Snapshot(string description) =>
        new(_document!.ToJson(), SelectedGame?.ExePath, SelectedSlot?.Slot.Id, description);

    private void RefreshHistoryFlags()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <remarks>
    /// The state pushed onto the redo stack carries the name of the action being
    /// undone, so redoing it says "Redid your slider changes" rather than the
    /// literal placeholder it used to pass, which read "Redid redo".
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() =>
        Restore(_history.Undo(Snapshot(_history.NextUndoDescription ?? "the change")), undoing: true);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() =>
        Restore(_history.Redo(Snapshot(_history.NextRedoDescription ?? "the change")), undoing: false);

    /// <summary>
    /// Replaces the document with a snapshot and rebuilds the view around it.
    /// </summary>
    /// <remarks>
    /// Everything on screen holds references into the old document, so the
    /// selection has to be re-resolved by identity - executable path and slot
    /// id - rather than by object. Skipping that leaves the UI editing a
    /// document that is no longer the one which will be saved.
    /// </remarks>
    private void Restore(EditSnapshot? snapshot, bool undoing)
    {
        if (snapshot is null || _document is null)
        {
            return;
        }

        _document = FilterPresetDocument.Parse(snapshot.Json);

        Games.Clear();
        foreach (GameProfile game in _document.Games())
        {
            Games.Add(game);
        }

        SelectedGame = Games.FirstOrDefault(g => g.ExePath == snapshot.GameExePath) ?? Games.FirstOrDefault();
        if (snapshot.SlotId is { } slotId)
        {
            SelectedSlot = Slots.FirstOrDefault(s => s.Slot.Id == slotId) ?? SelectedSlot;
        }

        RebuildFilters();
        RefreshHistoryFlags();

        HasUnsavedChanges = true;
        Status = undoing ? $"Undid {snapshot.Description}." : $"Redid {snapshot.Description}.";
    }

    /// <summary>
    /// Asks the user to accept a described change. Set by the view; a null
    /// callback means proceed, which keeps the view model usable headless.
    /// </summary>
    public Func<string, string, bool>? ConfirmChangeCallback { get; set; }

    private bool ConfirmChange(string what, string diff)
    {
        // Nothing would change, so there is nothing to ask about.
        if (string.IsNullOrWhiteSpace(diff))
        {
            return true;
        }

        return ConfirmChangeCallback?.Invoke(what, diff) ?? true;
    }

    /// <summary>Re-finds a slot in another copy of the document.</summary>
    private Slot? FindSlot(FilterPresetDocument document, string exePath, int slotId) =>
        document.GetGame(exePath)?.GetGroup(ActiveGroup)?.GetSlot(slotId);

    /// <summary>
    /// Records an undo point for a run of slider edits, then flags the change.
    /// </summary>
    /// <remarks>
    /// Called from the control view models <em>before</em> they write the new
    /// value, because the undo point is a snapshot of the document and taking it
    /// afterwards captures the edit rather than the state preceding it.
    /// </remarks>
    private void MarkDirty()
    {
        if (!_valueEditRecorded)
        {
            RecordUndo("your slider changes");
            _valueEditRecorded = true;
        }

        SetDirty();
    }

    /// <summary>
    /// Flags unsaved work without touching the undo history.
    /// </summary>
    /// <remarks>
    /// For the commands that record their own undo point before mutating.
    /// Calling <see cref="MarkDirty"/> there recorded a second snapshot, of the
    /// state *after* the change, so the first undo appeared to do nothing and
    /// everything needed undoing twice.
    /// </remarks>
    private void SetDirty()
    {
        HasUnsavedChanges = true;
        Status = "Edited — not applied yet.";
    }

    /// <summary>
    /// Applies pending edits on the way out, for the close prompt.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the write could not happen, so the caller
    /// can keep the window open rather than closing over unsaved work.
    /// </returns>
    public bool TryApplyBeforeClosing()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        if (!CanApply())
        {
            return false;
        }

        Apply();
        return !HasUnsavedChanges;
    }

    // --------------------------------------------------------------- editing

    [RelayCommand]
    private void MoveUp(FilterViewModel? filter)
    {
        if (filter is null || SelectedSlot is null || filter.IsFirst)
        {
            return;
        }

        RecordUndo($"moving {filter.Name} earlier");
        SelectedSlot.Slot.MoveFilter(filter.Order, filter.Order - 1);
        RebuildFilters();
        SetDirty();
    }

    [RelayCommand]
    private void MoveDown(FilterViewModel? filter)
    {
        if (filter is null || SelectedSlot is null || filter.IsLast)
        {
            return;
        }

        RecordUndo($"moving {filter.Name} later");
        SelectedSlot.Slot.MoveFilter(filter.Order, filter.Order + 1);
        RebuildFilters();
        SetDirty();
    }

    [RelayCommand]
    private void RemoveFilter(FilterViewModel? filter)
    {
        if (filter is null || SelectedSlot is null)
        {
            return;
        }

        RecordUndo($"removing {filter.Name}");
        SelectedSlot.Slot.RemoveFilterAt(filter.Order);
        RebuildFilters();
        SetDirty();
        Status = $"Removed {filter.Name}.";
    }

    [RelayCommand]
    private void ResetFilter(FilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        RecordUndo($"resetting {filter.Name}");
        filter.ResetAll();
        SetDirty();
    }

    [RelayCommand]
    private void AddFilter()
    {
        if (FilterToAdd is null || SelectedSlot is null)
        {
            return;
        }

        // The seed stores a bare file name, because the real id embeds the
        // driver's DriverStore hash. Rebase onto whatever directory this
        // machine's own filters use.
        JsonObject? skeleton = _catalogue.CreateSkeleton(
            FilterToAdd.Shader,
            _document is null ? null : FilterCatalogue.FindShaderDirectory(_document));
        if (skeleton is null)
        {
            Status = $"No definition known for {FilterToAdd.Shader} yet.";
            return;
        }

        try
        {
            string added = FilterToAdd.DisplayName;
            RecordUndo($"adding {added}");
            SelectedSlot.Slot.AddFilter(skeleton);
            RebuildFilters();
            SetDirty();

            // FilterNames keys on a shader file name. Passing skeleton["id"],
            // which is a full DriverStore path, missed every time and printed
            // the raw path back at the user.
            Status = $"Added {added} ♡";
        }
        catch (InvalidFilterStackException ex)
        {
            Status = ex.Message;
        }
    }

    // --------------------------------------------------------------- applying

    private bool CanApply() => CanWrite && IsLoaded;

    /// <summary>Writes the edited document back to the store.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (_document is null || _snapshot is null)
        {
            return;
        }

        try
        {
            StoreWriteResult result = new StoreWriter(_locator).Write(_document.ToJson(), _snapshot);

            HasUnsavedChanges = false;

            // The write consumed this snapshot's sequence and version, so a
            // fresh read is needed before another write. No prompt here: the
            // edits were just saved, so there is nothing to discard.
            ReloadKeepingPlace();

            // Set after the reload, which writes a status of its own. Applying
            // is the one action with no visible result - the store is not
            // somewhere the user can go and look - so its confirmation has to
            // survive rather than be overwritten a moment later.
            Status = $"Applied ♡  backup saved to {result.BackupDirectory}";
        }
        catch (Exception ex) when (ex is StoreWriteBlockedException
                                      or InvalidDataException
                                      or NotSupportedException
                                      or IOException)
        {
            Status = ex.Message;
        }
    }

    // ------------------------------------------------------ import / export

    [RelayCommand]
    private void ExportFile()
    {
        if (_document is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "NvFilterStudio export (*.json)|*.json",
            FileName = $"{SelectedGame?.DisplayName ?? "nvidia"}-filters.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var source = new Dictionary<string, string>
        {
            ["tool"] = "NvFilterStudio",
            ["storePath"] = _locator.Directory,
            ["recordSource"] = _snapshot?.SourceFile ?? string.Empty,
        };

        File.WriteAllText(dialog.FileName, ExportDocument.Create(_document, SelectedGame?.ExePath, source));
        Status = $"Exported to {Path.GetFileName(dialog.FileName)} ♡";
    }

    [RelayCommand]
    private void ImportFile()
    {
        var dialog = new OpenFileDialog { Filter = "NvFilterStudio export (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(dialog.FileName);

            // Same reasoning as pasting: preview against a copy first.
            FilterPresetDocument preview = _document!.Clone();
            int wouldApply = ImportPlan.ApplyFile(json, preview, _catalogue);

            if (wouldApply > 0 && SelectedSlot is { } slot &&
                FindSlot(preview, SelectedGame!.ExePath, slot.Slot.Id) is { } previewSlot &&
                !ConfirmChange(
                    $"Importing {Path.GetFileName(dialog.FileName)}",
                    SlotDiff.Describe(SlotDiff.Compare(slot.Slot, previewSlot))))
            {
                Status = "Import cancelled.";
                return;
            }

            RecordUndo("the import");
            int applied = ImportPlan.ApplyFile(json, _document!, _catalogue);
            RebuildFilters();
            SetDirty();
            Status = applied == 0
                ? "Nothing in that file matched a game in your store."
                : $"Imported {applied} slot{(applied == 1 ? string.Empty : "s")} — review, then Apply.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void CopyShareCode()
    {
        if (SelectedSlot is null || SelectedGame is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(ShareCode.Encode(SelectedGame.DisplayName, SelectedSlot.Slot));
            Status = "Share code copied — paste it to a friend ♡";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is a shared resource and another process can hold
            // it; this is transient and not worth an error dialog.
            Status = "Could not reach the clipboard just now — try again.";
        }
    }

    [RelayCommand]
    private void PasteShareCode()
    {
        if (SelectedSlot is null)
        {
            return;
        }

        try
        {
            SharedPreset preset = ShareCode.Decode(Clipboard.GetText());

            // Apply to a throwaway copy first so the change can be shown before
            // it touches the slot the user actually has. A share code from a
            // stranger replaces a whole stack, and "apply and see" is a poor way
            // to discover what it does to a profile that took a while to tune.
            FilterPresetDocument preview = _document!.Clone();
            Slot previewSlot = FindSlot(preview, SelectedGame!.ExePath, SelectedSlot.Slot.Id)!;
            ImportPlan.ApplyShared(preset, previewSlot, _catalogue, out IReadOnlyList<string> missing);

            if (!ConfirmChange(
                    $"Pasting “{preset.Label}” from {preset.Game} into slot {SelectedSlot.Slot.Id}",
                    SlotDiff.Describe(SlotDiff.Compare(SelectedSlot.Slot, previewSlot))))
            {
                Status = "Paste cancelled.";
                return;
            }

            RecordUndo("pasting a share code");
            ImportPlan.ApplyShared(preset, SelectedSlot.Slot, _catalogue, out missing);

            RebuildFilters();
            SetDirty();

            Status = missing.Count == 0
                ? $"Pasted “{preset.Label}” from {preset.Game} — review, then Apply."
                : $"Pasted, but no definition for {string.Join(", ", missing)}. " +
                  "Add each once in the NVIDIA overlay, then reload and paste again.";
        }
        catch (Exception ex) when (ex is InvalidShareCodeException or InvalidFilterStackException)
        {
            Status = ex.Message;
        }
    }
}

/// <summary>A slot as offered in the slot strip.</summary>
public sealed class SlotOption(Slot slot)
{
    /// <summary>The underlying slot.</summary>
    public Slot Slot { get; } = slot;

    /// <summary>Short label for the chip.</summary>
    public string Label => Slot.Label;

    /// <summary>Summary shown under the label.</summary>
    public string Summary => Slot.FilterCount == 0
        ? "empty"
        : $"{Slot.FilterCount} filter{(Slot.FilterCount == 1 ? string.Empty : "s")}";

    /// <summary>
    /// Whether the slot holds nothing, so the tile can say so without words.
    /// </summary>
    /// <remarks>
    /// Which slots are in use is the thing being scanned for, and reading three
    /// summary lines to find out is slower than seeing it.
    /// </remarks>
    public bool IsEmpty => Slot.FilterCount == 0;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Label} · {Summary}");
}
