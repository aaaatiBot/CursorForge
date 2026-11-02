using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;

namespace CursorForge.Client.ViewModels;

public sealed partial class MyCursorsViewModel : ObservableObject
{
    private readonly ISupabaseClientService _supabase;
    private readonly IStructuredLogger? _logger;

    [ObservableProperty]
    private string _title = "My Published Themes";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<CursorItem> Cursors { get; } = [];

    public MyCursorsViewModel(ISupabaseClientService supabase, IStructuredLogger? logger = null)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        _logger = logger;

        _supabase.AuthStateChanged += OnAuthStateChanged;
        IsSignedIn = _supabase.IsAuthenticated;

        _ = LoadCursorsAsync();
    }

    private void OnAuthStateChanged(bool authenticated)
    {
        IsSignedIn = authenticated;
        _ = LoadCursorsAsync();
    }

    [RelayCommand]
    public async Task LoadCursorsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = null;

        try
        {
            if (!_supabase.IsAuthenticated)
            {
                Cursors.Clear();
                StatusMessage = "Please sign in to view and manage your uploaded themes.";
                return;
            }

            var items = await _supabase.GetMyCursorsAsync();
            Cursors.Clear();
            foreach (var item in items)
            {
                Cursors.Add(item);
            }

            if (Cursors.Count == 0)
            {
                StatusMessage = "You haven't uploaded any cursor themes yet. Visit the Creator Studio to publish your first theme!";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("mycursors.load_failed", "Failed to load my cursors", ex);
            StatusMessage = "Failed to load your cursors.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteCursorAsync(CursorItem? item)
    {
        if (item == null) return;

        StatusMessage = $"Deleting '{item.Title}'...";
        bool success = await _supabase.RequestCursorDeletionAsync(item.Id);
        if (success)
        {
            Cursors.Remove(item);
            StatusMessage = $"'{item.Title}' has been queued for deletion.";
        }
        else
        {
            StatusMessage = "Failed to delete theme.";
        }
    }
}
