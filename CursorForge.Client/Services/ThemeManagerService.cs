using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CursorForge.Client.Models;

namespace CursorForge.Client.Services;

public interface IThemeManagerService
{
    IReadOnlyList<LocalTheme> LoadAllThemes();
    LocalTheme? GetTheme(string themeId);
    void InstallStarterThemesIfMissing();
    string ExportTheme(string themeId, string destinationZipPath);
    LocalTheme ImportTheme(string sourceZipPath);
    void DeleteTheme(string themeId);
}

public sealed class ThemeManagerService : IThemeManagerService
{
    private readonly ILocalStorageService _storage;
    private readonly ICursorConverterService _converter;
    private readonly IStructuredLogger _logger;

    public ThemeManagerService(
        ILocalStorageService storage,
        ICursorConverterService converter,
        IStructuredLogger logger)
    {
        _storage = storage;
        _converter = converter;
        _logger = logger;

        InstallStarterThemesIfMissing();
    }

    public void InstallStarterThemesIfMissing()
    {
        string starterDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "StarterThemes");
        if (!Directory.Exists(starterDir)) return;

        string[] themeFolders = ["Neon", "Slate", "Cyber"];

        foreach (var folder in themeFolders)
        {
            string themeSrcDir = Path.Combine(starterDir, folder);
            string themeJsonPath = Path.Combine(themeSrcDir, "theme.json");

            if (!File.Exists(themeJsonPath)) continue;

            try
            {
                string json = File.ReadAllText(themeJsonPath);
                using var doc = JsonDocument.Parse(json);
                string themeId = doc.RootElement.GetProperty("id").GetString() ?? folder.ToLowerInvariant();

                string destThemeDir = Path.Combine(_storage.ThemesDirectory, themeId);
                string destJsonPath = Path.Combine(destThemeDir, "theme.json");

                if (File.Exists(destJsonPath)) continue; // Already installed

                Directory.CreateDirectory(destThemeDir);

                // Copy preview
                string srcPreview = Path.Combine(themeSrcDir, "preview.png");
                string destPreview = Path.Combine(destThemeDir, "preview.png");
                if (File.Exists(srcPreview))
                {
                    File.Copy(srcPreview, destPreview, overwrite: true);
                }

                var installedRoles = new Dictionary<string, LocalCursorRoleItem>();
                var rolesElem = doc.RootElement.GetProperty("roles");

                foreach (var role in CursorRoleDefinition.StandardRoles)
                {
                    if (rolesElem.TryGetProperty(role.RoleId, out var roleObj))
                    {
                        string pngFileName = roleObj.GetProperty("file").GetString() ?? $"{role.RoleId}.png";
                        int hx = roleObj.GetProperty("hotspot_x").GetInt32();
                        int hy = roleObj.GetProperty("hotspot_y").GetInt32();

                        string srcPng = Path.Combine(themeSrcDir, pngFileName);
                        string destCur = Path.Combine(destThemeDir, $"{role.RoleId}.cur");

                        if (File.Exists(srcPng))
                        {
                            _converter.ConvertPngToMultiSizeCurFile(srcPng, destCur, hx, hy);
                            installedRoles[role.RoleId] = new LocalCursorRoleItem
                            {
                                FilePath = destCur,
                                HotspotX = hx,
                                HotspotY = hy
                            };
                        }
                    }
                }

                var localTheme = new LocalTheme
                {
                    Id = themeId,
                    Name = doc.RootElement.GetProperty("name").GetString() ?? folder,
                    Description = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null,
                    Author = doc.RootElement.TryGetProperty("author", out var a) ? a.GetString() : "CursorForge Official",
                    PreviewPath = destPreview,
                    Roles = installedRoles
                };

                string outJson = JsonSerializer.Serialize(localTheme, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(destJsonPath, outJson);

                _logger.LogInformation("starter_theme.installed", $"Installed bundled theme '{localTheme.Name}' ({themeId})");
            }
            catch (Exception ex)
            {
                _logger.LogError("starter_theme.install_failed", $"Failed to install starter theme '{folder}'", ex);
            }
        }
    }

    public IReadOnlyList<LocalTheme> LoadAllThemes()
    {
        var result = new List<LocalTheme>();
        if (!Directory.Exists(_storage.ThemesDirectory)) return result;

        var themeDirs = Directory.GetDirectories(_storage.ThemesDirectory);
        foreach (var dir in themeDirs)
        {
            string jsonPath = Path.Combine(dir, "theme.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var theme = JsonSerializer.Deserialize<LocalTheme>(json);
                    if (theme != null)
                    {
                        result.Add(theme);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("theme.load_failed", $"Failed to load theme from '{dir}'", ex);
                }
            }
        }

        return result;
    }

    public LocalTheme? GetTheme(string themeId)
    {
        string dir = Path.Combine(_storage.ThemesDirectory, themeId);
        string jsonPath = Path.Combine(dir, "theme.json");
        if (!File.Exists(jsonPath)) return null;

        try
        {
            string json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<LocalTheme>(json);
        }
        catch
        {
            return null;
        }
    }

    public string ExportTheme(string themeId, string destinationZipPath)
    {
        string themeDir = Path.Combine(_storage.ThemesDirectory, themeId);
        if (!Directory.Exists(themeDir))
        {
            throw new DirectoryNotFoundException($"Theme directory '{themeDir}' does not exist.");
        }

        string? destDir = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        if (File.Exists(destinationZipPath))
        {
            File.Delete(destinationZipPath);
        }

        ZipFile.CreateFromDirectory(themeDir, destinationZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        _logger.LogInformation("theme.exported", $"Exported theme '{themeId}' to '{destinationZipPath}'");
        return destinationZipPath;
    }

    public LocalTheme ImportTheme(string sourceZipPath)
    {
        if (!File.Exists(sourceZipPath))
        {
            throw new FileNotFoundException($"Import file not found at '{sourceZipPath}'.");
        }

        string tempExtract = Path.Combine(Path.GetTempPath(), $"CursorForgeImport_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtract);

        try
        {
            ZipFile.ExtractToDirectory(sourceZipPath, tempExtract);

            string jsonPath = Path.Combine(tempExtract, "theme.json");
            if (!File.Exists(jsonPath))
            {
                throw new InvalidOperationException("Invalid cursor theme package: missing theme.json.");
            }

            string json = File.ReadAllText(jsonPath);
            var parsedTheme = JsonSerializer.Deserialize<LocalTheme>(json)
                ?? throw new InvalidOperationException("Failed to parse theme.json.");

            string themeId = !string.IsNullOrWhiteSpace(parsedTheme.Id) ? parsedTheme.Id : Guid.NewGuid().ToString("N");
            string targetDir = Path.Combine(_storage.ThemesDirectory, themeId);

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.CreateDirectory(targetDir);

            // Copy all extracted files and remap file paths to the local themes directory
            var remappedRoles = new Dictionary<string, LocalCursorRoleItem>();
            foreach (var (roleId, item) in parsedTheme.Roles)
            {
                string originalFileName = Path.GetFileName(item.FilePath);
                string sourceFile = Path.Combine(tempExtract, originalFileName);
                string destFile = Path.Combine(targetDir, originalFileName);

                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, destFile, overwrite: true);
                    remappedRoles[roleId] = new LocalCursorRoleItem
                    {
                        FilePath = destFile,
                        HotspotX = item.HotspotX,
                        HotspotY = item.HotspotY
                    };
                }
            }

            string previewFile = Path.Combine(targetDir, "preview.png");
            string tempPreview = Path.Combine(tempExtract, "preview.png");
            if (File.Exists(tempPreview))
            {
                File.Copy(tempPreview, previewFile, overwrite: true);
            }

            parsedTheme.Id = themeId;
            parsedTheme.PreviewPath = previewFile;
            parsedTheme.Roles = remappedRoles;

            string updatedJson = JsonSerializer.Serialize(parsedTheme, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(targetDir, "theme.json"), updatedJson);

            _logger.LogInformation("theme.imported", $"Imported theme '{parsedTheme.Name}' ({themeId})");
            return parsedTheme;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, recursive: true);
            }
            catch { }
        }
    }

    public void DeleteTheme(string themeId)
    {
        string dir = Path.Combine(_storage.ThemesDirectory, themeId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            _logger.LogInformation("theme.deleted", $"Deleted local theme '{themeId}'");
        }
    }
}
