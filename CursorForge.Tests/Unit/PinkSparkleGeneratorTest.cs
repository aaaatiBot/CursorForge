using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorForge.Tests.Unit;

public class PinkSparkleGeneratorTest
{
    [Fact]
    public void GeneratePinkSparkleCursorPng()
    {
        int size = 128;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            // Background is completely transparent

            // 1. Drop shadow for the arrow (semi-transparent dark pink/black)
            var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 40, 0, 30)), 4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var shadowBrush = new SolidColorBrush(Color.FromArgb(120, 50, 10, 40));

            var shadowGeo = CreateArrowGeometry(new Point(12, 12));
            dc.DrawGeometry(shadowBrush, shadowPen, shadowGeo);

            // 2. Main Pink Arrow
            // Gradient: Soft pastel pink at tip down to vibrant rose pink
            var arrowGradient = new LinearGradientBrush(
                Color.FromRgb(255, 182, 193), // LightPink
                Color.FromRgb(255, 20, 147),  // DeepPink
                new Point(0, 0),
                new Point(1, 1));

            var borderPen = new Pen(Brushes.White, 3.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            var arrowGeo = CreateArrowGeometry(new Point(8, 8));
            dc.DrawGeometry(arrowGradient, borderPen, arrowGeo);

            // Inner highlight line for shiny 3D gloss effect
            var glossPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 2);
            dc.DrawLine(glossPen, new Point(14, 16), new Point(14, 76));

            // 3. Magical Sparkles (4-point stars)
            // Big primary sparkle at top-right
            DrawSparkle(dc, new Point(88, 26), 16, Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(255, 105, 180)), 1.5));
            // Golden accent star inside primary sparkle
            DrawSparkle(dc, new Point(88, 26), 7, new SolidColorBrush(Color.FromRgb(255, 250, 180)), null);

            // Medium sparkle at right
            DrawSparkle(dc, new Point(98, 70), 12, new SolidColorBrush(Color.FromRgb(255, 215, 240)), new Pen(new SolidColorBrush(Color.FromRgb(255, 20, 147)), 1.2));

            // Little sparkle near pointer body
            DrawSparkle(dc, new Point(58, 28), 9, Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(255, 182, 193)), 1));

            // Floating trail sparkles
            DrawSparkle(dc, new Point(82, 104), 10, new SolidColorBrush(Color.FromRgb(255, 240, 250)), new Pen(new SolidColorBrush(Color.FromRgb(255, 105, 180)), 1));
            DrawSparkle(dc, new Point(28, 108), 8, Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(255, 182, 193)), 1));

            // Tiny dot glitter particles
            dc.DrawEllipse(Brushes.White, null, new Point(106, 42), 2.5, 2.5);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 220, 240)), null, new Point(74, 12), 2, 2);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 240, 200)), null, new Point(112, 88), 2, 2);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        // Save to multiple convenient destinations:
        string localAssetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pink_sparkle_cursor.png");
        string repoPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CursorForge.Client", "Assets", "pink_sparkle_cursor.png"));
        string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pink_sparkle_cursor.png");

        using (var fs = File.Create(localAssetPath)) encoder.Save(fs);

        if (Directory.Exists(Path.GetDirectoryName(repoPath)!))
        {
            File.Copy(localAssetPath, repoPath, overwrite: true);
        }

        try
        {
            File.Copy(localAssetPath, desktopPath, overwrite: true);
        }
        catch { }

        Assert.True(File.Exists(localAssetPath));
    }

    private static StreamGeometry CreateArrowGeometry(Point origin)
    {
        double ox = origin.X;
        double oy = origin.Y;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(ox, oy), true, true);
            ctx.LineTo(new Point(ox, oy + 76), true, false);
            ctx.LineTo(new Point(ox + 22, oy + 54), true, false);
            ctx.LineTo(new Point(ox + 42, oy + 86), true, false);
            ctx.LineTo(new Point(ox + 56, oy + 76), true, false);
            ctx.LineTo(new Point(ox + 36, oy + 46), true, false);
            ctx.LineTo(new Point(ox + 64, oy + 46), true, false);
        }
        geo.Freeze();
        return geo;
    }

    private static void DrawSparkle(DrawingContext dc, Point center, double size, Brush fill, Pen? stroke)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double inner = size * 0.28;
            ctx.BeginFigure(new Point(center.X, center.Y - size), true, true);
            ctx.LineTo(new Point(center.X + inner, center.Y - inner), true, false);
            ctx.LineTo(new Point(center.X + size, center.Y), true, false);
            ctx.LineTo(new Point(center.X + inner, center.Y + inner), true, false);
            ctx.LineTo(new Point(center.X, center.Y + size), true, false);
            ctx.LineTo(new Point(center.X - inner, center.Y + inner), true, false);
            ctx.LineTo(new Point(center.X - size, center.Y), true, false);
            ctx.LineTo(new Point(center.X - inner, center.Y - inner), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, stroke, geo);
    }
}
