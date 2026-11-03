using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using FluentAssertions;

namespace CursorForge.Tests.Unit;

public class CursorImageProcessorTests : IDisposable
{
    private readonly string _tempDir;

    public CursorImageProcessorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ImageProcessorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private static BitmapSource CreateTestBitmap(int width, int height, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(Brushes.White, null, new Point(width / 2.0, height / 2.0), width / 4.0, height / 4.0);
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    [Fact]
    public void NormalizeToSquare_ArbitraryDimensions_Produces256x256Square()
    {
        // 500x200 rectangular bitmap
        var rectBitmap = CreateTestBitmap(500, 200, Colors.Magenta);
        var normalized = CursorImageProcessor.NormalizeToSquare(rectBitmap, 256);

        normalized.Should().NotBeNull();
        normalized.PixelWidth.Should().Be(256);
        normalized.PixelHeight.Should().Be(256);
    }

    [Fact]
    public void ExtractDominantColor_ReturnsExpectedVibrantColor()
    {
        var pinkBitmap = CreateTestBitmap(128, 128, Color.FromRgb(0xFF, 0x40, 0x81));
        var dominant = CursorImageProcessor.ExtractDominantColor(pinkBitmap);

        dominant.A.Should().Be(255);
        dominant.R.Should().BeGreaterThan(200);
    }

    [Fact]
    public void ApplyColorPalette_AllPalettes_ProduceValidBitmaps()
    {
        var source = CreateTestBitmap(128, 128, Colors.Cyan);

        foreach (var palette in CursorImageProcessor.AvailablePalettes)
        {
            var tinted = CursorImageProcessor.ApplyColorPalette(source, palette);
            tinted.Should().NotBeNull();
            tinted.PixelWidth.Should().Be(128);
            tinted.PixelHeight.Should().Be(128);
        }
    }

    [Fact]
    public void GenerateSmartRoleBundle_GeneratesAll15StandardRoles_WithAccurateHotspots()
    {
        var source = CreateTestBitmap(256, 256, Colors.DeepPink);
        var bundle = CursorImageProcessor.GenerateSmartRoleBundle(source, 10, 15, Colors.DeepPink, "Rose Pink");

        bundle.Should().HaveCount(15);

        // Arrow should retain user hotspot
        bundle.Should().ContainKey("Arrow");
        bundle["Arrow"].HotspotX.Should().Be(10);
        bundle["Arrow"].HotspotY.Should().Be(15);

        // Centered tools should have hotspot at (128, 128)
        bundle["IBeam"].HotspotX.Should().Be(128);
        bundle["IBeam"].HotspotY.Should().Be(128);

        bundle["Wait"].HotspotX.Should().Be(128);
        bundle["Wait"].HotspotY.Should().Be(128);

        bundle["Crosshair"].HotspotX.Should().Be(128);
        bundle["Crosshair"].HotspotY.Should().Be(128);

        bundle["SizeAll"].HotspotX.Should().Be(128);
        bundle["SizeAll"].HotspotY.Should().Be(128);

        // Check PNG bytes
        foreach (var role in bundle.Values)
        {
            role.PngBytes.Should().NotBeNull();
            role.PngBytes.Length.Should().BeGreaterThan(64);
        }
    }

    [Fact]
    public void ThemeManager_CreateThemeFromArtwork_GeneratesFullSchemeWithGiantSize()
    {
        var storage = new LocalStorageService(_tempDir);
        var converter = new CursorConverterService();
        var logger = new StructuredLogger(Path.Combine(_tempDir, "logs"));
        var themeManager = new ThemeManagerService(storage, converter, logger);

        // Create a 400x300 image to verify auto-normalization from any dimension
        var bmp = CreateTestBitmap(400, 300, Colors.MediumPurple);
        string pngPath = Path.Combine(_tempDir, "custom_artwork.png");
        byte[] bytes = CursorImageProcessor.EncodeToPngBytes(bmp);
        File.WriteAllBytes(pngPath, bytes);

        var createdTheme = themeManager.CreateThemeFromArtwork(
            name: "Giant Cosmic Cat",
            description: "High resolution giant pointer scheme",
            author: "Tester",
            pngPath: pngPath,
            hotspotX: 12,
            hotspotY: 14,
            cursorBaseSize: 256,
            colorPalette: "Royal Violet");

        createdTheme.Should().NotBeNull();
        createdTheme.CursorBaseSize.Should().Be(256);
        createdTheme.Roles.Should().HaveCount(15);

        // Verify each .cur file was compiled and passes Win32 LoadCursorFromFile validation
        foreach (var (roleId, item) in createdTheme.Roles)
        {
            File.Exists(item.FilePath).Should().BeTrue();
            converter.ValidateCurFile(item.FilePath).Should().BeTrue();
        }
    }
}
