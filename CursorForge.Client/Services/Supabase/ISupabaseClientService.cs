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
}
