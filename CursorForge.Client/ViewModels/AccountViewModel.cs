using CommunityToolkit.Mvvm.ComponentModel;

namespace CursorForge.Client.ViewModels;

public sealed partial class AccountViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Account";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private bool _isSignedIn = false;
}

