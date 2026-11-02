using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CursorForge.Client.Models;

namespace CursorForge.Client.Services.Supabase;

public sealed partial class SupabaseClientService : ISupabaseClientService
{
    private readonly AppConfig _config;
    private readonly IDpapiTokenStorage _tokenStorage;
    private readonly IStructuredLogger? _logger;
    private readonly HttpClient _httpClient;

    private global::Supabase.Client? _supabase;
    private AuthSession? _currentSession;
    private AuthUser? _currentUser;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.SupabaseUrl) &&
                                !string.IsNullOrWhiteSpace(_config.SupabaseAnonKey) &&
                                !_config.SupabaseUrl.Contains("your-project-id");

    public bool IsAuthenticated => _currentUser != null && _currentSession != null;
    public AuthUser? CurrentUser => _currentUser;
    public AuthSession? CurrentSession => _currentSession;
    public event Action<bool>? AuthStateChanged;

    public SupabaseClientService(
        ILocalStorageService storageService,
        IDpapiTokenStorage tokenStorage,
        IStructuredLogger? logger = null,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(tokenStorage);

        _config = storageService.LoadConfig();
        _tokenStorage = tokenStorage;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task InitializeAsync()
    {
        if (!IsConfigured)
        {
            _logger?.LogInformation("auth.unconfigured", "Supabase client is not configured with valid URL/Key. Running in offline mode.");
            return;
        }

        try
        {
            var options = new global::Supabase.SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = _config.EnableRealtime
            };

            _supabase = new global::Supabase.Client(_config.SupabaseUrl, _config.SupabaseAnonKey, options);
            await _supabase.InitializeAsync();

            // Try to restore session from DPAPI storage
            string? savedSessionJson = _tokenStorage.LoadSession();
            if (!string.IsNullOrEmpty(savedSessionJson))
            {
                var session = JsonSerializer.Deserialize<AuthSession>(savedSessionJson);
                if (session != null && session.ExpiresAt > DateTime.UtcNow && session.User != null)
                {
                    _currentSession = session;
                    _currentUser = session.User;
                    AuthStateChanged?.Invoke(true);
                    _logger?.LogInformation("auth.session_restored", $"Restored DPAPI session for user: {_currentUser.Username}");
                }
                else
                {
                    _tokenStorage.ClearSession();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("auth.init_failed", "Failed to initialize Supabase client.", ex);
        }
    }

    public async Task<AuthResult> SignUpAsync(
        string username,
        string password,
        string recoveryCode,
        bool termsAccepted)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new AuthResult(false, "Username cannot be empty.");
        }

        string cleanUser = username.Trim();
        if (!UsernameRegex().IsMatch(cleanUser))
        {
            return new AuthResult(false, "Username must be 3-30 characters, containing only letters, numbers, and underscores.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return new AuthResult(false, "Password must be at least 8 characters long.");
        }

        if (!termsAccepted)
        {
            return new AuthResult(false, "You must accept the terms of service.");
        }

        string normalizedCode = CrockfordBase32.Normalize(recoveryCode);
        if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length < 16)
        {
            return new AuthResult(false, "Invalid recovery code format.");
        }

        string recoveryHash = CrockfordBase32.ComputeRecoveryHash(cleanUser, normalizedCode);
        string email = $"{cleanUser.ToLowerInvariant()}@cursorforge.invalid";

        if (!IsConfigured || _supabase == null)
        {
            // Offline demo/mock mode
            var mockUser = new AuthUser
            {
                Id = Guid.NewGuid(),
                Username = cleanUser,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };
            var mockSession = new AuthSession
            {
                AccessToken = "mock_access_token_" + Guid.NewGuid().ToString("N"),
                RefreshToken = "mock_refresh_token_" + Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                User = mockUser
            };

            SetSession(mockSession);
            return new AuthResult(true, null, mockSession);
        }

        try
        {
            var userMetadata = new Dictionary<string, object>
            {
                { "username", cleanUser },
                { "recovery_code_hash", recoveryHash },
                { "terms_accepted", "true" }
            };

            var signUpOptions = new global::Supabase.Gotrue.SignUpOptions
            {
                Data = userMetadata
            };

            var response = await _supabase.Auth.SignUp(email, password, signUpOptions);
            if (response?.User == null)
            {
                return new AuthResult(false, "Signup failed. No user was returned.");
            }

            Guid userId = Guid.TryParse(response.User.Id, out var parsedId) ? parsedId : Guid.NewGuid();
            var authUser = new AuthUser
            {
                Id = userId,
                Username = cleanUser,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

            var session = new AuthSession
            {
                AccessToken = response.AccessToken ?? string.Empty,
                RefreshToken = response.RefreshToken ?? string.Empty,
                ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn > 0 ? response.ExpiresIn : 3600),
                User = authUser
            };

            SetSession(session);
            _logger?.LogInformation("auth.signup_success", $"Account successfully created for: {cleanUser}");
            return new AuthResult(true, null, session);
        }
        catch (Exception ex)
        {
            _logger?.LogError("auth.signup_error", $"Signup error for {cleanUser}", ex);
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task<AuthResult> SignInAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new AuthResult(false, "Username is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return new AuthResult(false, "Password is required.");
        }

        string cleanUser = username.Trim();
        string email = $"{cleanUser.ToLowerInvariant()}@cursorforge.invalid";

        if (!IsConfigured || _supabase == null)
        {
            // Offline demo/mock mode
            var mockUser = new AuthUser
            {
                Id = Guid.NewGuid(),
                Username = cleanUser,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };
            var mockSession = new AuthSession
            {
                AccessToken = "mock_access_token_" + Guid.NewGuid().ToString("N"),
                RefreshToken = "mock_refresh_token_" + Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                User = mockUser
            };

            SetSession(mockSession);
            return new AuthResult(true, null, mockSession);
        }

        try
        {
            var response = await _supabase.Auth.SignInWithPassword(email, password);
            if (response?.User == null)
            {
                return new AuthResult(false, "Invalid username or password.");
            }

            Guid userId = Guid.TryParse(response.User.Id, out var parsedId) ? parsedId : Guid.NewGuid();
            var authUser = new AuthUser
            {
                Id = userId,
                Username = cleanUser,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

            var session = new AuthSession
            {
                AccessToken = response.AccessToken ?? string.Empty,
                RefreshToken = response.RefreshToken ?? string.Empty,
                ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn > 0 ? response.ExpiresIn : 3600),
                User = authUser
            };

            SetSession(session);
            _logger?.LogInformation("auth.signin_success", $"User signed in: {cleanUser}");
            return new AuthResult(true, null, session);
        }
        catch (Exception ex)
        {
            _logger?.LogError("auth.signin_failed", $"Sign-in failed for {cleanUser}", ex);
            return new AuthResult(false, "Invalid credentials or network failure.");
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (_supabase != null)
            {
                await _supabase.Auth.SignOut();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("auth.signout_warning", "Error during Supabase sign-out.", ex);
        }
        finally
        {
            _currentSession = null;
            _currentUser = null;
            _tokenStorage.ClearSession();
            AuthStateChanged?.Invoke(false);
            _logger?.LogInformation("auth.signout_success", "User signed out and DPAPI session cleared.");
        }
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(
        string username,
        string recoveryCode,
        string newPassword)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new PasswordResetResult(false, "Username is required.");
        }

        if (string.IsNullOrWhiteSpace(recoveryCode))
        {
            return new PasswordResetResult(false, "Recovery code is required.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return new PasswordResetResult(false, "New password must be at least 8 characters.");
        }

        string cleanUser = username.Trim();
        string normalizedCode = CrockfordBase32.Normalize(recoveryCode);

        if (!IsConfigured)
        {
            // Offline mock test mode
            string newCode = CrockfordBase32.Generate();
            return new PasswordResetResult(true, null, newCode);
        }

        try
        {
            string endpoint = $"{_config.SupabaseUrl.TrimEnd('/')}/functions/v1/reset-password";
            var requestPayload = new
            {
                username = cleanUser,
                recovery_code = normalizedCode,
                new_password = newPassword
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.SupabaseAnonKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = "Password reset failed.";
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("error", out var errProp))
                    {
                        errorMsg = errProp.GetString() ?? errorMsg;
                    }
                }
                catch { }

                return new PasswordResetResult(false, errorMsg);
            }

            using var successDoc = JsonDocument.Parse(responseBody);
            string? newRotatedCode = null;
            if (successDoc.RootElement.TryGetProperty("new_recovery_code", out var codeProp))
            {
                newRotatedCode = codeProp.GetString();
            }

            _logger?.LogInformation("auth.reset_success", $"Password reset completed successfully for user: {cleanUser}");
            return new PasswordResetResult(true, null, newRotatedCode);
        }
        catch (Exception ex)
        {
            _logger?.LogError("auth.reset_error", $"Password reset call error for {cleanUser}", ex);
            return new PasswordResetResult(false, "Network error during password reset.");
        }
    }

    private void SetSession(AuthSession session)
    {
        _currentSession = session;
        _currentUser = session.User;

        try
        {
            string sessionJson = JsonSerializer.Serialize(session);
            _tokenStorage.SaveSession(sessionJson);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("auth.dpapi_save_failed", "Failed to persist session to DPAPI storage.", ex);
        }

        AuthStateChanged?.Invoke(true);
    }

    [GeneratedRegex("^[a-zA-Z0-9_]{3,30}$")]
    private static partial Regex UsernameRegex();
}
