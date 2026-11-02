using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorForge.Tests.Unit;

public class CatCursorTransparencyTest
{
    private const string SourceImagePath = @"C:\Users\as629\.gemini\antigravity\brain\4bbe6276-562b-4fc5-954b-7f3495328470\.user_uploaded\media_1793635840120_5c47b2e2.png";

    [Fact]
    public void ConvertCatCursorToTransparentPng()
    {
        Assert.True(File.Exists(SourceImagePath), "Source image must exist");

        // Load source image
        var uri = new Uri(SourceImagePath, UriKind.Absolute);
        var sourceBitmap = new BitmapImage();
        sourceBitmap.BeginInit();
        sourceBitmap.UriSource = uri;
        sourceBitmap.CacheOption = BitmapCacheOption.OnLoad;
        sourceBitmap.EndInit();

        int width = sourceBitmap.PixelWidth;
        int height = sourceBitmap.PixelHeight;

        // Convert to standard BGRA 32-bit array
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        sourceBitmap.CopyPixels(pixels, stride, 0);

        // Perform multi-source BFS flood fill from outer border pixels
        bool[,] isBackground = new bool[width, height];
        var queue = new Queue<(int x, int y)>();

        // Enqueue all boundary pixels that are white/near-white
        for (int x = 0; x < width; x++)
        {
            TryEnqueue(x, 0, width, height, pixels, isBackground, queue);
            TryEnqueue(x, height - 1, width, height, pixels, isBackground, queue);
        }
        for (int y = 0; y < height; y++)
        {
            TryEnqueue(0, y, width, height, pixels, isBackground, queue);
            TryEnqueue(width - 1, y, width, height, pixels, isBackground, queue);
        }

        // BFS Flood fill
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();

            TryEnqueue(cx + 1, cy, width, height, pixels, isBackground, queue);
            TryEnqueue(cx - 1, cy, width, height, pixels, isBackground, queue);
            TryEnqueue(cx, cy + 1, width, height, pixels, isBackground, queue);
            TryEnqueue(cx, cy - 1, width, height, pixels, isBackground, queue);
        }

        // Create transparent pixel buffer
        byte[] transparentPixels = new byte[height * stride];
        Array.Copy(pixels, transparentPixels, pixels.Length);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;
                if (isBackground[x, y])
                {
                    // Completely transparent
                    transparentPixels[idx] = 0;     // B
                    transparentPixels[idx + 1] = 0; // G
                    transparentPixels[idx + 2] = 0; // R
                    transparentPixels[idx + 3] = 0; // Alpha = 0
                }
            }
        }

        // 1. Save full resolution transparent PNG
        var fullResBitmap = BitmapSource.Create(
            width, height,
            sourceBitmap.DpiX, sourceBitmap.DpiY,
            PixelFormats.Bgra32, null,
            transparentPixels, stride);

        string fullResOut = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cat_cursor_transparent.png");
        SaveBitmapToPng(fullResBitmap, fullResOut);

        // 2. Also create a CursorForge-ready 128x128 high-DPI cursor PNG
        // Scale to fit within 128x128 keeping aspect ratio, centered with pointer tip near (10, 10)
        int targetDim = 128;
        double scale = Math.Min((double)targetDim / width, (double)targetDim / height);
        int scaledW = (int)(width * scale);
        int scaledH = (int)(height * scale);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Center horizontally, align top-left for cursor feel
            int offsetX = (targetDim - scaledW) / 2;
            int offsetY = (targetDim - scaledH) / 2;
            dc.DrawImage(fullResBitmap, new Rect(offsetX, offsetY, scaledW, scaledH));
        }

        var rtb = new RenderTargetBitmap(targetDim, targetDim, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        string cursorForgeOut = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cat_cursor_128.png");
        SaveBitmapToPng(rtb, cursorForgeOut);

        // Copy both to Desktop and project folders
        string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        string onedriveDesktop = @"C:\Users\as629\OneDrive\Documents\Desktop";
        string projectAssets = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CursorForge.Client", "Assets"));

        CopySafely(fullResOut, Path.Combine(onedriveDesktop, "cat_cursor_transparent.png"));
        CopySafely(cursorForgeOut, Path.Combine(onedriveDesktop, "cat_cursor_128.png"));
        CopySafely(cursorForgeOut, Path.Combine(projectAssets, "cat_cursor_128.png"));
        CopySafely(fullResOut, Path.Combine(projectAssets, "cat_cursor_transparent.png"));

        // Copy to workspace root
        string workspaceRoot = @"C:\Users\as629\OneDrive\Documents\Desktop\cursor";
        CopySafely(fullResOut, Path.Combine(workspaceRoot, "cat_cursor_transparent.png"));
        CopySafely(cursorForgeOut, Path.Combine(workspaceRoot, "cat_cursor_128.png"));
    }

    private static void TryEnqueue(int x, int y, int w, int h, byte[] pixels, bool[,] isBackground, Queue<(int, int)> queue)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        if (isBackground[x, y]) return;

        int idx = y * w * 4 + x * 4;
        byte b = pixels[idx];
        byte g = pixels[idx + 1];
        byte r = pixels[idx + 2];

        // Is it white / near-white background?
        // Check if color is near-white (R, G, B > 230 and low saturation)
        if (r > 230 && g > 230 && b > 230)
        {
            isBackground[x, y] = true;
            queue.Enqueue((x, y));
        }
    }

    private static void SaveBitmapToPng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static void CopySafely(string source, string dest)
    {
        try
        {
            string? dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.Copy(source, dest, overwrite: true);
        }
        catch { }
    }
}
