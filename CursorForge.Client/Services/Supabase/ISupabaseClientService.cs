using CursorForge.Client.Models;

namespace CursorForge.Client.Services.Supabase;

public interface ISupabaseClientService
{
    bool IsConfigured { get; }
    bool IsAuthenticated { get; }
    AuthUser? CurrentUser { get; }
    AuthSession? CurrentSession { get; }
    event Action<bool>? AuthStateChanged;

    Task InitializeAsync();
    Task<AuthResult> SignUpAsync(string emailOrUsername, string param2, string param3, bool termsAccepted);
    Task<AuthResult> SignUpAsync(string email, string username, string password, string? recoveryCode, bool termsAccepted);
    Task<AuthResult> SignInAsync(string usernameOrEmail, string password);
    Task SignOutAsync();
    Task<PasswordResetResult> ResetPasswordAsync(string email);
    Task<PasswordResetResult> ResetPasswordAsync(string username, string recoveryCode, string newPassword);

    // Phase 6 - Browse
    Task<PagedResult<CursorItem>> GetBrowsePageAsync(
        BrowseSort sort,
        string? search,
        DateTime? afterTs,
        Guid? afterId,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<bool> LikeCursorAsync(Guid cursorId);
    Task<bool> UnlikeCursorAsync(Guid cursorId);
    Task<bool> IncrementDownloadAsync(Guid cursorId);

    // Phase 7 - Upload & Creator Management
    Task<UploadSessionResult?> CreateUploadSessionAsync(string name, List<string> tags, List<string> roleNames);
    Task<bool> ValidateUploadSessionAsync(Guid sessionId);
    Task<Guid?> CompleteUploadSessionAsync(Guid sessionId, Dictionary<string, HotspotCoord> hotspots);
    Task<List<CursorItem>> GetMyCursorsAsync(CancellationToken ct = default);
    Task<bool> RequestCursorDeletionAsync(Guid cursorId);
}
