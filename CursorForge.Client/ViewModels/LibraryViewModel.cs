using CommunityToolkit.Mvvm.ComponentModel;

namespace CursorForge.Client.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Library";

    [ObservableProperty]
    private string _statusMessage = "Ready";
}

