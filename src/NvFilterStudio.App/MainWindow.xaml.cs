using System.Windows;
using NvFilterStudio.App.ViewModels;

namespace NvFilterStudio.App;

/// <summary>The application's only window.</summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window and binds it to a fresh view model.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
