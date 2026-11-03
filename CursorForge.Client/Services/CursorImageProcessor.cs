using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CursorForge.Client.Models;

namespace CursorForge.Client.Services;

public sealed class SmartRoleResult
{
    public required string RoleId { get; init; }
    public required string DisplayName { get; init; }
    public required BitmapSource Bitmap { get; init; }
    public required int HotspotX { get; init; }
    public required int HotspotY { get; init; }
    public required byte[] PngBytes { get; init; }
}

public static class CursorImageProcessor
{
    public const int DefaultCanvasSize = 256;

    public static readonly string[] AvailablePalettes =
    [
        "Original",
        "Neon Cyan",
        "Cyber Gold",
        "Rose Pink",
        "Royal Violet",
        "Emerald Mint",
        "Midnight Stealth"
    ];

    public static BitmapSource LoadAndNormalizeImage(string filePath, int targetSize = DefaultCanvasSize)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Image file not found.", filePath);

        using var stream = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidOperationException("Could not decode image frames.");

        var frame = decoder.Frames[0];
        return NormalizeToSquare(frame, targetSize);
    }

    public static BitmapSource NormalizeToSquare(BitmapSource source, int targetSize = DefaultCanvasSize)
    {
        int srcWidth = source.PixelWidth;
        int srcHeight = source.PixelHeight;

        if (srcWidth == targetSize && srcHeight == targetSize && source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        // Maintain aspect ratio and center on transparent square
        double scale = Math.Min((double)targetSize / srcWidth, (double)targetSize / srcHeight);
        double drawW = Math.Round(srcWidth * scale);
        double drawH = Math.Round(srcHeight * scale);
        double offsetX = Math.Round((targetSize - drawW) / 2.0);
        double offsetY = Math.Round((targetSize - drawH) / 2.0);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, targetSize, targetSize));
            dc.DrawImage(source, new Rect(offsetX, offsetY, drawW, drawH));
        }

        var rtb = new RenderTargetBitmap(targetSize, targetSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static Color ExtractDominantColor(BitmapSource source)
    {
        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        long rSum = 0, gSum = 0, bSum = 0, count = 0;
        double maxSat = -1;
        Color mostVibrant = Color.FromRgb(0x4C, 0xC2, 0xFF); // Default cyan

        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                int idx = (y * stride) + (x * 4);
                byte b = pixels[idx];
                byte g = pixels[idx + 1];
                byte r = pixels[idx + 2];
                byte a = pixels[idx + 3];

                if (a > 64)
                {
                    RgbToHsv(r, g, b, out _, out double s, out double v);
                    if (s > 0.25 && v > 0.3)
                    {
                        rSum += r;
                        gSum += g;
                        bSum += b;
                        count++;

                        if (s > maxSat)
                        {
                            maxSat = s;
                            mostVibrant = Color.FromRgb(r, g, b);
                        }
                    }
                }
            }
        }

        if (count > 0 && maxSat > 0.3)
        {
            return mostVibrant;
        }

        if (count > 0)
        {
            return Color.FromRgb((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count));
        }

        return Color.FromRgb(0x4C, 0xC2, 0xFF);
    }

    public static BitmapSource ApplyColorPalette(BitmapSource source, string paletteName)
    {
        if (string.Equals(paletteName, "Original", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        double? targetHue = paletteName switch
        {
            "Neon Cyan" => 185.0,
            "Cyber Gold" => 45.0,
            "Rose Pink" => 335.0,
            "Royal Violet" => 275.0,
            "Emerald Mint" => 145.0,
            "Midnight Stealth" => null,
            _ => null
        };

        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];

            if (a == 0) continue;

            RgbToHsv(r, g, b, out double h, out double s, out double v);

            if (targetHue.HasValue)
            {
                // Shift hue while preserving vibrancy and lighting
                double newSat = Math.Max(s, 0.65);
                HsvToRgb(targetHue.Value, newSat, v, out byte nr, out byte ng, out byte nb);
                pixels[i] = nb;
                pixels[i + 1] = ng;
                pixels[i + 2] = nr;
            }
            else
            {
                // Stealth: Desaturate with high-contrast tone
                byte gray = (byte)((0.299 * r) + (0.587 * g) + (0.114 * b));
                pixels[i] = gray;
                pixels[i + 1] = gray;
                pixels[i + 2] = gray;
            }
        }

        var writeable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        writeable.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        writeable.Freeze();
        return writeable;
    }

    public static Dictionary<string, SmartRoleResult> GenerateSmartRoleBundle(
        BitmapSource baseImage,
        int baseHotspotX,
        int baseHotspotY,
        Color accentColor,
        string paletteName = "Original")
    {
        var tintedBase = ApplyColorPalette(baseImage, paletteName);
        int size = tintedBase.PixelWidth;
        int center = size / 2;

        Color paletteAccent = paletteName switch
        {
            "Neon Cyan" => Color.FromRgb(0x00, 0xE5, 0xFF),
            "Cyber Gold" => Color.FromRgb(0xFF, 0xD6, 0x00),
            "Rose Pink" => Color.FromRgb(0xFF, 0x40, 0x81),
            "Royal Violet" => Color.FromRgb(0x7C, 0x4D, 0xFF),
            "Emerald Mint" => Color.FromRgb(0x00, 0xE6, 0x76),
            "Midnight Stealth" => Color.FromRgb(0xDD, 0xDD, 0xDD),
            _ => accentColor
        };

        var results = new Dictionary<string, SmartRoleResult>();

        // 1. Arrow (Normal Select)
        AddRole(results, "Arrow", "Normal Select", tintedBase, baseHotspotX, baseHotspotY);

        // 2. Hand (Link Select) - Base artwork with pointer gesture / link sparkle badge
        var handBmp = CreateBadgeCursor(tintedBase, size, paletteAccent, BadgeType.LinkSparkle);
        AddRole(results, "Hand", "Link Select", handBmp, baseHotspotX, baseHotspotY);

        // 3. IBeam (Text Select) - High definition I-beam in accent color with contrast border
        var ibeamBmp = CreateIBeamCursor(size, paletteAccent);
        AddRole(results, "IBeam", "Text Select", ibeamBmp, center, center);

        // 4. Wait (Busy) - Futuristic glowing spinner ring
        var waitBmp = CreateSpinnerCursor(size, paletteAccent);
        AddRole(results, "Wait", "Busy", waitBmp, center, center);

        // 5. AppStarting (Working in Background) - Base cursor + mini spinner badge
        var appStartingBmp = CreateBadgeCursor(tintedBase, size, paletteAccent, BadgeType.Spinner);
        AddRole(results, "AppStarting", "Working in Background", appStartingBmp, baseHotspotX, baseHotspotY);

        // 6. Crosshair (Precision Select)
        var crossBmp = CreateCrosshairCursor(size, paletteAccent);
        AddRole(results, "Crosshair", "Precision Select", crossBmp, center, center);

        // 7. Help (Help Select) - Base cursor + ? badge
        var helpBmp = CreateBadgeCursor(tintedBase, size, paletteAccent, BadgeType.Help);
        AddRole(results, "Help", "Help Select", helpBmp, baseHotspotX, baseHotspotY);

        // 8. No (Unavailable) - Base cursor + prohibition badge
        var noBmp = CreateBadgeCursor(tintedBase, size, Color.FromRgb(0xFF, 0x33, 0x44), BadgeType.Prohibition);
        AddRole(results, "No", "Unavailable", noBmp, baseHotspotX, baseHotspotY);

        // 9. SizeNS (Vertical Resize)
        var nsBmp = CreateResizeCursor(size, paletteAccent, ResizeDirection.Vertical);
        AddRole(results, "SizeNS", "Vertical Resize", nsBmp, center, center);

        // 10. SizeWE (Horizontal Resize)
        var weBmp = CreateResizeCursor(size, paletteAccent, ResizeDirection.Horizontal);
        AddRole(results, "SizeWE", "Horizontal Resize", weBmp, center, center);

        // 11. SizeNWSE (Diagonal Resize 1)
        var nwseBmp = CreateResizeCursor(size, paletteAccent, ResizeDirection.DiagonalNWSE);
        AddRole(results, "SizeNWSE", "Diagonal Resize 1", nwseBmp, center, center);

        // 12. SizeNESW (Diagonal Resize 2)
        var neswBmp = CreateResizeCursor(size, paletteAccent, ResizeDirection.DiagonalNESW);
        AddRole(results, "SizeNESW", "Diagonal Resize 2", neswBmp, center, center);

        // 13. SizeAll (Move)
        var moveBmp = CreateMoveCursor(size, paletteAccent);
        AddRole(results, "SizeAll", "Move", moveBmp, center, center);

        // 14. UpArrow (Alternate Select)
        var upBmp = CreateUpArrowCursor(size, paletteAccent);
        AddRole(results, "UpArrow", "Alternate Select", upBmp, center, Math.Max(0, (int)(size * 0.1)));

        // 15. NWPen (Handwriting)
        var penBmp = CreatePenCursor(size, paletteAccent);
        AddRole(results, "NWPen", "Handwriting", penBmp, (int)(size * 0.12), (int)(size * 0.12));

        return results;
    }

    private static void AddRole(
        Dictionary<string, SmartRoleResult> dict,
        string roleId,
        string displayName,
        BitmapSource bmp,
        int hx,
        int hy)
    {
        byte[] png = EncodeToPngBytes(bmp);
        dict[roleId] = new SmartRoleResult
        {
            RoleId = roleId,
            DisplayName = displayName,
            Bitmap = bmp,
            HotspotX = Math.Clamp(hx, 0, bmp.PixelWidth - 1),
            HotspotY = Math.Clamp(hy, 0, bmp.PixelHeight - 1),
            PngBytes = png
        };
    }

    public static byte[] EncodeToPngBytes(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private enum BadgeType { LinkSparkle, Spinner, Help, Prohibition }
    private enum ResizeDirection { Vertical, Horizontal, DiagonalNWSE, DiagonalNESW }

    private static BitmapSource CreateBadgeCursor(BitmapSource baseBmp, int size, Color color, BadgeType type)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Draw base image slightly scaled if needed to give badge breathing room
            dc.DrawImage(baseBmp, new Rect(0, 0, size, size));

            double badgeX = size * 0.62;
            double badgeY = size * 0.62;
            double badgeSize = size * 0.32;
            var center = new Point(badgeX + (badgeSize / 2), badgeY + (badgeSize / 2));
            double radius = badgeSize / 2;

            var fillBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0x18, 0x1A, 0x22));
            var accentBrush = new SolidColorBrush(color);
            var borderPen = new Pen(accentBrush, Math.Max(2.0, size * 0.015));
            borderPen.Freeze();
            fillBrush.Freeze();
            accentBrush.Freeze();

            switch (type)
            {
                case BadgeType.LinkSparkle:
                    // Star sparkle badge in upper right
                    double sx = size * 0.68;
                    double sy = size * 0.12;
                    double sr = size * 0.18;
                    var starPath = new StreamGeometry();
                    using (var sc = starPath.Open())
                    {
                        sc.BeginFigure(new Point(sx, sy - sr), isFilled: true, isClosed: true);
                        sc.QuadraticBezierTo(new Point(sx, sy), new Point(sx + sr, sy), true, false);
                        sc.QuadraticBezierTo(new Point(sx, sy), new Point(sx, sy + sr), true, false);
                        sc.QuadraticBezierTo(new Point(sx, sy), new Point(sx - sr, sy), true, false);
                        sc.QuadraticBezierTo(new Point(sx, sy), new Point(sx, sy - sr), true, false);
                    }
                    starPath.Freeze();
                    dc.DrawGeometry(accentBrush, new Pen(Brushes.White, 2), starPath);
                    break;

                case BadgeType.Spinner:
                    dc.DrawEllipse(fillBrush, borderPen, center, radius, radius);
                    var arcPen = new Pen(accentBrush, Math.Max(3.0, size * 0.025));
                    arcPen.Freeze();
                    dc.DrawEllipse(null, arcPen, center, radius * 0.65, radius * 0.65);
                    break;

                case BadgeType.Help:
                    dc.DrawEllipse(fillBrush, borderPen, center, radius, radius);
                    var textFormatted = new FormattedText(
                        "?",
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        badgeSize * 0.68,
                        accentBrush,
                        96.0);
                    dc.DrawText(textFormatted, new Point(center.X - (textFormatted.Width / 2), center.Y - (textFormatted.Height / 2)));
                    break;

                case BadgeType.Prohibition:
                    var redBrush = new SolidColorBrush(color);
                    var redPen = new Pen(redBrush, Math.Max(3.0, size * 0.025));
                    redBrush.Freeze();
                    redPen.Freeze();
                    dc.DrawEllipse(fillBrush, redPen, center, radius, radius);
                    // 45 degree slash
                    double offset = radius * 0.707;
                    dc.DrawLine(redPen, new Point(center.X - offset, center.Y - offset), new Point(center.X + offset, center.Y + offset));
                    break;
            }
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreateIBeamCursor(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double center = size / 2.0;
            double beamWidth = size * 0.38;
            double stemHeight = size * 0.64;
            double top = center - (stemHeight / 2.0);
            double bottom = center + (stemHeight / 2.0);
            double strokeWidth = Math.Max(4.0, size * 0.035);

            var outlinePen = new Pen(Brushes.Black, strokeWidth + 4);
            var colorPen = new Pen(new SolidColorBrush(color), strokeWidth);
            outlinePen.Freeze();
            colorPen.Freeze();

            // Outline for maximum contrast against any document
            dc.DrawLine(outlinePen, new Point(center - (beamWidth / 2), top), new Point(center + (beamWidth / 2), top));
            dc.DrawLine(outlinePen, new Point(center - (beamWidth / 2), bottom), new Point(center + (beamWidth / 2), bottom));
            dc.DrawLine(outlinePen, new Point(center, top), new Point(center, bottom));

            // Core beam
            dc.DrawLine(colorPen, new Point(center - (beamWidth / 2), top), new Point(center + (beamWidth / 2), top));
            dc.DrawLine(colorPen, new Point(center - (beamWidth / 2), bottom), new Point(center + (beamWidth / 2), bottom));
            dc.DrawLine(colorPen, new Point(center, top), new Point(center, bottom));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreateSpinnerCursor(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var center = new Point(size / 2.0, size / 2.0);
            double radius = size * 0.32;
            double strokeWidth = Math.Max(5.0, size * 0.04);

            var outlinePen = new Pen(Brushes.Black, strokeWidth + 3);
            var colorPen = new Pen(new SolidColorBrush(color), strokeWidth);
            outlinePen.Freeze();
            colorPen.Freeze();

            dc.DrawEllipse(null, outlinePen, center, radius, radius);
            dc.DrawEllipse(null, colorPen, center, radius, radius);

            // Inner glowing dot
            var innerBrush = new SolidColorBrush(color);
            innerBrush.Freeze();
            dc.DrawEllipse(innerBrush, new Pen(Brushes.White, 2), center, radius * 0.3, radius * 0.3);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreateCrosshairCursor(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double center = size / 2.0;
            double radius = size * 0.28;
            double strokeWidth = Math.Max(3.0, size * 0.025);

            var outlinePen = new Pen(Brushes.Black, strokeWidth + 3);
            var colorPen = new Pen(new SolidColorBrush(color), strokeWidth);
            outlinePen.Freeze();
            colorPen.Freeze();

            // Circle reticle
            dc.DrawEllipse(null, outlinePen, new Point(center, center), radius, radius);
            dc.DrawEllipse(null, colorPen, new Point(center, center), radius, radius);

            // 4 Reticle cross lines
            double outerSpan = size * 0.42;
            double innerSpan = size * 0.08;

            dc.DrawLine(outlinePen, new Point(center, center - outerSpan), new Point(center, center - innerSpan));
            dc.DrawLine(outlinePen, new Point(center, center + innerSpan), new Point(center, center + outerSpan));
            dc.DrawLine(outlinePen, new Point(center - outerSpan, center), new Point(center - innerSpan, center));
            dc.DrawLine(outlinePen, new Point(center + innerSpan, center), new Point(center + outerSpan, center));

            dc.DrawLine(colorPen, new Point(center, center - outerSpan), new Point(center, center - innerSpan));
            dc.DrawLine(colorPen, new Point(center, center + innerSpan), new Point(center, center + outerSpan));
            dc.DrawLine(colorPen, new Point(center - outerSpan, center), new Point(center - innerSpan, center));
            dc.DrawLine(colorPen, new Point(center + innerSpan, center), new Point(center + outerSpan, center));

            // Precision center dot
            var dotBrush = new SolidColorBrush(color);
            dotBrush.Freeze();
            dc.DrawEllipse(dotBrush, new Pen(Brushes.White, 1.5), new Point(center, center), 3.5, 3.5);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreateResizeCursor(int size, Color color, ResizeDirection direction)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double center = size / 2.0;
            double span = size * 0.36;
            double arrowSize = size * 0.12;
            double strokeWidth = Math.Max(4.0, size * 0.03);

            var outlinePen = new Pen(Brushes.Black, strokeWidth + 4);
            var colorPen = new Pen(new SolidColorBrush(color), strokeWidth);
            var colorBrush = new SolidColorBrush(color);
            outlinePen.Freeze();
            colorPen.Freeze();
            colorBrush.Freeze();

            switch (direction)
            {
                case ResizeDirection.Vertical:
                    dc.DrawLine(outlinePen, new Point(center, center - span), new Point(center, center + span));
                    dc.DrawLine(colorPen, new Point(center, center - span), new Point(center, center + span));
                    DrawArrowHead(dc, new Point(center, center - span), arrowSize, 0, colorBrush, outlinePen);
                    DrawArrowHead(dc, new Point(center, center + span), arrowSize, 180, colorBrush, outlinePen);
                    break;

                case ResizeDirection.Horizontal:
                    dc.DrawLine(outlinePen, new Point(center - span, center), new Point(center + span, center));
                    dc.DrawLine(colorPen, new Point(center - span, center), new Point(center + span, center));
                    DrawArrowHead(dc, new Point(center - span, center), arrowSize, 270, colorBrush, outlinePen);
                    DrawArrowHead(dc, new Point(center + span, center), arrowSize, 90, colorBrush, outlinePen);
                    break;

                case ResizeDirection.DiagonalNWSE:
                    dc.DrawLine(outlinePen, new Point(center - span, center - span), new Point(center + span, center + span));
                    dc.DrawLine(colorPen, new Point(center - span, center - span), new Point(center + span, center + span));
                    DrawArrowHead(dc, new Point(center - span, center - span), arrowSize, 315, colorBrush, outlinePen);
                    DrawArrowHead(dc, new Point(center + span, center + span), arrowSize, 135, colorBrush, outlinePen);
                    break;

                case ResizeDirection.DiagonalNESW:
                    dc.DrawLine(outlinePen, new Point(center + span, center - span), new Point(center - span, center + span));
                    dc.DrawLine(colorPen, new Point(center + span, center - span), new Point(center - span, center + span));
                    DrawArrowHead(dc, new Point(center + span, center - span), arrowSize, 45, colorBrush, outlinePen);
                    DrawArrowHead(dc, new Point(center - span, center + span), arrowSize, 225, colorBrush, outlinePen);
                    break;
            }
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreateMoveCursor(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double center = size / 2.0;
            double span = size * 0.34;
            double arrowSize = size * 0.10;
            double strokeWidth = Math.Max(3.0, size * 0.025);

            var outlinePen = new Pen(Brushes.Black, strokeWidth + 3);
            var colorPen = new Pen(new SolidColorBrush(color), strokeWidth);
            var colorBrush = new SolidColorBrush(color);
            outlinePen.Freeze();
            colorPen.Freeze();
            colorBrush.Freeze();

            dc.DrawLine(outlinePen, new Point(center, center - span), new Point(center, center + span));
            dc.DrawLine(outlinePen, new Point(center - span, center), new Point(center + span, center));
            dc.DrawLine(colorPen, new Point(center, center - span), new Point(center, center + span));
            dc.DrawLine(colorPen, new Point(center - span, center), new Point(center + span, center));

            DrawArrowHead(dc, new Point(center, center - span), arrowSize, 0, colorBrush, outlinePen);
            DrawArrowHead(dc, new Point(center, center + span), arrowSize, 180, colorBrush, outlinePen);
            DrawArrowHead(dc, new Point(center - span, center), arrowSize, 270, colorBrush, outlinePen);
            DrawArrowHead(dc, new Point(center + span, center), arrowSize, 90, colorBrush, outlinePen);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreateUpArrowCursor(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double center = size / 2.0;
            double top = size * 0.15;
            double bottom = size * 0.85;
            double arrowSize = size * 0.22;
            double strokeWidth = Math.Max(4.0, size * 0.03);

            var outlinePen = new Pen(Brushes.Black, strokeWidth + 4);
            var colorPen = new Pen(new SolidColorBrush(color), strokeWidth);
            var colorBrush = new SolidColorBrush(color);
            outlinePen.Freeze();
            colorPen.Freeze();
            colorBrush.Freeze();

            dc.DrawLine(outlinePen, new Point(center, top), new Point(center, bottom));
            dc.DrawLine(colorPen, new Point(center, top), new Point(center, bottom));
            DrawArrowHead(dc, new Point(center, top), arrowSize, 0, colorBrush, outlinePen);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CreatePenCursor(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double tipX = size * 0.15;
            double tipY = size * 0.15;
            double length = size * 0.55;
            double angleRad = 45.0 * Math.PI / 180.0;
            double endX = tipX + (length * Math.Cos(angleRad));
            double endY = tipY + (length * Math.Sin(angleRad));

            var outlinePen = new Pen(Brushes.Black, Math.Max(5.0, size * 0.04) + 3);
            var colorPen = new Pen(new SolidColorBrush(color), Math.Max(5.0, size * 0.04));
            outlinePen.Freeze();
            colorPen.Freeze();

            dc.DrawLine(outlinePen, new Point(tipX, tipY), new Point(endX, endY));
            dc.DrawLine(colorPen, new Point(tipX, tipY), new Point(endX, endY));

            // Nib tip
            var tipBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x00));
            tipBrush.Freeze();
            dc.DrawEllipse(tipBrush, new Pen(Brushes.Black, 2), new Point(tipX, tipY), 4, 4);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static void DrawArrowHead(DrawingContext dc, Point tip, double size, double angleDeg, Brush brush, Pen outlinePen)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double sideAngle1 = (angleDeg + 145.0) * Math.PI / 180.0;
        double sideAngle2 = (angleDeg - 145.0) * Math.PI / 180.0;

        var p1 = new Point(tip.X + (size * Math.Sin(sideAngle1)), tip.Y - (size * Math.Cos(sideAngle1)));
        var p2 = new Point(tip.X + (size * Math.Sin(sideAngle2)), tip.Y - (size * Math.Cos(sideAngle2)));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(tip, isFilled: true, isClosed: true);
            ctx.LineTo(p1, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(p2, isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();

        dc.DrawGeometry(brush, outlinePen, geo);
    }

    private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == rd)
        {
            h = 60.0 * (((gd - bd) / delta) % 6.0);
        }
        else if (max == gd)
        {
            h = 60.0 * (((bd - rd) / delta) + 2.0);
        }
        else
        {
            h = 60.0 * (((rd - gd) / delta) + 4.0);
        }

        if (h < 0) h += 360.0;
    }

    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        double c = v * s;
        double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = v - c;

        double rd = 0, gd = 0, bd = 0;

        if (h >= 0 && h < 60)
        {
            rd = c; gd = x; bd = 0;
        }
        else if (h >= 60 && h < 120)
        {
            rd = x; gd = c; bd = 0;
        }
        else if (h >= 120 && h < 180)
        {
            rd = 0; gd = c; bd = x;
        }
        else if (h >= 180 && h < 240)
        {
            rd = 0; gd = x; bd = c;
        }
        else if (h >= 240 && h < 300)
        {
            rd = x; gd = 0; bd = c;
        }
        else
        {
            rd = c; gd = 0; bd = x;
        }

        r = (byte)Math.Clamp(Math.Round((rd + m) * 255.0), 0, 255);
        g = (byte)Math.Clamp(Math.Round((gd + m) * 255.0), 0, 255);
        b = (byte)Math.Clamp(Math.Round((bd + m) * 255.0), 0, 255);
    }
}
