using CommunityToolkit.Mvvm.ComponentModel;

namespace CursorForge.Client.ViewModels;

public sealed partial class MyCursorsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "My Cursors";
}

