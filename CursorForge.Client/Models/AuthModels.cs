using System.Text.Json.Serialization;

namespace CursorForge.Client.Models;

public sealed class AuthUser
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AuthSession
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public AuthUser? User { get; set; }
}

public sealed record AuthResult(
    bool Success,
    string? ErrorMessage = null,
    AuthSession? Session = null
);

public sealed record PasswordResetResult(
    bool Success,
    string? ErrorMessage = null,
    string? NewRecoveryCode = null
);
