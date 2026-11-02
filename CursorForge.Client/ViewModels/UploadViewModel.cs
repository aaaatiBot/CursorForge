using CommunityToolkit.Mvvm.ComponentModel;

namespace CursorForge.Client.ViewModels;

public sealed partial class UploadViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Upload Cursor Theme";

    [ObservableProperty]
    private string _themeName = string.Empty;
}

