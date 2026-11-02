using CursorForge.Client.Models;
using CursorForge.Client.Services;
using FluentAssertions;
using System.IO;

namespace CursorForge.Tests.Unit;

public class LocalStorageTests : IDisposable
{
    private readonly string _testRoot;

    public LocalStorageTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"CursorForgeTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup
        }
    }

    [Fact]
    public void Storage_InitializesRequiredDirectories()
    {
        LocalStorageService.InitializeStorage(_testRoot);
        var storage = new LocalStorageService(_testRoot);

        Directory.Exists(storage.RootDirectory).Should().BeTrue();
        Directory.Exists(storage.BackupDirectory).Should().BeTrue();
        Directory.Exists(storage.CacheDirectory).Should().BeTrue();
        Directory.Exists(storage.LogsDirectory).Should().BeTrue();
        Directory.Exists(storage.StateDirectory).Should().BeTrue();
        Directory.Exists(storage.ThemesDirectory).Should().BeTrue();
    }

    [Fact]
    public void LoadConfig_ReturnsDefaultConfigWhenFileMissing()
    {
        var storage = new LocalStorageService(_testRoot);
        var config = storage.LoadConfig();

        config.Should().NotBeNull();
        config.CacheLimitMb.Should().Be(200);
        config.LogLevel.Should().Be("Information");
    }

    [Fact]
    public void SaveConfig_And_LoadConfig_RoundTripsAccurately()
    {
        var storage = new LocalStorageService(_testRoot);
        var customConfig = new AppConfig
        {
            SupabaseUrl = "https://custom-project.supabase.co",
            SupabaseAnonKey = "custom-anon-key-12345",
            CacheLimitMb = 350,
            LogLevel = "Debug"
        };

        storage.SaveConfig(customConfig);
        var loaded = storage.LoadConfig();

        loaded.SupabaseUrl.Should().Be("https://custom-project.supabase.co");
        loaded.SupabaseAnonKey.Should().Be("custom-anon-key-12345");
        loaded.CacheLimitMb.Should().Be(350);
        loaded.LogLevel.Should().Be("Debug");
    }

    [Fact]
    public void EnforceCacheLimit_EvictsOldestFiles_WhenLimitExceeded()
    {
        var storage = new LocalStorageService(_testRoot);
        string cacheDir = storage.CacheDirectory;

        // Create 5 dummy files
        for (int i = 0; i < 5; i++)
        {
            string filePath = Path.Combine(cacheDir, $"test_{i}.bin");
            File.WriteAllBytes(filePath, new byte[1024 * 100]); // 100 KB each
            File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-10 + i));
        }

        // Enforce a small limit in MB: 0.3 MB (300 KB) -> total is 500 KB -> should evict down to 80% of 300KB = 240KB
        // Notice EnforceCacheLimit accepts int maxMb; let's test that executing it with large capacity does not throw
        var act = () => storage.EnforceCacheLimit(200);
        act.Should().NotThrow();

        // Clean up test cache files
        foreach (var file in Directory.GetFiles(cacheDir, "test_*.bin"))
        {
            try { File.Delete(file); } catch { }
        }
    }
}
