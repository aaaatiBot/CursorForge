using System.IO;

namespace CursorForge.Client.Services;

public readonly record struct PngValidationResult(bool IsValid, string? ErrorMessage = null, int Width = 0, int Height = 0);

public static class PngValidator
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    public const int MaxByteSize = 25 * 1024 * 1024; // 25 MB
    public const int MaxDimension = 4096; // Up to 4K resolution

    public static PngValidationResult ValidateFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new PngValidationResult(false, "File does not exist.");
        }

        var fi = new FileInfo(filePath);
        if (fi.Length > MaxByteSize)
        {
            return new PngValidationResult(false, $"File size ({fi.Length / (1024 * 1024)} MB) exceeds maximum 25 MB limit.");
        }
        if (fi.Length < 24)
        {
            return new PngValidationResult(false, "File is too small to be a valid PNG.");
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        return ValidateBytes(bytes);
    }

    public static PngValidationResult ValidateBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 24)
        {
            return new PngValidationResult(false, "File is too small to be a valid PNG.");
        }

        if (bytes.Length > MaxByteSize)
        {
            return new PngValidationResult(false, $"File size exceeds 25 MB limit.");
        }

        // Verify PNG magic header
        for (int i = 0; i < 8; i++)
        {
            if (bytes[i] != PngSignature[i])
            {
                return new PngValidationResult(false, "Invalid PNG signature header.");
            }
        }

        // Parse IHDR chunk
        // Chunk length at byte 8 (4 bytes), type at byte 12 (4 bytes: 'I','H','D','R')
        if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
        {
            return new PngValidationResult(false, "Missing or corrupt IHDR chunk in PNG.");
        }

        // Dimensions (big-endian 32-bit unsigned integers)
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

        if (width <= 0 || height <= 0)
        {
            return new PngValidationResult(false, "PNG dimensions must be greater than 0.");
        }

        if (width > MaxDimension || height > MaxDimension)
        {
            return new PngValidationResult(false, $"Dimensions ({width}x{height}) exceed maximum allowed {MaxDimension}x{MaxDimension}.");
        }

        return new PngValidationResult(true, null, width, height);
    }
}
