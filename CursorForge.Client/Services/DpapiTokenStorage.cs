using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CursorForge.Client.Services;

public interface IDpapiTokenStorage
{
    void SaveSession(string sessionJson);
    string? LoadSession();
    void ClearSession();
    bool HasSession();
}

public sealed class DpapiTokenStorage : IDpapiTokenStorage
{
    private readonly string _sessionFilePath;
    private readonly IStructuredLogger? _logger;

    public DpapiTokenStorage(ILocalStorageService storageService, IStructuredLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        _sessionFilePath = Path.Combine(storageService.StateDirectory, "session.dat");
        _logger = logger;
    }

    // Constructor for testing with arbitrary path
    public DpapiTokenStorage(string customFilePath, IStructuredLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customFilePath);
        _sessionFilePath = customFilePath;
        _logger = logger;
    }

    public void SaveSession(string sessionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionJson);

        string? dir = Path.GetDirectoryName(_sessionFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(sessionJson);
        byte[] encryptedBytes = ProtectedData.Protect(
            plainBytes,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        // Atomic write via temp file
        string tempPath = _sessionFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(tempPath, encryptedBytes);
        File.Move(tempPath, _sessionFilePath, overwrite: true);
    }

    public string? LoadSession()
    {
        if (!File.Exists(_sessionFilePath))
        {
            return null;
        }

        try
        {
            byte[] encryptedBytes = File.ReadAllBytes(_sessionFilePath);
            if (encryptedBytes.Length == 0)
            {
                ClearSession();
                return null;
            }

            byte[] plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            _logger?.LogWarning("auth.dpapi_failed", "DPAPI session decryption failed or corrupted. Clearing stored session.", ex);
            ClearSession();
            return null;
        }
    }

    public void ClearSession()
    {
        try
        {
            if (File.Exists(_sessionFilePath))
            {
                File.Delete(_sessionFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("auth.dpapi_delete_failed", "Failed to delete session file.", ex);
        }
    }

    public bool HasSession()
    {
        return File.Exists(_sessionFilePath);
    }
}
