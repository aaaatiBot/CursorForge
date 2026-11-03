using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;
using Microsoft.Win32;

namespace CursorForge.Client.ViewModels;

public sealed partial class UploadViewModel : ObservableObject
{
    private readonly ISupabaseClientService _supabase;
    private readonly IThemeManagerService? _themeManager;
    private readonly IStructuredLogger? _logger;

    [ObservableProperty]
    private string _title = "Creator Studio";

    [ObservableProperty]
    private string _themeName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _tagsInput = string.Empty;

    [ObservableProperty]
    private string? _selectedPngPath;

    [ObservableProperty]
    private string? _selectedFileName;

    [ObservableProperty]
    private string? _selectedFileSizeText;

    [ObservableProperty]
    private string? _selectedFileInfoText;

    [ObservableProperty]
    private bool _hasSelectedFile;

    [ObservableProperty]
    private int _imageWidth;

    [ObservableProperty]
    private int _imageHeight;

    [ObservableProperty]
    private int _hotspotX;

    [ObservableProperty]
    private int _hotspotY;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private string? _publishedThemeId;

    public UploadViewModel(ISupabaseClientService supabase, IStructuredLogger? logger)
        : this(supabase, null, logger)
    {
    }

    public UploadViewModel(
        ISupabaseClientService supabase,
        IThemeManagerService? themeManager = null,
        IStructuredLogger? logger = null)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        _themeManager = themeManager;
        _logger = logger;
    }

    [RelayCommand]
    public void SelectPngFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            Title = "Select Primary Cursor Artwork (PNG)"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFile(dialog.FileName);
        }
    }

    [RelayCommand]
    public void ClearSelectedFile()
    {
        SelectedPngPath = null;
        SelectedFileName = null;
        SelectedFileSizeText = null;
        SelectedFileInfoText = null;
        HasSelectedFile = false;
        ImageWidth = 0;
        ImageHeight = 0;
        HotspotX = 0;
        HotspotY = 0;
        ErrorMessage = null;
        StatusMessage = null;
    }

    public void LoadFile(string path)
    {
        ErrorMessage = null;
        IsSuccess = false;
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ErrorMessage = "File does not exist.";
            return;
        }

        var validation = PngValidator.ValidateFile(path);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorMessage;
            return;
        }

        SelectedPngPath = path;
        SelectedFileName = Path.GetFileName(path);
        var fileInfo = new FileInfo(path);
        SelectedFileSizeText = $"{(fileInfo.Length / 1024.0):F1} KB";
        ImageWidth = validation.Width;
        ImageHeight = validation.Height;
        HasSelectedFile = true;
        SelectedFileInfoText = $"{ImageWidth}x{ImageHeight} px  •  {SelectedFileSizeText}  •  Valid Transparent PNG";

        if (string.IsNullOrWhiteSpace(ThemeName))
        {
            string baseName = Path.GetFileNameWithoutExtension(path)
                .Replace("_", " ")
                .Replace("-", " ");
            ThemeName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(baseName);
        }

        // Default hotspot within bounds
        HotspotX = Math.Clamp(HotspotX, 0, Math.Max(0, ImageWidth - 1));
        HotspotY = Math.Clamp(HotspotY, 0, Math.Max(0, ImageHeight - 1));

        StatusMessage = $"Loaded {SelectedFileName} ({ImageWidth}x{ImageHeight} px, {SelectedFileSizeText}). Ready to position hotspot.";
    }

    [RelayCommand]
    public void SetHotspot(string coords)
    {
        if (string.IsNullOrWhiteSpace(coords)) return;
        var parts = coords.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
        {
            HotspotX = Math.Clamp(x, 0, Math.Max(0, ImageWidth - 1));
            HotspotY = Math.Clamp(y, 0, Math.Max(0, ImageHeight - 1));
        }
    }

    [RelayCommand]
    public async Task PublishAsync()
    {
        if (IsUploading) return;

        ErrorMessage = null;
        StatusMessage = null;
        IsSuccess = false;

        if (!_supabase.IsAuthenticated)
        {
            ErrorMessage = "You must be signed in to publish a cursor theme. Please sign in via the Account tab.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ThemeName) || ThemeName.Length < 3)
        {
            ErrorMessage = "Theme name must be at least 3 characters.";
            return;
        }

        if (string.IsNullOrEmpty(SelectedPngPath) || !File.Exists(SelectedPngPath))
        {
            ErrorMessage = "Please select or drop a valid PNG cursor artwork file.";
            return;
        }

        var validation = PngValidator.ValidateFile(SelectedPngPath);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorMessage;
            return;
        }

        IsUploading = true;
        StatusMessage = "Processing cursor artwork and creating theme...";

        try
        {
            var tags = TagsInput
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length > 0)
                .Take(5)
                .ToList();
            if (tags.Count == 0) tags.Add("Custom");

            var roleNames = new List<string> { "Arrow", "Hand", "IBeam", "Wait" };

            // 1. Create upload session
            var session = await _supabase.CreateUploadSessionAsync(ThemeName, tags, roleNames);
            if (session == null)
            {
                ErrorMessage = "Failed to create upload session. You may have exceeded your upload rate limit.";
                return;
            }

            // 2. Validate session server-side
            bool validated = await _supabase.ValidateUploadSessionAsync(session.SessionId);
            if (!validated)
            {
                ErrorMessage = "Server validation failed for artwork.";
                return;
            }

            // 3. Complete session in cloud/catalog
            var hotspots = roleNames.ToDictionary(r => r, _ => new HotspotCoord(HotspotX, HotspotY));
            var createdId = await _supabase.CompleteUploadSessionAsync(session.SessionId, hotspots);

            if (!createdId.HasValue)
            {
                ErrorMessage = "Failed to complete theme publishing.";
                return;
            }

            // 4. Also register and compile into local Library so it is immediately usable on Windows
            if (_themeManager != null && !string.IsNullOrEmpty(SelectedPngPath))
            {
                try
                {
                    var author = _supabase.CurrentUser?.Username ?? "You";
                    var localTheme = _themeManager.CreateThemeFromArtwork(ThemeName, Description, author, SelectedPngPath, HotspotX, HotspotY);
                    PublishedThemeId = localTheme.Id;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("upload.local_theme_warning", "Could not register to local library", ex);
                }
            }

            IsSuccess = true;
            StatusMessage = $"Theme '{ThemeName}' successfully published! It is now visible in 'My Cursors' and installed in your 'Library'.";
            _logger?.LogInformation("upload.success", $"Published theme {createdId.Value}");
        }
        catch (Exception ex)
        {
            _logger?.LogError("upload.publish_error", "Publish failed", ex);
            ErrorMessage = "An error occurred while publishing your theme.";
        }
        finally
        {
            IsUploading = false;
        }
    }
}
