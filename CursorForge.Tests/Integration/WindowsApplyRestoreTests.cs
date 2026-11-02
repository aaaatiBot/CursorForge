using System.IO;
using System.Text.Json;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using FluentAssertions;
using Microsoft.Win32;
using Moq;

namespace CursorForge.Tests.Integration;

public class WindowsApplyRestoreTests : IDisposable
{
    private readonly string _testRoot;
    private readonly Mock<IStructuredLogger> _loggerMock;
    private readonly CursorConverterService _converter;
    private readonly LocalStorageService _storage;
    private readonly SystemCursorService _cursorService;
    private readonly CursorSchemeSnapshot _initialSnapshot;

    public WindowsApplyRestoreTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ApplyRestoreTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _storage = new LocalStorageService(_testRoot);
        _loggerMock = new Mock<IStructuredLogger>();
        _converter = new CursorConverterService();
        _cursorService = new SystemCursorService(_storage, _loggerMock.Object, _converter);

        // Capture initial system state so we can guarantee clean rollback after tests
        _initialSnapshot = _cursorService.CaptureCurrentSnapshot();
    }

    public void Dispose()
    {
        try
        {
            // Always restore initial system state after tests
            _cursorService.RestoreFromSnapshot(_initialSnapshot);

            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private LocalTheme CreateTestTheme(string themeId, string themeName)
    {
        string themeDir = Path.Combine(_testRoot, "themes", themeId);
        Directory.CreateDirectory(themeDir);

        var roles = new Dictionary<string, LocalCursorRoleItem>();
        byte[] pngBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "StarterThemes", "Neon", "Arrow.png"));

        foreach (var def in CursorRoleDefinition.StandardRoles)
        {
            string curPath = Path.Combine(themeDir, $"{def.RoleId}.cur");
            _converter.ConvertPngToMultiSizeCurFile(
                Path.Combine(AppContext.BaseDirectory, "Assets", "StarterThemes", "Neon", $"{def.RoleId}.png"),
                curPath,
                hotspotX: 4,
                hotspotY: 4);

            roles[def.RoleId] = new LocalCursorRoleItem
            {
                FilePath = curPath,
                HotspotX = 4,
                HotspotY = 4
            };
        }

        return new LocalTheme
        {
            Id = themeId,
            Name = themeName,
            Roles = roles
        };
    }

    [Fact]
    public void ApplyTheme_And_RestoreFromSnapshot_RoundTripsRegistryState()
    {
        var testTheme = CreateTestTheme("test-neon-theme", "Test Neon Theme");

        // Apply theme
        _cursorService.ApplyTheme(testTheme);

        // Verify registry values changed to theme paths
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: false))
        {
            key.Should().NotBeNull();
            string? appliedScheme = key!.GetValue(string.Empty) as string;
            appliedScheme.Should().Be("Test Neon Theme");

            string? arrowPath = key.GetValue("Arrow") as string;
            arrowPath.Should().Be(testTheme.Roles["Arrow"].FilePath);
        }

        // Restore initial snapshot
        _cursorService.RestoreFromSnapshot(_initialSnapshot);

        // Verify registry returned to initial snapshot
        var restoredSnapshot = _cursorService.CaptureCurrentSnapshot();
        restoredSnapshot.DefaultSchemeName.Should().Be(_initialSnapshot.DefaultSchemeName);
    }

    [Fact]
    public void CheckAndRecoverPendingApply_RecoversState_WhenMarkerPresent()
    {
        string stateDir = _storage.StateDirectory;
        string markerPath = Path.Combine(stateDir, "pending_apply.marker");
        string snapshotPath = Path.Combine(stateDir, "pre_apply_snapshot.json");

        // Write marker and snapshot
        File.WriteAllText(markerPath, "aborted-theme-id");
        string json = JsonSerializer.Serialize(_initialSnapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(snapshotPath, json);

        // Execute recovery
        _cursorService.CheckAndRecoverPendingApply();

        // Verify marker and snapshot were cleared
        File.Exists(markerPath).Should().BeFalse();
        File.Exists(snapshotPath).Should().BeFalse();
    }
}
