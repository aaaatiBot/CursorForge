using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CursorForge.Client.Services;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public interface IStructuredLogger
{
    void Log(LogLevel level, string @event, string? message = null, string? userId = null, Exception? exception = null, object? context = null);
    void LogInformation(string @event, string? message = null, object? context = null);
    void LogWarning(string @event, string? message = null, Exception? exception = null, object? context = null);
    void LogError(string @event, string? message = null, Exception? exception = null, string? userId = null, object? context = null);
}

public sealed partial class StructuredLogger : IStructuredLogger
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private static readonly Regex SecretSanitizer = new(
        @"(?i)""(password|token|recovery_code|jwt|secret|key)""\s*:\s*""[^""]+""",
        RegexOptions.Compiled);

    public StructuredLogger(string? customLogDir = null)
    {
        _logDirectory = customLogDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CursorForge",
            "logs");

        Directory.CreateDirectory(_logDirectory);
        EnforceRetentionPolicy();
    }

    public void Log(LogLevel level, string @event, string? message = null, string? userId = null, Exception? exception = null, object? context = null)
    {
        var logEntry = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            level = level.ToString(),
            @event,
            message = Sanitize(message),
            userId,
            error = exception != null ? new
            {
                type = exception.GetType().FullName,
                message = Sanitize(exception.Message),
                stackTrace = exception.StackTrace
            } : null,
            ctx = context
        };

        string json = JsonSerializer.Serialize(logEntry);
        json = SecretSanitizer.Replace(json, @"""$1"":""[REDACTED]""");

        lock (_lock)
        {
            try
            {
                string logFilePath = Path.Combine(_logDirectory, $"app-{DateTime.UtcNow:yyyy-MM-dd}.log");
                File.AppendAllText(logFilePath, json + Environment.NewLine);
            }
            catch
            {
                // Never crash the host process due to logging failure
            }
        }
    }

    public void LogInformation(string @event, string? message = null, object? context = null) =>
        Log(LogLevel.Information, @event, message, null, null, context);

    public void LogWarning(string @event, string? message = null, Exception? exception = null, object? context = null) =>
        Log(LogLevel.Warning, @event, message, null, exception, context);

    public void LogError(string @event, string? message = null, Exception? exception = null, string? userId = null, object? context = null) =>
        Log(LogLevel.Error, @event, message, userId, exception, context);

    private static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return SecretSanitizer.Replace(text, @"""$1"":""[REDACTED]""");
    }

    private void EnforceRetentionPolicy()
    {
        try
        {
            var files = Directory.GetFiles(_logDirectory, "app-*.log")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();

            DateTime threshold = DateTime.UtcNow.AddDays(-7);
            foreach (var file in files)
            {
                if (file.CreationTimeUtc < threshold)
                {
                    file.Delete();
                }
            }

            // Also check 50 MB total limit
            long totalSize = files.Where(f => f.Exists).Sum(f => f.Length);
            long maxBytes = 50 * 1024 * 1024;
            if (totalSize > maxBytes)
            {
                foreach (var file in files.Where(f => f.Exists))
                {
                    file.Delete();
                    totalSize -= file.Length;
                    if (totalSize <= maxBytes * 0.8) break;
                }
            }
        }
        catch
        {
            // Ignore retention cleanup errors
        }
    }
}

