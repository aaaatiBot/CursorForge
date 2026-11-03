using System.Windows;
using System.Windows.Controls;
using CursorForge.Client.ViewModels;

namespace CursorForge.Client.Views;

public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AccountViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is AccountViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is AccountViewModel vm)
        {
            if (e.PropertyName == nameof(AccountViewModel.PasswordInput) && AuthPasswordBox != null)
            {
                if (AuthPasswordBox.Password != vm.PasswordInput)
                {
                    AuthPasswordBox.Password = vm.PasswordInput;
                }
            }
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
        {
            vm.PasswordInput = pb.Password;
        }
    }

    private void OnResetPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
        {
            vm.ResetNewPassword = pb.Password;
        }
    }
}

