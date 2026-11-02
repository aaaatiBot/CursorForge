using CursorForge.Client.Services;
using FluentAssertions;
using System.IO;

namespace CursorForge.Tests.Unit;

public class StructuredLoggerTests : IDisposable
{
    private readonly string _testLogDir;

    public StructuredLoggerTests()
    {
        _testLogDir = Path.Combine(Path.GetTempPath(), $"CursorForgeLogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLogDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testLogDir))
            {
                Directory.Delete(_testLogDir, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup
        }
    }

    [Fact]
    public void Logger_WritesJsonLinesEntry_WithExpectedFields()
    {
        var logger = new StructuredLogger(_testLogDir);
        logger.LogInformation("test.event", "Test log message", new { item = 42 });

        string[] files = Directory.GetFiles(_testLogDir, "app-*.log");
        files.Should().HaveCount(1);

        string content = File.ReadAllText(files[0]);
        content.Should().Contain("\"event\":\"test.event\"");
        content.Should().Contain("\"message\":\"Test log message\"");
        content.Should().Contain("\"level\":\"Information\"");
        content.Should().Contain("\"item\":42");
    }

    [Fact]
    public void Logger_RedactsSensitiveData()
    {
        var logger = new StructuredLogger(_testLogDir);
        string sensitivePayload = "User said: \"password\":\"SuperSecret123!\" and \"token\":\"jwt.payload.signature\"";

        logger.LogWarning("security.test", sensitivePayload);

        string[] files = Directory.GetFiles(_testLogDir, "app-*.log");
        string content = File.ReadAllText(files[0]);

        content.Should().NotContain("SuperSecret123!");
        content.Should().NotContain("jwt.payload.signature");
        content.Should().Contain("[REDACTED]");
    }
}
