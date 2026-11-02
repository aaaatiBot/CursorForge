using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorForge.Client.Services;

public interface ICursorConverterService
{
    byte[] ConvertPngToMultiSizeCur(byte[] pngBytes, int hotspotX, int hotspotY);
    void ConvertPngToMultiSizeCurFile(string pngFilePath, string outputCurFilePath, int hotspotX, int hotspotY);
    bool ValidateCurFile(string curFilePath);
}

public sealed class CursorConverterService : ICursorConverterService
{
    public static readonly int[] TargetSizes = [32, 48, 64, 96, 128];
    private const int BaseSize = 128;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadCursorFromFile(string lpFileName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(IntPtr hCursor);

    public void ConvertPngToMultiSizeCurFile(string pngFilePath, string outputCurFilePath, int hotspotX, int hotspotY)
    {
        byte[] pngBytes = File.ReadAllBytes(pngFilePath);
        byte[] curBytes = ConvertPngToMultiSizeCur(pngBytes, hotspotX, hotspotY);

        string? dir = Path.GetDirectoryName(outputCurFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(outputCurFilePath, curBytes);

        if (!ValidateCurFile(outputCurFilePath))
        {
            throw new InvalidOperationException($"Generated cursor file at '{outputCurFilePath}' failed Win32 LoadCursorFromFile validation.");
        }
    }

    public byte[] ConvertPngToMultiSizeCur(byte[] pngBytes, int hotspotX, int hotspotY)
    {
        if (pngBytes == null || pngBytes.Length < 8)
        {
            throw new ArgumentException("Invalid PNG byte stream.");
        }

        // Verify PNG magic bytes: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
        ReadOnlySpan<byte> magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!pngBytes.AsSpan(0, 8).SequenceEqual(magic))
        {
            throw new ArgumentException("Byte stream does not start with PNG magic header.");
        }

        BitmapSource sourceBitmap;
        using (var ms = new MemoryStream(pngBytes))
        {
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            sourceBitmap = decoder.Frames[0];
        }

        int srcWidth = sourceBitmap.PixelWidth;
        int srcHeight = sourceBitmap.PixelHeight;
        int baseDimension = Math.Max(srcWidth, srcHeight);
        if (baseDimension <= 0) baseDimension = BaseSize;

        // Render each target size into 32-bit BGRA bytes
        var resizedData = new List<(int Size, ushort HotspotX, ushort HotspotY, byte[] DIBPayload)>();

        foreach (int size in TargetSizes)
        {
            ushort scaledHx = (ushort)Math.Clamp((int)Math.Round(hotspotX * (size / (double)baseDimension)), 0, size - 1);
            ushort scaledHy = (ushort)Math.Clamp((int)Math.Round(hotspotY * (size / (double)baseDimension)), 0, size - 1);

            byte[] dib = BuildDIBPayload(sourceBitmap, size);
            resizedData.Add((size, scaledHx, scaledHy, dib));
        }

        using var outputStream = new MemoryStream();
        using var writer = new BinaryWriter(outputStream);

        // 1. ICONDIR (6 bytes)
        writer.Write((ushort)0); // idReserved = 0
        writer.Write((ushort)2); // idType = 2 (CUR)
        writer.Write((ushort)resizedData.Count); // idCount = 5

        // Calculate initial image offset: 6 bytes (header) + 16 bytes * count
        uint currentOffset = (uint)(6 + (16 * resizedData.Count));

        // 2. ICONDIRENTRY array (16 bytes per entry)
        foreach (var entry in resizedData)
        {
            writer.Write((byte)entry.Size); // bWidth (128 is valid as 128)
            writer.Write((byte)entry.Size); // bHeight
            writer.Write((byte)0);          // bColorCount = 0 for 32bpp
            writer.Write((byte)0);          // bReserved = 0
            writer.Write(entry.HotspotX);   // wXHotspot
            writer.Write(entry.HotspotY);   // wYHotspot
            writer.Write((uint)entry.DIBPayload.Length); // dwBytesInRes
            writer.Write(currentOffset);    // dwImageOffset

            currentOffset += (uint)entry.DIBPayload.Length;
        }

        // 3. DIB Payloads
        foreach (var entry in resizedData)
        {
            writer.Write(entry.DIBPayload);
        }

        writer.Flush();
        return outputStream.ToArray();
    }

    public bool ValidateCurFile(string curFilePath)
    {
        if (!File.Exists(curFilePath)) return false;

        IntPtr hCursor = LoadCursorFromFile(curFilePath);
        if (hCursor == IntPtr.Zero)
        {
            return false;
        }

        DestroyCursor(hCursor);
        return true;
    }

    private static byte[] BuildDIBPayload(BitmapSource source, int targetSize)
    {
        // 1. Render scaled in Premultiplied BGRA (Pbgra32)
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, targetSize, targetSize));
        }

        var rtb = new RenderTargetBitmap(targetSize, targetSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        int rowStride = targetSize * 4;
        byte[] pbgraPixels = new byte[rowStride * targetSize];
        rtb.CopyPixels(pbgraPixels, rowStride, 0);

        // 2. Unpremultiply to standard BGRA32
        byte[] bgraPixels = new byte[pbgraPixels.Length];
        for (int i = 0; i < pbgraPixels.Length; i += 4)
        {
            byte a = pbgraPixels[i + 3];
            if (a == 0)
            {
                bgraPixels[i] = 0;
                bgraPixels[i + 1] = 0;
                bgraPixels[i + 2] = 0;
                bgraPixels[i + 3] = 0;
            }
            else if (a == 255)
            {
                bgraPixels[i] = pbgraPixels[i];
                bgraPixels[i + 1] = pbgraPixels[i + 1];
                bgraPixels[i + 2] = pbgraPixels[i + 2];
                bgraPixels[i + 3] = 255;
            }
            else
            {
                bgraPixels[i] = (byte)Math.Clamp((int)Math.Round((pbgraPixels[i] * 255.0) / a), 0, 255);
                bgraPixels[i + 1] = (byte)Math.Clamp((int)Math.Round((pbgraPixels[i + 1] * 255.0) / a), 0, 255);
                bgraPixels[i + 2] = (byte)Math.Clamp((int)Math.Round((pbgraPixels[i + 2] * 255.0) / a), 0, 255);
                bgraPixels[i + 3] = a;
            }
        }

        // 3. AND mask calculation: 1 bit per pixel, row stride padded to 4 bytes (DWORD aligned)
        int andMaskRowStride = ((targetSize + 31) / 32) * 4;
        int andMaskTotalBytes = andMaskRowStride * targetSize;
        byte[] andMask = new byte[andMaskTotalBytes]; // all 0x00 for 32-bit alpha pass-through

        int dibHeaderSize = 40;
        int totalPayloadSize = dibHeaderSize + bgraPixels.Length + andMaskTotalBytes;

        using var ms = new MemoryStream(totalPayloadSize);
        using var writer = new BinaryWriter(ms);

        // BITMAPINFOHEADER (40 bytes)
        writer.Write((uint)40);                         // biSize
        writer.Write(targetSize);                       // biWidth
        writer.Write(targetSize * 2);                   // biHeight = targetSize * 2 (XOR + AND)
        writer.Write((ushort)1);                        // biPlanes
        writer.Write((ushort)32);                       // biBitCount = 32
        writer.Write((uint)0);                          // biCompression = BI_RGB
        writer.Write((uint)bgraPixels.Length);          // biSizeImage
        writer.Write(0);                                // biXPelsPerMeter
        writer.Write(0);                                // biYPelsPerMeter
        writer.Write((uint)0);                          // biClrUsed
        writer.Write((uint)0);                          // biClrImportant

        // Color Array (Bottom-Up 32-bit BGRA)
        for (int row = targetSize - 1; row >= 0; row--)
        {
            int offset = row * rowStride;
            writer.Write(bgraPixels, offset, rowStride);
        }

        // AND Mask (1-bit monochrome mask)
        writer.Write(andMask);

        writer.Flush();
        return ms.ToArray();
    }
}
