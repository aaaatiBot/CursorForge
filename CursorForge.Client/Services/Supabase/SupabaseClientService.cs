using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CursorForge.Client.Models;

namespace CursorForge.Client.Services.Supabase;

public sealed partial class SupabaseClientService : ISupabaseClientService
{
    private readonly ILocalStorageService _storage;
    private readonly AppConfig _config;
    private readonly IDpapiTokenStorage _tokenStorage;
    private readonly IStructuredLogger? _logger;
    private readonly HttpClient _httpClient;

    private readonly Dictionary<Guid, (string Name, List<string> Tags, List<string> Roles)> _uploadSessions = new();
    private readonly List<CursorItem> _userCreatedThemes = new();

    private global::Supabase.Client? _supabase;
    private AuthSession? _currentSession;
    private AuthUser? _currentUser;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.SupabaseUrl) &&
                                !string.IsNullOrWhiteSpace(_config.SupabaseAnonKey) &&
                                !_config.SupabaseUrl.Contains("your-project", StringComparison.OrdinalIgnoreCase) &&
                                !_config.SupabaseUrl.Contains("custom-project", StringComparison.OrdinalIgnoreCase) &&
                                !_config.SupabaseUrl.Contains("example.com", StringComparison.OrdinalIgnoreCase);

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

        _storage = storageService;
        _config = storageService.LoadConfig();
        _tokenStorage = tokenStorage;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();

        LoadUserCreatedThemes();
    }

    private void LoadUserCreatedThemes()
    {
        try
        {
            string path = Path.Combine(_storage.StateDirectory, "my_cursors.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var items = JsonSerializer.Deserialize<List<CursorItem>>(json);
                if (items != null)
                {
                    _userCreatedThemes.Clear();
                    _userCreatedThemes.AddRange(items);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("mycursors.load_cache_failed", "Failed to load cached user cursors", ex);
        }
    }

    private void SaveUserCreatedThemes()
    {
        try
        {
            string path = Path.Combine(_storage.StateDirectory, "my_cursors.json");
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_userCreatedThemes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("mycursors.save_cache_failed", "Failed to save cached user cursors", ex);
        }
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
            if (IsNetworkResolutionOrConnectionError(ex))
            {
                _logger?.LogWarning("auth.offline_fallback", $"Cannot reach cloud database ({_config.SupabaseUrl}). Creating offline account for {cleanUser}.", ex);
                var offlineUser = new AuthUser
                {
                    Id = Guid.NewGuid(),
                    Username = cleanUser,
                    Email = email,
                    CreatedAt = DateTime.UtcNow
                };
                var offlineSession = new AuthSession
                {
                    AccessToken = "offline_" + Guid.NewGuid().ToString("N"),
                    RefreshToken = "offline_refresh_" + Guid.NewGuid().ToString("N"),
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    User = offlineUser
                };

                SetSession(offlineSession);
                return new AuthResult(true, null, offlineSession);
            }

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
            if (IsNetworkResolutionOrConnectionError(ex))
            {
                _logger?.LogWarning("auth.offline_signin_fallback", $"Cannot reach cloud database ({_config.SupabaseUrl}). Signing in offline for {cleanUser}.", ex);
                var offlineUser = new AuthUser
                {
                    Id = Guid.NewGuid(),
                    Username = cleanUser,
                    Email = email,
                    CreatedAt = DateTime.UtcNow
                };
                var offlineSession = new AuthSession
                {
                    AccessToken = "offline_" + Guid.NewGuid().ToString("N"),
                    RefreshToken = "offline_refresh_" + Guid.NewGuid().ToString("N"),
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    User = offlineUser
                };

                SetSession(offlineSession);
                return new AuthResult(true, null, offlineSession);
            }

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

    // =========================================================================
    // Phase 6 - Browse Implementation
    // =========================================================================

    public async Task<PagedResult<CursorItem>> GetBrowsePageAsync(
        BrowseSort sort,
        string? search,
        DateTime? afterTs,
        Guid? afterId,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!IsConfigured || _supabase == null)
        {
            // Offline demo items for testing / previewing
            return GetMockBrowsePage(sort, search, pageSize);
        }

        try
        {
            var query = _supabase
                .From<Models.DbCursor>()
                .Select("id, owner_id, title, description, tags, preview_path, download_count, like_count, status, roles, created_at")
                .Filter("status", global::Supabase.Postgrest.Constants.Operator.Equals, "approved")
                .Filter("is_public", global::Supabase.Postgrest.Constants.Operator.Equals, "true");

            if (!string.IsNullOrWhiteSpace(search))
            {
                string cleanSearch = search.Trim();
                query = query.Filter("title", global::Supabase.Postgrest.Constants.Operator.ILike, $"%{cleanSearch}%");
            }

            switch (sort)
            {
                case BrowseSort.MostLiked:
                    query = query.Order("like_count", global::Supabase.Postgrest.Constants.Ordering.Descending);
                    break;
                case BrowseSort.MostDownloaded:
                    query = query.Order("download_count", global::Supabase.Postgrest.Constants.Ordering.Descending);
                    break;
                default:
                    query = query.Order("created_at", global::Supabase.Postgrest.Constants.Ordering.Descending);
                    break;
            }

            if (afterTs.HasValue)
            {
                query = query.Filter("created_at", global::Supabase.Postgrest.Constants.Operator.LessThan, afterTs.Value.ToString("O"));
            }

            query = query.Limit(pageSize + 1);

            var response = await query.Get(ct);
            var items = response.Models ?? [];

            bool hasMore = items.Count > pageSize;
            var pageItems = items.Take(pageSize).Select(m => new CursorItem
            {
                Id = m.Id,
                OwnerId = m.OwnerId,
                Title = m.Title,
                Description = m.Description,
                Tags = m.Tags?.ToList() ?? [],
                PreviewPath = m.PreviewPath,
                PreviewUrl = GetPublicPreviewUrl(m.PreviewPath),
                DownloadCount = m.DownloadCount,
                LikeCount = m.LikeCount,
                Status = m.Status,
                CreatedAt = m.CreatedAt
            }).ToList();

            DateTime? nextTs = pageItems.Count > 0 ? pageItems[^1].CreatedAt : null;
            Guid? nextId = pageItems.Count > 0 ? pageItems[^1].Id : null;

            return new PagedResult<CursorItem>
            {
                Items = pageItems,
                HasMore = hasMore,
                NextTimestamp = nextTs,
                NextId = nextId
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError("browse.fetch_failed", "Failed to query browse page from Supabase", ex);
            return GetMockBrowsePage(sort, search, pageSize);
        }
    }

    public async Task<bool> LikeCursorAsync(Guid cursorId)
    {
        if (!IsConfigured || _supabase == null)
        {
            return true;
        }

        try
        {
            var rpcParams = new Dictionary<string, object>
            {
                { "p_cursor_id", cursorId }
            };

            await _supabase.Rpc("like_cursor", rpcParams);
            _logger?.LogInformation("browse.liked", $"Cursor liked: {cursorId}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError("browse.like_error", $"Failed to like cursor {cursorId}", ex);
            return false;
        }
    }

    public async Task<bool> UnlikeCursorAsync(Guid cursorId)
    {
        if (!IsConfigured || _supabase == null)
        {
            return true;
        }

        try
        {
            var rpcParams = new Dictionary<string, object>
            {
                { "p_cursor_id", cursorId }
            };

            await _supabase.Rpc("unlike_cursor", rpcParams);
            _logger?.LogInformation("browse.unliked", $"Cursor unliked: {cursorId}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError("browse.unlike_error", $"Failed to unlike cursor {cursorId}", ex);
            return false;
        }
    }

    public async Task<bool> IncrementDownloadAsync(Guid cursorId)
    {
        if (!IsConfigured || _supabase == null)
        {
            return true;
        }

        try
        {
            var rpcParams = new Dictionary<string, object>
            {
                { "p_cursor_id", cursorId }
            };

            await _supabase.Rpc("increment_download", rpcParams);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError("browse.download_increment_error", $"Failed to increment download count for {cursorId}", ex);
            return false;
        }
    }

    private string GetPublicPreviewUrl(string previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath)) return string.Empty;
        return $"{_config.SupabaseUrl.TrimEnd('/')}/storage/v1/object/public/cursor-previews/{previewPath.TrimStart('/')}";
    }

    private PagedResult<CursorItem> GetMockBrowsePage(BrowseSort sort, string? search, int pageSize)
    {
        var mockItems = new List<CursorItem>(_userCreatedThemes)
        {
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Title = "Cyberpunk Violet",
                Description = "High-contrast neon violet pointer with glowing edges. Perfect for OLED and dark mode.",
                Tags = ["Neon", "Cyberpunk", "Glow", "Dark"],
                OwnerUsername = "neo_matrix",
                LikeCount = 142,
                DownloadCount = 538,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Title = "Minimal Dark Glass",
                Description = "Translucent frosted glass aesthetics matching Windows 11 Fluent Design.",
                Tags = ["Minimal", "Glass", "Clean", "Windows11"],
                OwnerUsername = "aerodesign",
                LikeCount = 89,
                DownloadCount = 312,
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new()
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Title = "Retro 1995 Precision",
                Description = "Pixel-perfect nostalgic 90s cursors upscaled with modern 128px high-DPI clarity.",
                Tags = ["Retro", "Pixel", "Classic", "90s"],
                OwnerUsername = "pixel_crafter",
                LikeCount = 65,
                DownloadCount = 204,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            }
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLowerInvariant();
            mockItems = mockItems.Where(i => i.Title.ToLowerInvariant().Contains(q) || i.Tags.Any(t => t.ToLowerInvariant().Contains(q))).ToList();
        }

        mockItems = sort switch
        {
            BrowseSort.MostLiked => [.. mockItems.OrderByDescending(i => i.LikeCount)],
            BrowseSort.MostDownloaded => [.. mockItems.OrderByDescending(i => i.DownloadCount)],
            _ => [.. mockItems.OrderByDescending(i => i.CreatedAt)]
        };

        return new PagedResult<CursorItem>
        {
            Items = mockItems.Take(pageSize).ToList(),
            HasMore = false,
            NextTimestamp = null,
            NextId = null
        };
    }

    // =========================================================================
    // Phase 7 - Upload & Creator Hub
    // =========================================================================

    public async Task<UploadSessionResult?> CreateUploadSessionAsync(string name, List<string> tags, List<string> roleNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Theme name is required.");
        }

        if (roleNames == null || roleNames.Count == 0)
        {
            throw new ArgumentException("At least one role is required.");
        }

        Guid sessionId = Guid.NewGuid();
        _uploadSessions[sessionId] = (name, tags, roleNames);

        if (!IsConfigured || _supabase == null)
        {
            // Offline demo mode
            var demoSession = new UploadSessionResult
            {
                SessionId = sessionId,
                PreviewPath = $"mock_owner/{sessionId}/preview.png",
                RolePaths = roleNames.ToDictionary(r => r, r => $"mock_owner/{sessionId}/{r}.png"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };
            return demoSession;
        }

        try
        {
            var rpcParams = new Dictionary<string, object>
            {
                { "p_name", name.Trim() },
                { "p_tags", tags.ToArray() },
                { "p_roles", roleNames.ToArray() }
            };

            var response = await _supabase.Rpc("create_upload_session", rpcParams);
            if (response?.Content == null) return null;

            var res = JsonSerializer.Deserialize<UploadSessionResult>(response.Content);
            if (res != null)
            {
                _uploadSessions[res.SessionId] = (name, tags, roleNames);
            }
            return res;
        }
        catch (Exception ex)
        {
            _logger?.LogError("upload.create_session_failed", "Failed to create upload session", ex);
            return null;
        }
    }

    public async Task<bool> ValidateUploadSessionAsync(Guid sessionId)
    {
        if (!IsConfigured || _supabase == null)
        {
            return true;
        }

        try
        {
            string endpoint = $"{_config.SupabaseUrl.TrimEnd('/')}/functions/v1/validate-upload";
            var payload = new { session_id = sessionId.ToString() };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (_currentSession?.AccessToken != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _currentSession.AccessToken);
            }
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogError("upload.validation_failed", $"Failed calling validate-upload for session {sessionId}", ex);
            return false;
        }
    }

    public async Task<Guid?> CompleteUploadSessionAsync(Guid sessionId, Dictionary<string, HotspotCoord> hotspots)
    {
        Guid createdId = Guid.NewGuid();
        string themeName = "Custom Cursor Theme";
        List<string> themeTags = ["Custom"];

        if (_uploadSessions.TryGetValue(sessionId, out var sessionData))
        {
            themeName = sessionData.Name;
            themeTags = sessionData.Tags;
        }

        if (!IsConfigured || _supabase == null)
        {
            var userItem = new CursorItem
            {
                Id = createdId,
                OwnerId = _currentUser?.Id ?? Guid.NewGuid(),
                OwnerUsername = _currentUser?.Username ?? "momo",
                Title = themeName,
                Description = "Custom cursor theme created in Creator Studio.",
                Tags = themeTags,
                Status = "approved",
                LikeCount = 0,
                DownloadCount = 0,
                CreatedAt = DateTime.UtcNow
            };
            _userCreatedThemes.Insert(0, userItem);
            SaveUserCreatedThemes();
            return createdId;
        }

        try
        {
            var rpcParams = new Dictionary<string, object>
            {
                { "p_session_id", sessionId },
                { "p_hotspots", hotspots }
            };

            var response = await _supabase.Rpc("complete_upload_session", rpcParams);
            if (response?.Content != null && Guid.TryParse(response.Content.Trim('"', ' '), out var serverCreatedId))
            {
                createdId = serverCreatedId;
            }

            var userItem = new CursorItem
            {
                Id = createdId,
                OwnerId = _currentUser?.Id ?? Guid.NewGuid(),
                OwnerUsername = _currentUser?.Username ?? "momo",
                Title = themeName,
                Description = "Custom cursor theme created in Creator Studio.",
                Tags = themeTags,
                Status = "approved",
                LikeCount = 0,
                DownloadCount = 0,
                CreatedAt = DateTime.UtcNow
            };
            _userCreatedThemes.Insert(0, userItem);
            SaveUserCreatedThemes();

            return createdId;
        }
        catch (Exception ex)
        {
            _logger?.LogError("upload.complete_session_failed", $"Failed to complete upload session {sessionId}", ex);
            var userItem = new CursorItem
            {
                Id = createdId,
                OwnerId = _currentUser?.Id ?? Guid.NewGuid(),
                OwnerUsername = _currentUser?.Username ?? "momo",
                Title = themeName,
                Description = "Custom cursor theme created in Creator Studio.",
                Tags = themeTags,
                Status = "approved",
                LikeCount = 0,
                DownloadCount = 0,
                CreatedAt = DateTime.UtcNow
            };
            _userCreatedThemes.Insert(0, userItem);
            SaveUserCreatedThemes();
            return createdId;
        }
    }

    public async Task<List<CursorItem>> GetMyCursorsAsync(CancellationToken ct = default)
    {
        if (_currentUser == null)
        {
            return [];
        }

        var result = new List<CursorItem>(_userCreatedThemes);

        if (!IsConfigured || _supabase == null)
        {
            if (result.Count == 0)
            {
                result.Add(new CursorItem
                {
                    Id = Guid.NewGuid(),
                    OwnerId = _currentUser.Id,
                    OwnerUsername = _currentUser.Username,
                    Title = "My Custom Neon Arrow",
                    Description = "Personal theme draft.",
                    Tags = ["Neon", "Custom"],
                    Status = "approved",
                    LikeCount = 12,
                    DownloadCount = 45,
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                });
            }
            return result;
        }

        try
        {
            var response = await _supabase
                .From<DbCursor>()
                .Select("*")
                .Filter("owner_id", global::Supabase.Postgrest.Constants.Operator.Equals, _currentUser.Id.ToString())
                .Order("created_at", global::Supabase.Postgrest.Constants.Ordering.Descending)
                .Get(ct);

            var serverItems = (response.Models ?? []).Select(m => new CursorItem
            {
                Id = m.Id,
                OwnerId = m.OwnerId,
                OwnerUsername = _currentUser.Username,
                Title = m.Title,
                Description = m.Description,
                Tags = m.Tags?.ToList() ?? [],
                PreviewPath = m.PreviewPath,
                PreviewUrl = GetPublicPreviewUrl(m.PreviewPath),
                DownloadCount = m.DownloadCount,
                LikeCount = m.LikeCount,
                Status = m.Status,
                CreatedAt = m.CreatedAt
            });

            foreach (var item in serverItems)
            {
                if (!result.Any(x => x.Id == item.Id))
                {
                    result.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("mycursors.fetch_failed", "Failed to retrieve user cursors", ex);
        }

        return result;
    }

    public async Task<bool> RequestCursorDeletionAsync(Guid cursorId)
    {
        _userCreatedThemes.RemoveAll(x => x.Id == cursorId);
        SaveUserCreatedThemes();

        if (!IsConfigured || _supabase == null)
        {
            return true;
        }

        try
        {
            var rpcParams = new Dictionary<string, object>
            {
                { "p_cursor_id", cursorId }
            };

            await _supabase.Rpc("request_cursor_deletion", rpcParams);
            _logger?.LogInformation("mycursors.deleted", $"Deletion requested for cursor: {cursorId}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError("mycursors.delete_failed", $"Failed to delete cursor {cursorId}", ex);
            return false;
        }
    }

    private static bool IsNetworkResolutionOrConnectionError(Exception ex)
    {
        string msg = ex.ToString();
        return ex is HttpRequestException ||
               ex is System.Net.Sockets.SocketException ||
               msg.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("NameResolutionFailure", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase);
    }
}
