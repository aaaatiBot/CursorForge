using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;

namespace CursorForge.Client.ViewModels;

public sealed partial class BrowseViewModel : ObservableObject
{
    private readonly ISupabaseClientService _supabase;
    private readonly IStructuredLogger? _logger;

    private DateTime? _nextTimestamp;
    private Guid? _nextId;

    [ObservableProperty]
    private string _title = "Browse Community";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private BrowseSort _selectedSort = BrowseSort.Newest;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private CursorItem? _selectedItem;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<CursorItem> Items { get; } = [];

    public BrowseViewModel(ISupabaseClientService supabase, IStructuredLogger? logger = null)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        _logger = logger;

        _ = LoadInitialPageAsync();
    }

    [RelayCommand]
    public async Task LoadInitialPageAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = null;
        _nextTimestamp = null;
        _nextId = null;

        try
        {
            var paged = await _supabase.GetBrowsePageAsync(SelectedSort, SearchQuery, null, null);
            Items.Clear();
            foreach (var item in paged.Items)
            {
                Items.Add(item);
            }

            HasMore = paged.HasMore;
            _nextTimestamp = paged.NextTimestamp;
            _nextId = paged.NextId;

            if (Items.Count == 0)
            {
                StatusMessage = "No cursors found matching your search.";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("browse.load_initial_failed", "Failed to load browse items", ex);
            StatusMessage = "Failed to load community cursors.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;

        IsLoading = true;
        try
        {
            var paged = await _supabase.GetBrowsePageAsync(SelectedSort, SearchQuery, _nextTimestamp, _nextId);
            foreach (var item in paged.Items)
            {
                Items.Add(item);
            }

            HasMore = paged.HasMore;
            _nextTimestamp = paged.NextTimestamp;
            _nextId = paged.NextId;
        }
        catch (Exception ex)
        {
            _logger?.LogError("browse.load_more_failed", "Failed to load more browse items", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleLikeAsync(CursorItem? item)
    {
        if (item == null) return;

        bool willLike = !item.IsLikedByMe;
        item.IsLikedByMe = willLike;
        item.LikeCount += willLike ? 1 : -1;
        if (item.LikeCount < 0) item.LikeCount = 0;

        // Trigger UI refresh on the item
        int index = Items.IndexOf(item);
        if (index >= 0)
        {
            Items[index] = item;
        }

        bool success = willLike 
            ? await _supabase.LikeCursorAsync(item.Id)
            : await _supabase.UnlikeCursorAsync(item.Id);

        if (!success)
        {
            // Rollback optimistic update on failure
            item.IsLikedByMe = !willLike;
            item.LikeCount += willLike ? -1 : 1;
            if (index >= 0) Items[index] = item;
            StatusMessage = "Unable to register like. Please try again.";
        }
    }

    [RelayCommand]
    public async Task DownloadCursorAsync(CursorItem? item)
    {
        if (item == null) return;

        StatusMessage = $"Downloading '{item.Title}'...";
        await _supabase.IncrementDownloadAsync(item.Id);
        item.DownloadCount++;

        int index = Items.IndexOf(item);
        if (index >= 0) Items[index] = item;

        StatusMessage = $"Theme '{item.Title}' downloaded!";
    }

    [RelayCommand]
    public void SelectSort(string sortName)
    {
        if (Enum.TryParse<BrowseSort>(sortName, true, out var sort))
        {
            SelectedSort = sort;
            _ = LoadInitialPageAsync();
        }
    }
}
