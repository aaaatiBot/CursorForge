using System.IO;
using CursorForge.Client.Services;
using FluentAssertions;

namespace CursorForge.Tests.Unit;

public class WindowsCursorSmokeTests
{
    private readonly CursorConverterService _converter = new();

    [Fact]
    public void SystemCursors_StaticCurFiles_PassValidation()
    {
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string cursorsDir = Path.Combine(winDir, "Cursors");

        if (!Directory.Exists(cursorsDir))
        {
            // If running in environment without Windows Cursors directory, skip gracefully
            return;
        }

        string[] curFiles = Directory.GetFiles(cursorsDir, "*.cur");
        curFiles.Should().NotBeEmpty("Windows should have default .cur files installed in C:\\Windows\\Cursors");

        int validCount = 0;
        foreach (string curFile in curFiles.Take(10)) // Smoke test top 10 standard system cursors
        {
            // Strictly test static .cur, never .ani
            Path.GetExtension(curFile).Should().BeEquivalentTo(".cur");

            bool isValid = _converter.ValidateCurFile(curFile);
            if (isValid) validCount++;
        }

        validCount.Should().BeGreaterThan(0);
    }
}
