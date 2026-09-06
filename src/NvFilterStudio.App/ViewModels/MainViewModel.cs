using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
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

    private StoreLocator _locator = new();
    private StoreSnapshot? _snapshot;
    private FilterPresetDocument? _document;

    public MainViewModel()
    {
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

    [ObservableProperty]
    private string _status = "Reading your filters…";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _canWrite;

    [ObservableProperty]
    private string? _loadError;

    /// <summary>Guidance shown while the store is held open.</summary>
    public string BlockedHint =>
        "Filters are read-only right now.\n" +
        "To apply changes: NVIDIA App → Settings → Features → In-Game Overlay → off, " +
        "then close the NVIDIA App. I'll notice on my own.";

    /// <summary>Whether the read succeeded.</summary>
    public bool IsLoaded => _document is not null;

    /// <summary>
    /// Build version, shown in the header so a screenshot identifies its build.
    /// </summary>
    public string DisplayVersion => $"v{AppInfo.ShortVersion}";

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

    /// <summary>Re-reads the store unconditionally.</summary>
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
        OnPropertyChanged(nameof(IsLoaded));
    }

    private void RefreshWritability()
    {
        bool writable = _locator.Exists && _locator.IsWritable();
        if (writable != CanWrite)
        {
            CanWrite = writable;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedGameChanged(GameProfile? value)
    {
        Slots.Clear();
        if (value?.GetGroup(SlotGroupKind.GameFilters) is { } group)
        {
            foreach (Slot slot in group.Slots.Where(s => !s.IsNoneSlot))
            {
                Slots.Add(new SlotOption(slot));
            }
        }

        SelectedSlot = Slots.FirstOrDefault(s => s.Slot.FilterCount > 0) ?? Slots.FirstOrDefault();
    }

    partial void OnSelectedSlotChanged(SlotOption? value) => RebuildFilters();

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

    private void MarkDirty()
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

        SelectedSlot.Slot.MoveFilter(filter.Order, filter.Order - 1);
        RebuildFilters();
        MarkDirty();
    }

    [RelayCommand]
    private void MoveDown(FilterViewModel? filter)
    {
        if (filter is null || SelectedSlot is null || filter.IsLast)
        {
            return;
        }

        SelectedSlot.Slot.MoveFilter(filter.Order, filter.Order + 1);
        RebuildFilters();
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveFilter(FilterViewModel? filter)
    {
        if (filter is null || SelectedSlot is null)
        {
            return;
        }

        SelectedSlot.Slot.RemoveFilterAt(filter.Order);
        RebuildFilters();
        MarkDirty();
        Status = $"Removed {filter.Name}.";
    }

    [RelayCommand]
    private void ResetFilter(FilterViewModel? filter)
    {
        filter?.ResetAll();
        MarkDirty();
    }

    [RelayCommand]
    private void AddFilter()
    {
        if (FilterToAdd is null || SelectedSlot is null)
        {
            return;
        }

        JsonObject? skeleton = _catalogue.CreateSkeleton(FilterToAdd.Shader);
        if (skeleton is null)
        {
            Status = $"No definition known for {FilterToAdd.Shader} yet.";
            return;
        }

        try
        {
            string added = FilterToAdd.DisplayName;
            SelectedSlot.Slot.AddFilter(skeleton);
            RebuildFilters();
            MarkDirty();

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
            Status = $"Applied ♡  backup saved to {result.BackupDirectory}";

            // The write consumed this snapshot's sequence and version, so a
            // fresh read is needed before another write. No prompt here: the
            // edits were just saved, so there is nothing to discard.
            ReloadWithoutAsking();
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
            int applied = ImportPlan.ApplyFile(File.ReadAllText(dialog.FileName), _document!, _catalogue);
            RebuildFilters();
            MarkDirty();
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
            ImportPlan.ApplyShared(preset, SelectedSlot.Slot, _catalogue, out IReadOnlyList<string> missing);

            RebuildFilters();
            MarkDirty();

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

    /// <summary>Summary shown under the chip.</summary>
    public string Summary => Slot.FilterCount == 0
        ? "empty"
        : $"{Slot.FilterCount} filter{(Slot.FilterCount == 1 ? string.Empty : "s")}";

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Label} · {Summary}");
}
