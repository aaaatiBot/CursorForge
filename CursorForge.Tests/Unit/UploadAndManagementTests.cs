using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;
using CursorForge.Client.ViewModels;

namespace CursorForge.Tests.Unit;

public sealed class UploadAndManagementTests : IDisposable
{
    private readonly string _testDir;
    private readonly LocalStorageService _storage;
    private readonly DpapiTokenStorage _tokenStorage;
    private readonly SupabaseClientService _supabase;

    public UploadAndManagementTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CursorForge_UploadTests_" + Guid.NewGuid().ToString("N"));
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

    private static byte[] CreateValidPngBytes(int width = 32, int height = 32)
    {
        var bytes = new byte[33];
        // PNG signature
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(sig, 0, bytes, 0, 8);
        // IHDR chunk length = 13 (big endian)
        bytes[8] = 0; bytes[9] = 0; bytes[10] = 0; bytes[11] = 13;
        // Chunk type 'IHDR'
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        // Width
        bytes[16] = (byte)((width >> 24) & 0xFF);
        bytes[17] = (byte)((width >> 16) & 0xFF);
        bytes[18] = (byte)((width >> 8) & 0xFF);
        bytes[19] = (byte)(width & 0xFF);
        // Height
        bytes[20] = (byte)((height >> 24) & 0xFF);
        bytes[21] = (byte)((height >> 16) & 0xFF);
        bytes[22] = (byte)((height >> 8) & 0xFF);
        bytes[23] = (byte)(height & 0xFF);

        return bytes;
    }

    [Fact]
    public void PngValidator_ValidBytes_PassesWithCorrectDimensions()
    {
        byte[] valid = CreateValidPngBytes(64, 48);
        var result = PngValidator.ValidateBytes(valid);

        Assert.True(result.IsValid);
        Assert.Equal(64, result.Width);
        Assert.Equal(48, result.Height);
    }

    [Fact]
    public void PngValidator_InvalidMagicHeader_Fails()
    {
        byte[] invalidSig = CreateValidPngBytes(32, 32);
        invalidSig[0] = 0xFF; // corrupt signature
        var result = PngValidator.ValidateBytes(invalidSig);

        Assert.False(result.IsValid);
        Assert.Contains("signature", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PngValidator_OversizedDimensions_Fails()
    {
        byte[] huge = CreateValidPngBytes(5000, 5000);
        var result = PngValidator.ValidateBytes(huge);

        Assert.False(result.IsValid);
        Assert.Contains("4096x4096", result.ErrorMessage);
    }

    [Fact]
    public void PngValidator_OversizedByteSize_Fails()
    {
        byte[] big = new byte[26 * 1024 * 1024]; // 26 MB
        var result = PngValidator.ValidateBytes(big);

        Assert.False(result.IsValid);
        Assert.Contains("25 MB", result.ErrorMessage);
    }

    [Fact]
    public void UploadViewModel_HotspotClamping_ClampsToBounds()
    {
        var vm = new UploadViewModel(_supabase)
        {
            ImageWidth = 64,
            ImageHeight = 64
        };

        // Coordinates within range
        vm.SetHotspot("10,15");
        Assert.Equal(10, vm.HotspotX);
        Assert.Equal(15, vm.HotspotY);

        // Coordinates overflowing boundary
        vm.SetHotspot("100,200");
        Assert.Equal(63, vm.HotspotX);
        Assert.Equal(63, vm.HotspotY);

        // Negative coordinates
        vm.SetHotspot("-5,-10");
        Assert.Equal(0, vm.HotspotX);
        Assert.Equal(0, vm.HotspotY);
    }

    [Fact]
    public async Task UploadViewModel_Publish_RequiresAuthenticationAndValidMetadata()
    {
        var vm = new UploadViewModel(_supabase);

        // Case 1: Not signed in
        await vm.PublishAsync();
        Assert.False(vm.IsSuccess);
        Assert.Contains("signed in", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Sign in in offline mode
        await _supabase.SignInAsync("creator_user", "ValidPassword123!");

        // Case 2: Empty theme name
        vm.ThemeName = "";
        await vm.PublishAsync();
        Assert.False(vm.IsSuccess);
        Assert.Contains("name", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Case 3: Missing artwork file
        vm.ThemeName = "Valid Theme Name";
        vm.SelectedPngPath = null;
        await vm.PublishAsync();
        Assert.False(vm.IsSuccess);
        Assert.Contains("artwork", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Case 4: Valid PNG artwork
        string samplePng = Path.Combine(_testDir, "cursor.png");
        File.WriteAllBytes(samplePng, CreateValidPngBytes(32, 32));
        vm.LoadFile(samplePng);
        vm.TagsInput = "Neon, Glow, Cyber";

        await vm.PublishAsync();
        Assert.True(vm.IsSuccess);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains("published", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadViewModel_Publish_PersistsToMyCursorsAndLocalLibrary()
    {
        await _supabase.SignInAsync("momo", "MomoSecretPassword123!");
        var logger = new StructuredLogger(_storage.LogsDirectory);
        var converter = new CursorConverterService();
        var themeManager = new ThemeManagerService(_storage, converter, logger);

        var vm = new UploadViewModel(_supabase, themeManager, logger);
        string samplePng = Path.Combine(_testDir, "cat_cursor_128.png");
        string srcPng = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "cat_cursor_128.png");
        if (!File.Exists(srcPng))
        {
            srcPng = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CursorForge.Client", "Assets", "cat_cursor_128.png"));
        }
        File.Copy(srcPng, samplePng, overwrite: true);

        vm.LoadFile(samplePng);
        vm.ThemeName = "Momo Cat Test";
        vm.Description = "Cute cat cursor";
        vm.TagsInput = "Cat, Pixel";
        vm.HotspotX = 8;
        vm.HotspotY = 8;

        await vm.PublishAsync();

        Assert.True(vm.IsSuccess);
        Assert.NotNull(vm.PublishedThemeId);

        // Verify it appears in My Cursors
        var myCursors = await _supabase.GetMyCursorsAsync();
        Assert.Contains(myCursors, c => c.Title == "Momo Cat Test");

        // Verify it appears in local ThemeManager Library
        var allThemes = themeManager.LoadAllThemes();
        var installed = allThemes.FirstOrDefault(t => t.Name == "Momo Cat Test");
        Assert.NotNull(installed);
        Assert.True(installed.Roles.ContainsKey("Arrow"));
        Assert.True(File.Exists(installed.Roles["Arrow"].FilePath));
    }

    [Fact]
    public async Task MyCursorsViewModel_DeleteCursor_RemovesItemFromCollection()
    {
        await _supabase.SignInAsync("creator_user", "ValidPassword123!");
        var vm = new MyCursorsViewModel(_supabase);
        await vm.LoadCursorsAsync();

        Assert.NotEmpty(vm.Cursors);
        var itemToDelete = vm.Cursors[0];

        await vm.DeleteCursorCommand.ExecuteAsync(itemToDelete);

        Assert.DoesNotContain(itemToDelete, vm.Cursors);
        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("queued for deletion", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
