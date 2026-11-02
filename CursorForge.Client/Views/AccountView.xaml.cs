using System.Windows;
using System.Windows.Controls;
using CursorForge.Client.ViewModels;

namespace CursorForge.Client.Views;

public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
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

