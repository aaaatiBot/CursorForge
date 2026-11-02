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

    public UploadViewModel(ISupabaseClientService supabase, IStructuredLogger? logger = null)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
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

    public void LoadFile(string path)
    {
        ErrorMessage = null;
        var validation = PngValidator.ValidateFile(path);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorMessage;
            return;
        }

        SelectedPngPath = path;
        ImageWidth = validation.Width;
        ImageHeight = validation.Height;

        // Default hotspot to top-left (0,0) or center if crosshair
        HotspotX = 0;
        HotspotY = 0;
        StatusMessage = $"Loaded {Path.GetFileName(path)} ({ImageWidth}x{ImageHeight} px). Ready for hotspot positioning.";
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
            ErrorMessage = "You must be signed in to publish a cursor theme.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ThemeName) || ThemeName.Length < 3)
        {
            ErrorMessage = "Theme name must be at least 3 characters.";
            return;
        }

        if (string.IsNullOrEmpty(SelectedPngPath) || !File.Exists(SelectedPngPath))
        {
            ErrorMessage = "Please select a valid PNG cursor artwork file.";
            return;
        }

        var validation = PngValidator.ValidateFile(SelectedPngPath);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorMessage;
            return;
        }

        IsUploading = true;
        StatusMessage = "Creating upload session...";

        try
        {
            var tags = TagsInput
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length > 0)
                .Take(5)
                .ToList();

            var roleNames = new List<string> { "Arrow", "Hand", "IBeam", "Wait" };

            // 1. Create upload session
            var session = await _supabase.CreateUploadSessionAsync(ThemeName, tags, roleNames);
            if (session == null)
            {
                ErrorMessage = "Failed to create upload session. You may have exceeded your upload rate limit.";
                return;
            }

            StatusMessage = "Session created. Validating and finalizing theme...";

            // 2. Validate session server-side
            bool validated = await _supabase.ValidateUploadSessionAsync(session.SessionId);
            if (!validated)
            {
                ErrorMessage = "Server validation failed for artwork.";
                return;
            }

            // 3. Complete session
            var hotspots = roleNames.ToDictionary(r => r, _ => new HotspotCoord(HotspotX, HotspotY));
            var createdId = await _supabase.CompleteUploadSessionAsync(session.SessionId, hotspots);

            if (!createdId.HasValue)
            {
                ErrorMessage = "Failed to complete theme publishing.";
                return;
            }

            IsSuccess = true;
            StatusMessage = $"Theme '{ThemeName}' successfully published!";
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
