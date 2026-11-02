using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;
using CursorForge.Client.ViewModels;

namespace CursorForge.Tests.Unit;

public sealed class BrowseTests : IDisposable
{
    private readonly string _testDir;
    private readonly LocalStorageService _storage;
    private readonly DpapiTokenStorage _tokenStorage;
    private readonly SupabaseClientService _supabase;

    public BrowseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CursorForge_BrowseTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _storage = new LocalStorageService(_testDir);
        string sessionFile = Path.Combine(_testDir, "state", "session.dat");
        _tokenStorage = new DpapiTokenStorage(sessionFile);
        _supabase = new SupabaseClientService(_storage, _tokenStorage);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task GetBrowsePage_Offline_ReturnsDemoCommunityThemes()
    {
        var result = await _supabase.GetBrowsePageAsync(BrowseSort.Newest, null, null, null, 20);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, i => i.Title.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Items, i => Assert.NotEmpty(i.Tags));
    }

    [Fact]
    public async Task GetBrowsePage_SearchFilter_ReturnsMatchingThemesOnly()
    {
        var result = await _supabase.GetBrowsePageAsync(BrowseSort.Newest, "Retro", null, null, 20);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Contains("Retro", result.Items[0].Title);
    }

    [Fact]
    public async Task GetBrowsePage_Sorting_OrdersItemsCorrectly()
    {
        var mostLiked = await _supabase.GetBrowsePageAsync(BrowseSort.MostLiked, null, null, null, 20);
        Assert.NotNull(mostLiked);
        Assert.True(mostLiked.Items.Count >= 2);
        Assert.True(mostLiked.Items[0].LikeCount >= mostLiked.Items[1].LikeCount);

        var mostDownloaded = await _supabase.GetBrowsePageAsync(BrowseSort.MostDownloaded, null, null, null, 20);
        Assert.NotNull(mostDownloaded);
        Assert.True(mostDownloaded.Items.Count >= 2);
        Assert.True(mostDownloaded.Items[0].DownloadCount >= mostDownloaded.Items[1].DownloadCount);
    }

    [Fact]
    public async Task BrowseViewModel_ToggleLike_UpdatesItemStateAndCount()
    {
        var vm = new BrowseViewModel(_supabase);
        await vm.LoadInitialPageAsync();

        Assert.NotEmpty(vm.Items);
        var targetItem = vm.Items[0];
        int originalLikes = targetItem.LikeCount;
        Assert.False(targetItem.IsLikedByMe);

        // First like: sets IsLikedByMe to true and increments like count
        await vm.ToggleLikeCommand.ExecuteAsync(targetItem);
        Assert.True(targetItem.IsLikedByMe);
        Assert.Equal(originalLikes + 1, targetItem.LikeCount);

        // Second like: unlikes, sets IsLikedByMe to false and decrements like count
        await vm.ToggleLikeCommand.ExecuteAsync(targetItem);
        Assert.False(targetItem.IsLikedByMe);
        Assert.Equal(originalLikes, targetItem.LikeCount);
    }

    [Fact]
    public async Task BrowseViewModel_DownloadCursor_IncrementsDownloadCountAndSetsStatus()
    {
        var vm = new BrowseViewModel(_supabase);
        await vm.LoadInitialPageAsync();

        Assert.NotEmpty(vm.Items);
        var targetItem = vm.Items[0];
        int originalDownloads = targetItem.DownloadCount;

        await vm.DownloadCursorCommand.ExecuteAsync(targetItem);

        Assert.Equal(originalDownloads + 1, targetItem.DownloadCount);
        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("downloaded", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
