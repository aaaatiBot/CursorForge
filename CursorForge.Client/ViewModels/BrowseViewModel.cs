using CommunityToolkit.Mvvm.ComponentModel;

namespace CursorForge.Client.ViewModels;

public sealed partial class BrowseViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Browse Community Cursors";

    [ObservableProperty]
    private string _searchQuery = string.Empty;
}

