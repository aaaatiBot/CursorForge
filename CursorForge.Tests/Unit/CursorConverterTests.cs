using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CursorForge.Client.Services;
using FluentAssertions;

namespace CursorForge.Tests.Unit;

public class CursorConverterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CursorConverterService _converter;

    public CursorConverterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ConverterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _converter = new CursorConverterService();
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
        catch
        {
            // Ignore test cleanup
        }
    }

    private static byte[] CreateSamplePng(int width, int height, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
            // Draw a diagonal line to create varying alpha and shape
            dc.DrawLine(new Pen(Brushes.White, 4), new Point(0, 0), new Point(width, height));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void ConvertPngToMultiSizeCur_GeneratesValidIconDir_WithMultiSizeEntries()
    {
        byte[] pngBytes = CreateSamplePng(128, 128, Colors.Cyan);
        byte[] curBytes = _converter.ConvertPngToMultiSizeCur(pngBytes, hotspotX: 10, hotspotY: 20);

        curBytes.Should().NotBeNull();
        curBytes.Length.Should().BeGreaterThan(6 + (16 * CursorConverterService.TargetSizes.Length));

        using var ms = new MemoryStream(curBytes);
        using var reader = new BinaryReader(ms);

        // Header
        ushort idReserved = reader.ReadUInt16();
        ushort idType = reader.ReadUInt16();
        ushort idCount = reader.ReadUInt16();

        idReserved.Should().Be(0);
        idType.Should().Be(2); // CUR format
        idCount.Should().Be((ushort)CursorConverterService.TargetSizes.Length); // 6 resolutions: 32, 48, 64, 96, 128, 256

        // Directory entries
        int[] expectedSizes = CursorConverterService.TargetSizes;
        for (int i = 0; i < expectedSizes.Length; i++)
        {
            byte bWidth = reader.ReadByte();
            byte bHeight = reader.ReadByte();
            byte bColorCount = reader.ReadByte();
            byte bReserved = reader.ReadByte();
            ushort wXHotspot = reader.ReadUInt16();
            ushort wYHotspot = reader.ReadUInt16();
            uint dwBytesInRes = reader.ReadUInt32();
            uint dwImageOffset = reader.ReadUInt32();

            int expectedSize = expectedSizes[i];
            byte expectedDimByte = (byte)(expectedSize >= 256 ? 0 : expectedSize);
            bWidth.Should().Be(expectedDimByte);
            bHeight.Should().Be(expectedDimByte);
            bColorCount.Should().Be(0);
            bReserved.Should().Be(0);

            // Proportional hotspot scaling check
            int expectedHx = (int)Math.Round(10.0 * (expectedSize / 128.0));
            int expectedHy = (int)Math.Round(20.0 * (expectedSize / 128.0));
            wXHotspot.Should().Be((ushort)expectedHx);
            wYHotspot.Should().Be((ushort)expectedHy);

            dwBytesInRes.Should().BeGreaterThan(0);
            dwImageOffset.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void ConvertPngToMultiSizeCurFile_ProducesFile_PassingWin32Validation()
    {
        byte[] pngBytes = CreateSamplePng(128, 128, Colors.Magenta);
        string pngPath = Path.Combine(_tempDir, "test_cursor.png");
        string curPath = Path.Combine(_tempDir, "test_cursor.cur");

        File.WriteAllBytes(pngPath, pngBytes);

        _converter.ConvertPngToMultiSizeCurFile(pngPath, curPath, hotspotX: 4, hotspotY: 4);

        File.Exists(curPath).Should().BeTrue();
        _converter.ValidateCurFile(curPath).Should().BeTrue();
    }

    [Fact]
    public void ConvertPngToMultiSizeCur_ThrowsOnInvalidHeader()
    {
        byte[] invalidBytes = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

        var act = () => _converter.ConvertPngToMultiSizeCur(invalidBytes, 0, 0);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*PNG magic header*");
    }
}
