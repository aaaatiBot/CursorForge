using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Services;

namespace CursorForge.Client.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IStructuredLogger _logger;
    private readonly ILocalStorageService _storage;

    public LibraryViewModel LibraryVM { get; }
    public BrowseViewModel BrowseVM { get; }
    public UploadViewModel UploadVM { get; }
    public MyCursorsViewModel MyCursorsVM { get; }
    public AccountViewModel AccountVM { get; }

    [ObservableProperty]
    private ObservableObject _currentView;

    [ObservableProperty]
    private string _bannerText = string.Empty;

    [ObservableProperty]
    private bool _isBannerVisible = false;

    public MainViewModel(
        IStructuredLogger logger,
        ILocalStorageService storage,
        LibraryViewModel libraryVM,
        BrowseViewModel browseVM,
        UploadViewModel uploadVM,
        MyCursorsViewModel myCursorsVM,
        AccountViewModel accountVM,
        CursorForge.Client.Services.Supabase.ISupabaseClientService supabase)
    {
        _logger = logger;
        _storage = storage;

        LibraryVM = libraryVM;
        BrowseVM = browseVM;
        UploadVM = uploadVM;
        MyCursorsVM = myCursorsVM;
        AccountVM = accountVM;

        // If user is already authenticated (from DPAPI session), start on Library.
        // Otherwise, start on Account (Sign In / Sign Up) first.
        _currentView = supabase.IsAuthenticated ? LibraryVM : AccountVM;

        // When user signs in or creates an account, automatically transition to Library
        supabase.AuthStateChanged += (authenticated) =>
        {
            if (authenticated)
            {
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentView = LibraryVM;
                });
            }
        };

        _logger.LogInformation("app.initialized", "CursorForge MainViewModel initialized");
    }

    [RelayCommand]
    public void Navigate(string viewName)
    {
        _logger.LogInformation("navigation.requested", $"Navigating to {viewName}");
        CurrentView = viewName switch
        {
            "Library" => LibraryVM,
            "Browse" => BrowseVM,
            "Upload" => UploadVM,
            "MyCursors" => MyCursorsVM,
            "Account" => AccountVM,
            _ => LibraryVM
        };
    }

    public void ShowBanner(string message)
    {
        BannerText = message;
        IsBannerVisible = !string.IsNullOrWhiteSpace(message);
    }

    [RelayCommand]
    public void HideBanner()
    {
        BannerText = string.Empty;
        IsBannerVisible = false;
    }
}

