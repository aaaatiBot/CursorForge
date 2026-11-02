using System.IO;
using System.Text.Json;
using CursorForge.Client.Models;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;
using CursorForge.Client.ViewModels;

namespace CursorForge.Tests.Unit;

public sealed class SecurityTests : IDisposable
{
    private readonly string _testDir;

    public SecurityTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CursorForge_SecTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void DpapiTokenStorage_RoundTrip_EncryptsAndDecryptsSuccessfully()
    {
        string sessionFile = Path.Combine(_testDir, "session.dat");
        var storage = new DpapiTokenStorage(sessionFile);

        var originalSession = new AuthSession
        {
            AccessToken = "jwt_token_secret_12345",
            RefreshToken = "refresh_secret_67890",
            ExpiresAt = DateTime.UtcNow.AddHours(2),
            User = new AuthUser
            {
                Id = Guid.NewGuid(),
                Username = "test_user",
                Email = "test_user@cursorforge.invalid"
            }
        };

        string json = JsonSerializer.Serialize(originalSession);
        storage.SaveSession(json);

        Assert.True(File.Exists(sessionFile));
        Assert.True(storage.HasSession());

        // Raw bytes on disk must NOT contain plaintext access token
        byte[] rawBytes = File.ReadAllBytes(sessionFile);
        string rawText = System.Text.Encoding.UTF8.GetString(rawBytes);
        Assert.DoesNotContain("jwt_token_secret_12345", rawText);

        // Decrypted content must match original exactly
        string? loadedJson = storage.LoadSession();
        Assert.NotNull(loadedJson);

        var restoredSession = JsonSerializer.Deserialize<AuthSession>(loadedJson);
        Assert.NotNull(restoredSession);
        Assert.Equal(originalSession.AccessToken, restoredSession.AccessToken);
        Assert.Equal(originalSession.User.Username, restoredSession.User?.Username);
    }

    [Fact]
    public void DpapiTokenStorage_CorruptData_SafelyCleansUpAndReturnsNull()
    {
        string sessionFile = Path.Combine(_testDir, "corrupt_session.dat");
        // Write invalid random bytes that fail DPAPI decryption
        File.WriteAllBytes(sessionFile, [0x01, 0x02, 0x03, 0x04, 0xAA, 0xBB, 0xCC, 0xDD]);

        var storage = new DpapiTokenStorage(sessionFile);
        string? result = storage.LoadSession();

        Assert.Null(result);
        Assert.False(File.Exists(sessionFile), "Corrupted session file should be purged");
        Assert.False(storage.HasSession());
    }

    [Fact]
    public void CrockfordBase32_GeneratedCode_FollowsAlphabetAndExcludesAmbiguousChars()
    {
        for (int i = 0; i < 20; i++)
        {
            string code = CrockfordBase32.Generate(20);
            string normalized = CrockfordBase32.Normalize(code);

            Assert.Equal(20, normalized.Length);

            // Must NOT contain ambiguous characters: I, L, O, U
            Assert.DoesNotContain("I", normalized);
            Assert.DoesNotContain("L", normalized);
            Assert.DoesNotContain("O", normalized);
            Assert.DoesNotContain("U", normalized);

            // All characters must be in CrockfordBase32.Alphabet
            foreach (char c in normalized)
            {
                Assert.Contains(c, CrockfordBase32.Alphabet);
            }
        }
    }

    [Fact]
    public void CrockfordBase32_Normalize_NormalizesPunctuationAndSubstitutions()
    {
        // 'i' and 'l' map to '1', 'o' maps to '0', hyphens/spaces stripped
        string input = " io-12  L34 ";
        string normalized = CrockfordBase32.Normalize(input);

        // i -> 1, o -> 0, 1 -> 1, 2 -> 2, L -> 1, 3 -> 3, 4 -> 4
        Assert.Equal("1012134", normalized);
    }

    [Fact]
    public void CrockfordBase32_ComputeRecoveryHash_DeterministicSaltedSha256()
    {
        string username = "  HeroGamer_99  ";
        string recoveryCode = "A1B2-C3D4-E5F6";

        string hash1 = CrockfordBase32.ComputeRecoveryHash(username, recoveryCode);
        string hash2 = CrockfordBase32.ComputeRecoveryHash("herogamer_99", "a1b2c3d4e5f6");

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 64 hex characters for SHA-256
    }

    [Fact]
    public async Task SupabaseClientService_Validation_EnforcesStrictRules()
    {
        var localStorage = new LocalStorageService(_testDir);
        string sessionFile = Path.Combine(_testDir, "state", "session.dat");
        var tokenStorage = new DpapiTokenStorage(sessionFile);
        var service = new SupabaseClientService(localStorage, tokenStorage);

        // Empty username rejected
        var emptyUserResult = await service.SignUpAsync("", "ValidPass123!", "A1B2C3D4E5F67890", true);
        Assert.False(emptyUserResult.Success);
        Assert.Contains("empty", emptyUserResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Short password rejected
        var shortPassResult = await service.SignUpAsync("valid_user", "short", "A1B2C3D4E5F67890", true);
        Assert.False(shortPassResult.Success);
        Assert.Contains("8 characters", shortPassResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Terms unaccepted rejected
        var termsResult = await service.SignUpAsync("valid_user", "ValidPass123!", "A1B2C3D4E5F67890", termsAccepted: false);
        Assert.False(termsResult.Success);
        Assert.Contains("terms", termsResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Invalid characters in username rejected
        var invalidUserResult = await service.SignUpAsync("user@name!#", "ValidPass123!", "A1B2C3D4E5F67890", true);
        Assert.False(invalidUserResult.Success);
    }

    [Fact]
    public async Task AccountViewModel_SignUp_EnforcesTermsAndShowsRecoveryModal()
    {
        var localStorage = new LocalStorageService(_testDir);
        string sessionFile = Path.Combine(_testDir, "state", "session.dat");
        var tokenStorage = new DpapiTokenStorage(sessionFile);
        var service = new SupabaseClientService(localStorage, tokenStorage);

        var vm = new AccountViewModel(service)
        {
            IsRegisterMode = true,
            UsernameInput = "hero_player",
            PasswordInput = "SuperSecretPassword123!",
            TermsAccepted = false
        };

        // Attempt signup without terms
        await vm.SignUpCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("terms", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsRecoveryModalOpen);

        // Now accept terms
        vm.TermsAccepted = true;
        await vm.SignUpCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.IsRecoveryModalOpen);
        Assert.False(string.IsNullOrEmpty(vm.DisplayedRecoveryCode));
        Assert.True(vm.IsSignedIn);
        Assert.Equal("hero_player", vm.CurrentUsername);

        // Dismiss modal
        vm.DismissRecoveryModalCommand.Execute(null);
        Assert.False(vm.IsRecoveryModalOpen);
        Assert.Empty(vm.DisplayedRecoveryCode);

        // Sign out
        await vm.SignOutCommand.ExecuteAsync(null);
        Assert.False(vm.IsSignedIn);
        Assert.Empty(vm.CurrentUsername);
    }
}
