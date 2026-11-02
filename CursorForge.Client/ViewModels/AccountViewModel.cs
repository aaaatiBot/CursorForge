using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorForge.Client.Services;
using CursorForge.Client.Services.Supabase;

namespace CursorForge.Client.ViewModels;

public sealed partial class AccountViewModel : ObservableObject
{
    private readonly ISupabaseClientService _supabase;
    private readonly IStructuredLogger? _logger;

    [ObservableProperty]
    private string _title = "Account";

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private string _currentUsername = string.Empty;

    [ObservableProperty]
    private string _currentUserId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignInMode))]
    private bool _isRegisterMode;

    public bool IsSignInMode
    {
        get => !IsRegisterMode;
        set => IsRegisterMode = !value;
    }

    [ObservableProperty]
    private bool _isTermsModalOpen;

    [ObservableProperty]
    private string _usernameInput = string.Empty;

    [ObservableProperty]
    private string _passwordInput = string.Empty;

    [ObservableProperty]
    private bool _termsAccepted;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    // One-time Crockford recovery code display modal
    [ObservableProperty]
    private bool _isRecoveryModalOpen;

    [ObservableProperty]
    private string _displayedRecoveryCode = string.Empty;

    // Password reset modal state
    [ObservableProperty]
    private bool _isResetModalOpen;

    [ObservableProperty]
    private string _resetUsername = string.Empty;

    [ObservableProperty]
    private string _resetRecoveryCode = string.Empty;

    [ObservableProperty]
    private string _resetNewPassword = string.Empty;

    [ObservableProperty]
    private string? _newRotatedRecoveryCode;

    public AccountViewModel(ISupabaseClientService supabase, IStructuredLogger? logger = null)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        _logger = logger;

        _supabase.AuthStateChanged += OnAuthStateChanged;
        RefreshAuthState();
    }

    private void OnAuthStateChanged(bool authenticated)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshAuthState);
        }
        else
        {
            RefreshAuthState();
        }
    }

    private void RefreshAuthState()
    {
        IsSignedIn = _supabase.IsAuthenticated;
        if (IsSignedIn && _supabase.CurrentUser != null)
        {
            CurrentUsername = _supabase.CurrentUser.Username;
            CurrentUserId = _supabase.CurrentUser.Id.ToString();
        }
        else
        {
            CurrentUsername = string.Empty;
            CurrentUserId = string.Empty;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    private void OpenTermsModal()
    {
        IsTermsModalOpen = true;
    }

    [RelayCommand]
    private void CloseTermsModal()
    {
        IsTermsModalOpen = false;
    }

    [RelayCommand]
    public async Task QuickLoginDemoAsync()
    {
        UsernameInput = "momo";
        PasswordInput = "momomomo123";
        TermsAccepted = true;
        await SignInAsync();
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        SuccessMessage = null;
        IsBusy = true;

        try
        {
            var result = await _supabase.SignInAsync(UsernameInput, PasswordInput);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Sign in failed. Check your credentials.";
            }
            else
            {
                PasswordInput = string.Empty;
                SuccessMessage = "Signed in successfully!";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("ui.signin_failed", "Sign in error", ex);
            ErrorMessage = "An unexpected error occurred.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignUpAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        SuccessMessage = null;

        if (!TermsAccepted)
        {
            ErrorMessage = "You must accept the terms of service to create an account.";
            return;
        }

        IsBusy = true;
        try
        {
            string generatedCode = CrockfordBase32.Generate();
            var result = await _supabase.SignUpAsync(UsernameInput, PasswordInput, generatedCode, TermsAccepted);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Registration failed.";
            }
            else
            {
                DisplayedRecoveryCode = generatedCode;
                IsRecoveryModalOpen = true;
                PasswordInput = string.Empty;
                SuccessMessage = "Account registered successfully!";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("ui.signup_failed", "Sign up error", ex);
            ErrorMessage = "An unexpected error occurred during signup.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            await _supabase.SignOutAsync();
            SuccessMessage = "You have signed out.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenResetModal()
    {
        ResetUsername = UsernameInput;
        ResetRecoveryCode = string.Empty;
        ResetNewPassword = string.Empty;
        NewRotatedRecoveryCode = null;
        ErrorMessage = null;
        IsResetModalOpen = true;
    }

    [RelayCommand]
    private void CloseResetModal()
    {
        IsResetModalOpen = false;
    }

    [RelayCommand]
    private async Task SubmitResetAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            var result = await _supabase.ResetPasswordAsync(ResetUsername, ResetRecoveryCode, ResetNewPassword);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Password reset failed. Invalid username or recovery code.";
            }
            else
            {
                NewRotatedRecoveryCode = result.NewRecoveryCode;
                SuccessMessage = "Password successfully reset! Your recovery code has been rotated.";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("ui.reset_failed", "Password reset error", ex);
            ErrorMessage = "An unexpected error occurred.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissRecoveryModal()
    {
        IsRecoveryModalOpen = false;
        DisplayedRecoveryCode = string.Empty;
    }

    [RelayCommand]
    private void CopyRecoveryCode()
    {
        if (!string.IsNullOrEmpty(DisplayedRecoveryCode))
        {
            try
            {
                Clipboard.SetText(DisplayedRecoveryCode);
            }
            catch { }
        }
    }

    [RelayCommand]
    private void CopyRotatedRecoveryCode()
    {
        if (!string.IsNullOrEmpty(NewRotatedRecoveryCode))
        {
            try
            {
                Clipboard.SetText(NewRotatedRecoveryCode);
            }
            catch { }
        }
    }
}
