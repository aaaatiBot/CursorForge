using CursorForge.Client.ViewModels;
using Wpf.Ui.Controls;

namespace CursorForge.Client.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}

