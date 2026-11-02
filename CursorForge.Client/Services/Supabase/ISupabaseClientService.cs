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
    Task<AuthResult> SignUpAsync(string username, string password, string recoveryCode, bool termsAccepted);
    Task<AuthResult> SignInAsync(string username, string password);
    Task SignOutAsync();
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
}
