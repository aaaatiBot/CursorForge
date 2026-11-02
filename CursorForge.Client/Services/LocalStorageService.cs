using System.IO;
using System.Text.Json;
using CursorForge.Client.Models;

namespace CursorForge.Client.Services;

public interface ILocalStorageService
{
    string RootDirectory { get; }
    string BackupDirectory { get; }
    string CacheDirectory { get; }
    string LogsDirectory { get; }
    string StateDirectory { get; }
    string ThemesDirectory { get; }

    AppConfig LoadConfig();
    void SaveConfig(AppConfig config);
    void EnforceCacheLimit(int maxMb = 200);
    void CleanupStaleTempFiles();
}

public sealed class LocalStorageService : ILocalStorageService
{
    private static readonly string DefaultAppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CursorForge");

    private readonly string _rootDirectory;

    public string RootDirectory => _rootDirectory;
    public string BackupDirectory => Path.Combine(_rootDirectory, "backup");
    public string CacheDirectory => Path.Combine(_rootDirectory, "cache");
    public string LogsDirectory => Path.Combine(_rootDirectory, "logs");
    public string StateDirectory => Path.Combine(_rootDirectory, "state");
    public string ThemesDirectory => Path.Combine(_rootDirectory, "themes");

    private readonly string _configFilePath;

    public LocalStorageService(string? customRoot = null)
    {
        _rootDirectory = customRoot ?? DefaultAppDataRoot;
        InitializeStorage(_rootDirectory);
        _configFilePath = Path.Combine(_rootDirectory, "config.json");
    }

    public static void InitializeStorage(string? root = null)
    {
        string targetRoot = root ?? DefaultAppDataRoot;
        string[] directories =
        [
            targetRoot,
            Path.Combine(targetRoot, "backup"),
            Path.Combine(targetRoot, "cache"),
            Path.Combine(targetRoot, "logs"),
            Path.Combine(targetRoot, "state"),
            Path.Combine(targetRoot, "themes")
        ];

        foreach (var dir in directories)
        {
            Directory.CreateDirectory(dir);
        }
    }

    public AppConfig LoadConfig()
    {
        try
        {
            // First check %APPDATA%\CursorForge\config.json
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null) return config;
            }

            // Next check local application directory config.json
            string localConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(localConfig))
            {
                string json = File.ReadAllText(localConfig);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    SaveConfig(config);
                    return config;
                }
            }
        }
        catch
        {
            // Fall back to default
        }

        var defaultConfig = new AppConfig();
        SaveConfig(defaultConfig);
        return defaultConfig;
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            string tempFile = Path.Combine(_rootDirectory, $"config.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _configFilePath, overwrite: true);
        }
        catch
        {
            // Ignore config save errors
        }
    }

    public void EnforceCacheLimit(int maxMb = 200)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;

            long maxBytes = (long)maxMb * 1024 * 1024;
            var files = Directory.GetFiles(CacheDirectory)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            long totalBytes = files.Sum(f => f.Length);
            if (totalBytes > maxBytes)
            {
                long targetBytes = (long)(maxBytes * 0.80); // 20% hysteresis
                foreach (var file in files)
                {
                    try
                    {
                        file.Delete();
                        totalBytes -= file.Length;
                        if (totalBytes <= targetBytes) break;
                    }
                    catch
                    {
                        // Ignore individual file delete errors
                    }
                }
            }
        }
        catch
        {
            // Cache cleanup is non-critical
        }
    }

    public void CleanupStaleTempFiles()
    {
        try
        {
            string tempPath = Path.GetTempPath();
            var cursorTempDirs = Directory.GetDirectories(tempPath, "CursorForge-*");
            DateTime cutoff = DateTime.UtcNow.AddHours(-24);

            foreach (var dir in cursorTempDirs)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.CreationTimeUtc < cutoff)
                    {
                        dirInfo.Delete(recursive: true);
                    }
                }
                catch
                {
                    // Ignore directory cleanup error
                }
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }
}

