using System.ComponentModel;
using System.Windows;
using NvFilterStudio.App.ViewModels;

namespace NvFilterStudio.App;

/// <summary>The application's only window.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    /// <summary>Creates the window and binds it to a fresh view model.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _model = new MainViewModel
        {
            // The view model decides when to ask; the view decides how.
            ConfirmDiscard = message => MessageBox.Show(
                this,
                message,
                "NvFilterStudio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes,

            // Imports and pasted share codes replace a whole stack, so the
            // change is shown before it is made rather than after.
            ConfirmChangeCallback = (what, diff) => MessageBox.Show(
                this,
                $"{what} would:{Environment.NewLine}{Environment.NewLine}{diff}{Environment.NewLine}Go ahead?",
                "NvFilterStudio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes) == MessageBoxResult.Yes,
        };

        DataContext = _model;

        WindowPlacement.Restore(this);
    }

    /// <summary>
    /// Offers to apply pending edits rather than closing over them.
    /// </summary>
    /// <remarks>
    /// Closing used to discard unapplied edits in silence — the exact loss this
    /// tool exists to prevent — so leaving with unsaved work now takes a
    /// deliberate answer.
    /// <para>
    /// Only Reload and closing lose edits. Switching game or slot does not,
    /// because changes are written straight into the in-memory document, so
    /// neither prompts. A warning that fires when nothing is at stake teaches
    /// people to dismiss the one that matters.
    /// </para>
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_model.HasUnsavedChanges && !ShouldClose())
        {
            e.Cancel = true;
        }

        // Only once the close is actually going ahead, so a cancelled close
        // does not overwrite the remembered geometry with a transient state.
        if (!e.Cancel)
        {
            WindowPlacement.Save(this);
        }

        base.OnClosing(e);
    }

    /// <summary>Asks what to do about unsaved edits.</summary>
    private bool ShouldClose()
    {
        // Applying is only offered when it could actually succeed: the overlay
        // holds the store open most of the time, and an Apply button that fails
        // is worse than one that is not there.
        if (!_model.ApplyCommand.CanExecute(null))
        {
            return MessageBox.Show(
                this,
                "You have changes that haven't been applied, and NVIDIA is holding " +
                "the settings open so they can't be saved right now.\n\n" +
                "Close and lose them?",
                "NvFilterStudio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            "You have changes that haven't been applied.\n\n" +
            "Yes — apply them now\n" +
            "No — close and lose them\n" +
            "Cancel — stay here",
            "NvFilterStudio",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        switch (answer)
        {
            case MessageBoxResult.Yes:
                if (_model.TryApplyBeforeClosing())
                {
                    return true;
                }

                // The write did not go through, so closing would still lose it.
                MessageBox.Show(
                    this,
                    $"Your changes could not be applied, so nothing was closed.\n\n{_model.Status}",
                    "NvFilterStudio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;

            case MessageBoxResult.No:
                return true;

            default:
                return false;
        }
    }
}
