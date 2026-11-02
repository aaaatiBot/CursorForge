using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using FluentAssertions;
using Moq;

namespace CursorForge.Tests.Unit;

public class ThemeManagerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly Mock<IStructuredLogger> _loggerMock;
    private readonly CursorConverterService _converter;
    private readonly LocalStorageService _storage;
    private readonly ThemeManagerService _themeManager;

    public ThemeManagerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ThemeMgrTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _storage = new LocalStorageService(_testRoot);
        _loggerMock = new Mock<IStructuredLogger>();
        _converter = new CursorConverterService();

        _themeManager = new ThemeManagerService(_storage, _converter, _loggerMock.Object);
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
    public void StarterThemes_AreInstalled_And_Loadable()
    {
        var themes = _themeManager.LoadAllThemes();
        themes.Should().NotBeEmpty();

        var neonTheme = _themeManager.GetTheme("neon-cyan");
        neonTheme.Should().NotBeNull();
        neonTheme!.Name.Should().Be("Neon Cyan");
        neonTheme.Roles.Should().ContainKey("Arrow");
        File.Exists(neonTheme.Roles["Arrow"].FilePath).Should().BeTrue();
    }

    [Fact]
    public void ExportAndImportTheme_RoundTripsThemePackageAccurately()
    {
        var neonTheme = _themeManager.GetTheme("neon-cyan");
        neonTheme.Should().NotBeNull();

        string exportZip = Path.Combine(_testRoot, "exported_neon.cursorforge");
        _themeManager.ExportTheme(neonTheme!.Id, exportZip);

        File.Exists(exportZip).Should().BeTrue();

        // Import under a clean temporary test storage
        string secondaryRoot = Path.Combine(_testRoot, "secondary_storage");
        var secondaryStorage = new LocalStorageService(secondaryRoot);
        var secondaryMgr = new ThemeManagerService(secondaryStorage, _converter, _loggerMock.Object);

        var imported = secondaryMgr.ImportTheme(exportZip);
        imported.Should().NotBeNull();
        imported.Name.Should().Be(neonTheme.Name);
        imported.Roles.Should().ContainKey("Arrow");
        File.Exists(imported.Roles["Arrow"].FilePath).Should().BeTrue();
    }

    [Fact]
    public void DeleteTheme_RemovesThemeDirectory()
    {
        var neon = _themeManager.GetTheme("neon-cyan");
        neon.Should().NotBeNull();

        _themeManager.DeleteTheme("neon-cyan");

        var afterDelete = _themeManager.GetTheme("neon-cyan");
        afterDelete.Should().BeNull();
    }
}
