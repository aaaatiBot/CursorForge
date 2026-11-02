using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Models;
using CursorForge.Client.Services;

namespace CursorForge.Client.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IThemeManagerService _themeManager;
    private readonly ISystemCursorService _cursorService;
    private readonly IStructuredLogger _logger;

    public ObservableCollection<LocalTheme> Themes { get; } = [];

    [ObservableProperty]
    private LocalTheme? _selectedTheme;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    public LibraryViewModel(
        IThemeManagerService themeManager,
        ISystemCursorService cursorService,
        IStructuredLogger logger)
    {
        _themeManager = themeManager;
        _cursorService = cursorService;
        _logger = logger;

        RefreshThemes();
    }

    [RelayCommand]
    public void RefreshThemes()
    {
        Themes.Clear();
        var loaded = _themeManager.LoadAllThemes() ?? [];
        foreach (var theme in loaded)
        {
            Themes.Add(theme);
        }

        if (SelectedTheme == null && Themes.Count > 0)
        {
            SelectedTheme = Themes[0];
        }

        StatusMessage = $"Loaded {Themes.Count} local themes.";
    }

    [RelayCommand]
    public void ApplySelectedTheme()
    {
        if (SelectedTheme == null)
        {
            StatusMessage = "Please select a theme to apply.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Applying '{SelectedTheme.Name}'...";
            _cursorService.ApplyTheme(SelectedTheme);
            StatusMessage = $"Successfully applied '{SelectedTheme.Name}' system-wide.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to apply theme: {ex.Message}";
            _logger.LogError("library.apply_error", "Failed to apply selected theme", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void RestoreOriginalBackup()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Restoring original system cursor backup...";
            _cursorService.RestoreFromBackup();
            StatusMessage = "Original system cursor settings restored.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to restore backup: {ex.Message}";
            _logger.LogError("library.restore_error", "Failed to restore backup", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ResetToWindowsDefaults()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Resetting cursors to Windows default scheme...";
            _cursorService.ResetToWindowsDefaults();
            StatusMessage = "Windows default cursors active.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to reset defaults: {ex.Message}";
            _logger.LogError("library.reset_error", "Failed to reset defaults", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
