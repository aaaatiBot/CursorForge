using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;
using CursorForge.Client.ViewModels;
using Xunit;

namespace CursorForge.Tests.Unit;

public sealed class OAuthTests : IDisposable
{
    private readonly string _testDir;

    public OAuthTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CursorForge_OAuthTests_" + Guid.NewGuid().ToString("N"));
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
    public void AccountViewModel_GeneratePasswordCommand_PopulatesPasswordAndSetsMessage()
    {
        var localStorage = new LocalStorageService(_testDir);
        string sessionFile = Path.Combine(_testDir, "state", "session.dat");
        var tokenStorage = new DpapiTokenStorage(sessionFile);
        var service = new SupabaseClientService(localStorage, tokenStorage);

        var vm = new AccountViewModel(service);
        Assert.Empty(vm.PasswordInput);

        vm.GeneratePasswordCommand.Execute(null);

        Assert.NotEmpty(vm.PasswordInput);
        Assert.Equal(16, vm.PasswordInput.Length);
        Assert.NotNull(vm.SuccessMessage);
        Assert.Contains("password", vm.SuccessMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountViewModel_SignInWithOAuth_GoogleAndGithub_Succeeds()
    {
        var localStorage = new LocalStorageService(_testDir);
        string sessionFile = Path.Combine(_testDir, "state", "session.dat");
        var tokenStorage = new DpapiTokenStorage(sessionFile);
        var service = new SupabaseClientService(localStorage, tokenStorage);

        var vm = new AccountViewModel(service);

        // Test Google Sign In (mock/offline mode in unit test)
        await vm.SignInWithGoogleCommand.ExecuteAsync(null);
        Assert.True(vm.IsSignedIn);
        Assert.Equal("google_creator", vm.CurrentUsername);
        Assert.Equal("creator@gmail.com", vm.CurrentUserEmail);

        // Sign out
        await vm.SignOutCommand.ExecuteAsync(null);
        Assert.False(vm.IsSignedIn);

        // Test GitHub Sign In (mock/offline mode in unit test)
        await vm.SignInWithGitHubCommand.ExecuteAsync(null);
        Assert.True(vm.IsSignedIn);
        Assert.Equal("github_coder", vm.CurrentUsername);
        Assert.Equal("coder@github.com", vm.CurrentUserEmail);
    }

    [Fact]
    public async Task OAuthLoopbackServer_CancelsGracefullyWhenTokenCancelled()
    {
        using var server = new OAuthLoopbackServer(0);
        Assert.True(server.Port > 0);
        Assert.StartsWith("http://127.0.0.1:", server.RedirectUri);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var result = await server.WaitForCallbackAsync(cts.Token);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
