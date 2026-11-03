using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using CursorForge.Client.Models;
using Microsoft.Win32;

namespace CursorForge.Client.Services;

public interface ISystemCursorService
{
    CursorSchemeSnapshot CaptureCurrentSnapshot();
    void SaveOriginalBackupIfNotExists();
    void RestoreFromBackup();
    void ResetToWindowsDefaults();
    void ApplyTheme(LocalTheme theme);
    void CheckAndRecoverPendingApply();
    void Refresh();
}

public sealed class SystemCursorService : ISystemCursorService
{
    private const uint SPI_SETCURSORS = 0x0057;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    private readonly ILocalStorageService _storage;
    private readonly IStructuredLogger _logger;
    private readonly ICursorConverterService _converter;
    private readonly string _backupFilePath;
    private readonly string _stateMarkerPath;
    private readonly string _preApplySnapshotPath;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        IntPtr pvParam,
        uint fWinIni);

    public SystemCursorService(
        ILocalStorageService storage,
        IStructuredLogger logger,
        ICursorConverterService converter)
    {
        _storage = storage;
        _logger = logger;
        _converter = converter;

        _backupFilePath = Path.Combine(_storage.BackupDirectory, "original_cursors.json");
        _stateMarkerPath = Path.Combine(_storage.StateDirectory, "pending_apply.marker");
        _preApplySnapshotPath = Path.Combine(_storage.StateDirectory, "pre_apply_snapshot.json");

        SaveOriginalBackupIfNotExists();
    }

    public void Refresh()
    {
        bool success = SystemParametersInfo(
            SPI_SETCURSORS,
            0,
            IntPtr.Zero,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                _logger.LogError("system_cursors.refresh_failed", $"SystemParametersInfo failed with Win32 error {error}");
                throw new Win32Exception(error);
            }
            _logger.LogWarning("system_cursors.refresh_non_interactive", "SystemParametersInfo returned false with code 0 (session may be non-interactive).");
        }

        _logger.LogInformation("system_cursors.refreshed", "SystemParametersInfo(SPI_SETCURSORS) executed successfully");
    }

    public CursorSchemeSnapshot CaptureCurrentSnapshot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: false);
        if (key == null)
        {
            return new CursorSchemeSnapshot();
        }

        string? defaultScheme = key.GetValue(string.Empty) as string;
        int? baseSize = key.GetValue("CursorBaseSize") as int?;

        var rolesList = new List<RegistryValueSnapshot>();
        foreach (var def in CursorRoleDefinition.StandardRoles)
        {
            object? rawValue = key.GetValue(def.RegistryValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            bool exists = rawValue != null;
            RegistryValueKind kind = exists ? key.GetValueKind(def.RegistryValueName) : RegistryValueKind.String;

            rolesList.Add(new RegistryValueSnapshot
            {
                ValueName = def.RegistryValueName,
                Exists = exists,
                ValueKind = kind,
                RawData = rawValue?.ToString()
            });
        }

        return new CursorSchemeSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            DefaultSchemeName = defaultScheme,
            CursorBaseSize = baseSize,
            Roles = rolesList
        };
    }

    public void SaveOriginalBackupIfNotExists()
    {
        if (File.Exists(_backupFilePath)) return;

        try
        {
            var snapshot = CaptureCurrentSnapshot();
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            string temp = Path.Combine(_storage.BackupDirectory, $"backup.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temp, json);
            File.Move(temp, _backupFilePath, overwrite: true);
            _logger.LogInformation("backup.created", "Original cursor registry settings backed up successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("backup.failed", "Failed to create original cursor backup", ex);
        }
    }

    public void RestoreFromBackup()
    {
        if (!File.Exists(_backupFilePath))
        {
            throw new FileNotFoundException("No original backup found to restore from.");
        }

        string json = File.ReadAllText(_backupFilePath);
        var snapshot = JsonSerializer.Deserialize<CursorSchemeSnapshot>(json)
            ?? throw new InvalidOperationException("Failed to deserialize cursor backup snapshot.");

        RestoreFromSnapshot(snapshot);
        _logger.LogInformation("backup.restored", "System cursors restored from original backup");
    }

    public void RestoreFromSnapshot(CursorSchemeSnapshot snapshot)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: true)
            ?? throw new InvalidOperationException("Failed to open HKCU\\Control Panel\\Cursors for writing.");

        foreach (var role in snapshot.Roles)
        {
            if (!role.Exists)
            {
                key.DeleteValue(role.ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(role.ValueName, role.RawData ?? string.Empty, role.ValueKind);
            }
        }

        if (snapshot.DefaultSchemeName != null)
        {
            key.SetValue(string.Empty, snapshot.DefaultSchemeName, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(string.Empty, throwOnMissingValue: false);
        }

        if (snapshot.CursorBaseSize.HasValue)
        {
            key.SetValue("CursorBaseSize", snapshot.CursorBaseSize.Value, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue("CursorBaseSize", throwOnMissingValue: false);
        }

        Refresh();
    }

    public void ResetToWindowsDefaults()
    {
        // Dynamic Windows default resolution
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string cursorsDir = Path.Combine(winDir, "Cursors");

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: true)
            ?? throw new InvalidOperationException("Failed to open HKCU\\Control Panel\\Cursors for writing.");

        // Check if Windows Default scheme exists in Schemes
        using var schemesKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors\Schemes", writable: false);
        string? defaultSchemeString = schemesKey?.GetValue("Windows Default") as string;

        if (!string.IsNullOrEmpty(defaultSchemeString))
        {
            string[] paths = defaultSchemeString.Split(',');
            for (int i = 0; i < Math.Min(paths.Length, CursorRoleDefinition.StandardRoles.Count); i++)
            {
                var role = CursorRoleDefinition.StandardRoles[i];
                key.SetValue(role.RegistryValueName, paths[i], RegistryValueKind.ExpandString);
            }
        }
        else
        {
            // Fallback to aero default files in Windows\Cursors
            foreach (var role in CursorRoleDefinition.StandardRoles)
            {
                string filePath = Path.Combine(cursorsDir, role.DefaultFileName);
                if (File.Exists(filePath))
                {
                    key.SetValue(role.RegistryValueName, filePath, RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(role.RegistryValueName, throwOnMissingValue: false);
                }
            }
        }

        key.SetValue(string.Empty, "Windows Default", RegistryValueKind.String);
        key.DeleteValue("CursorBaseSize", throwOnMissingValue: false);

        Refresh();
        _logger.LogInformation("defaults.reset", "System cursors reset to Windows defaults");
    }

    public void ApplyTheme(LocalTheme theme)
    {
        // 1. Pre-validation: verify all defined cursor files exist and load via Win32
        foreach (var (roleId, item) in theme.Roles)
        {
            if (!File.Exists(item.FilePath))
            {
                throw new FileNotFoundException($"Cursor file for role '{roleId}' does not exist at '{item.FilePath}'.");
            }

            if (!_converter.ValidateCurFile(item.FilePath))
            {
                throw new InvalidOperationException($"Cursor file '{item.FilePath}' is not a valid Windows cursor.");
            }
        }

        // 2. Snapshot current state for rollback and crash recovery
        var preApplySnapshot = CaptureCurrentSnapshot();
        string snapshotJson = JsonSerializer.Serialize(preApplySnapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_preApplySnapshotPath, snapshotJson);

        // 3. Atomically write pending apply marker BEFORE registry writes
        File.WriteAllText(_stateMarkerPath, theme.Id);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: true)
                ?? throw new InvalidOperationException("Failed to open HKCU\\Control Panel\\Cursors for writing.");

            var roleMap = CursorRoleDefinition.StandardRoles.ToDictionary(r => r.RoleId, r => r.RegistryValueName);

            foreach (var (roleId, item) in theme.Roles)
            {
                if (roleMap.TryGetValue(roleId, out var regValueName))
                {
                    key.SetValue(regValueName, item.FilePath, RegistryValueKind.String);
                }
            }

            key.SetValue(string.Empty, theme.Name, RegistryValueKind.String);

            if (theme.CursorBaseSize.HasValue && theme.CursorBaseSize.Value > 0)
            {
                key.SetValue("CursorBaseSize", theme.CursorBaseSize.Value, RegistryValueKind.DWord);
            }

            // Register in HKCU\Control Panel\Cursors\Schemes
            RegisterScheme(theme);

            // 4. Broadcast system update
            Refresh();

            // 5. Success: clear state marker and snapshot
            if (File.Exists(_stateMarkerPath)) File.Delete(_stateMarkerPath);
            if (File.Exists(_preApplySnapshotPath)) File.Delete(_preApplySnapshotPath);

            _logger.LogInformation("theme.applied", $"Applied theme '{theme.Name}' ({theme.Id}) successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("theme.apply_failed", $"Failed to apply theme '{theme.Name}'. Rolling back...", ex);
            try
            {
                RestoreFromSnapshot(preApplySnapshot);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError("theme.rollback_failed", "Failed to rollback to pre-apply snapshot", rollbackEx);
            }
            finally
            {
                if (File.Exists(_stateMarkerPath)) File.Delete(_stateMarkerPath);
                if (File.Exists(_preApplySnapshotPath)) File.Delete(_preApplySnapshotPath);
            }
            throw;
        }
    }

    public void CheckAndRecoverPendingApply()
    {
        if (File.Exists(_stateMarkerPath) && File.Exists(_preApplySnapshotPath))
        {
            _logger.LogWarning("crash_recovery.detected", "Detected interrupted theme apply from previous session. Restoring pre-apply snapshot...");
            try
            {
                string json = File.ReadAllText(_preApplySnapshotPath);
                var snapshot = JsonSerializer.Deserialize<CursorSchemeSnapshot>(json);
                if (snapshot != null)
                {
                    RestoreFromSnapshot(snapshot);
                    _logger.LogInformation("crash_recovery.completed", "Crash recovery completed successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("crash_recovery.failed", "Crash recovery restoration encountered an error", ex);
            }
            finally
            {
                if (File.Exists(_stateMarkerPath)) File.Delete(_stateMarkerPath);
                if (File.Exists(_preApplySnapshotPath)) File.Delete(_preApplySnapshotPath);
            }
        }
    }

    private void RegisterScheme(LocalTheme theme)
    {
        try
        {
            using var schemesKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors\Schemes", writable: true);
            if (schemesKey == null) return;

            // Build comma-delimited string of 15 paths in exact Windows order
            var paths = new List<string>();
            foreach (var role in CursorRoleDefinition.StandardRoles)
            {
                if (theme.Roles.TryGetValue(role.RoleId, out var item))
                {
                    paths.Add(item.FilePath);
                }
                else
                {
                    paths.Add(string.Empty);
                }
            }

            string schemeString = string.Join(",", paths);
            schemesKey.SetValue(theme.Name, schemeString, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("theme.register_scheme_failed", "Failed to register theme in Cursors\\Schemes", ex);
        }
    }
}
