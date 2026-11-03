using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;
using Microsoft.Win32;

namespace CursorForge.Client.ViewModels;

public sealed class SmartRolePreviewItem
{
    public required string RoleId { get; init; }
    public required string DisplayName { get; init; }
    public required BitmapSource PreviewImage { get; init; }
    public required int HotspotX { get; init; }
    public required int HotspotY { get; init; }
}

public sealed partial class UploadViewModel : ObservableObject
{
    private readonly ISupabaseClientService _supabase;
    private readonly IThemeManagerService? _themeManager;
    private readonly IStructuredLogger? _logger;
    private BitmapSource? _rawNormalizedBitmap;
    private Color _dominantColor = Color.FromRgb(0x4C, 0xC2, 0xFF);

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
    private string? _normalizedInfoText;

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
    private int _selectedPointerSize = 256;

    [ObservableProperty]
    private string _selectedPalette = "Original";

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

    public IReadOnlyList<string> AvailablePalettes => CursorImageProcessor.AvailablePalettes;
    public IReadOnlyList<int> AvailableSizes { get; } = [256, 128, 64, 32];

    public ObservableCollection<SmartRolePreviewItem> GeneratedRoles { get; } = [];

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

    partial void OnSelectedPaletteChanged(string value)
    {
        if (_rawNormalizedBitmap != null)
        {
            RefreshSmartRolesBundle();
        }
    }

    [RelayCommand]
    public void SelectPalette(string palette)
    {
        if (!string.IsNullOrWhiteSpace(palette))
        {
            SelectedPalette = palette;
        }
    }

    [RelayCommand]
    public void SelectSize(object? param)
    {
        if (param != null && int.TryParse(param.ToString(), out int size) && size > 0)
        {
            SelectedPointerSize = size;
        }
    }

    [RelayCommand]
    public void SelectPngFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|PNG Image (*.png)|*.png",
            Title = "Select Primary Cursor Artwork"
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
        NormalizedInfoText = null;
        HasSelectedFile = false;
        _rawNormalizedBitmap = null;
        GeneratedRoles.Clear();
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

        try
        {
            // Normalize any dimension/aspect ratio to 256x256 square
            var normalized = CursorImageProcessor.LoadAndNormalizeImage(path, 256);
            _rawNormalizedBitmap = normalized;
            _dominantColor = CursorImageProcessor.ExtractDominantColor(normalized);

            // Save normalized PNG to temp cache so preview and converter use the clean square canvas
            string tempDir = Path.Combine(Path.GetTempPath(), "CursorForge_Normalized");
            Directory.CreateDirectory(tempDir);
            string cachedNormPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.png");
            File.WriteAllBytes(cachedNormPath, CursorImageProcessor.EncodeToPngBytes(normalized));

            SelectedPngPath = cachedNormPath;
            SelectedFileName = Path.GetFileName(path);
            var fileInfo = new FileInfo(path);
            SelectedFileSizeText = $"{(fileInfo.Length / 1024.0):F1} KB";
            ImageWidth = normalized.PixelWidth;
            ImageHeight = normalized.PixelHeight;
            HasSelectedFile = true;

            SelectedFileInfoText = $"{validation.Width}x{validation.Height} px original  •  {SelectedFileSizeText}";
            NormalizedInfoText = $"✨ Auto-fit to {ImageWidth}x{ImageHeight} px square canvas with transparent background.";

            if (string.IsNullOrWhiteSpace(ThemeName))
            {
                string baseName = Path.GetFileNameWithoutExtension(path)
                    .Replace("_", " ")
                    .Replace("-", " ");
                ThemeName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(baseName);
            }

            // Default hotspot
            HotspotX = 0;
            HotspotY = 0;

            RefreshSmartRolesBundle();

            StatusMessage = $"Artwork loaded & normalized to {ImageWidth}x{ImageHeight} px. 15 cursor roles automatically generated!";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("upload.normalize_warning", $"Fallback to raw image file: {ex.Message}");
            SelectedPngPath = path;
            SelectedFileName = Path.GetFileName(path);
            var fileInfo = new FileInfo(path);
            SelectedFileSizeText = $"{(fileInfo.Length / 1024.0):F1} KB";
            ImageWidth = validation.Width > 0 ? validation.Width : 128;
            ImageHeight = validation.Height > 0 ? validation.Height : 128;
            HasSelectedFile = true;
            SelectedFileInfoText = $"{ImageWidth}x{ImageHeight} px  •  {SelectedFileSizeText}";
            StatusMessage = $"Loaded {SelectedFileName}.";
        }
    }

    private void RefreshSmartRolesBundle()
    {
        if (_rawNormalizedBitmap == null) return;

        GeneratedRoles.Clear();
        var bundle = CursorImageProcessor.GenerateSmartRoleBundle(
            _rawNormalizedBitmap,
            HotspotX,
            HotspotY,
            _dominantColor,
            SelectedPalette);

        foreach (var (_, role) in bundle)
        {
            GeneratedRoles.Add(new SmartRolePreviewItem
            {
                RoleId = role.RoleId,
                DisplayName = role.DisplayName,
                PreviewImage = role.Bitmap,
                HotspotX = role.HotspotX,
                HotspotY = role.HotspotY
            });
        }
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
            RefreshSmartRolesBundle();
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
        StatusMessage = "Generating complete 15-cursor scheme bundle and publishing...";

        try
        {
            var tags = TagsInput
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length > 0)
                .Take(5)
                .ToList();
            if (tags.Count == 0) tags.Add("Custom");

            var roleNames = CursorRoleDefinition.StandardRoles.Select(r => r.RoleId).ToList();

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
            var hotspots = roleNames.ToDictionary(
                r => r,
                r => (r == "Arrow" || r == "Help" || r == "AppStarting" || r == "No")
                    ? new HotspotCoord(HotspotX, HotspotY)
                    : new HotspotCoord(ImageWidth / 2, ImageHeight / 2));

            var createdId = await _supabase.CompleteUploadSessionAsync(session.SessionId, hotspots);

            if (!createdId.HasValue)
            {
                ErrorMessage = "Failed to complete theme publishing.";
                return;
            }

            // 4. Register and compile full 15-cursor bundle into local Library
            if (_themeManager != null && !string.IsNullOrEmpty(SelectedPngPath))
            {
                try
                {
                    var author = _supabase.CurrentUser?.Username ?? "You";
                    var localTheme = _themeManager.CreateThemeFromArtwork(
                        ThemeName,
                        Description,
                        author,
                        SelectedPngPath,
                        HotspotX,
                        HotspotY,
                        SelectedPointerSize,
                        SelectedPalette);
                    PublishedThemeId = localTheme.Id;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("upload.local_theme_warning", "Could not register to local library", ex);
                }
            }

            IsSuccess = true;
            StatusMessage = $"Theme '{ThemeName}' ({SelectedPointerSize}px, {SelectedPalette}) successfully published! 15 custom cursors installed to your Library.";
            _logger?.LogInformation("upload.success", $"Published theme {createdId.Value} with 15 smart roles");
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
