using System.Windows;
using AcmeDeck.ViewModels;

namespace AcmeDeck.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _clearing;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // PasswordBox.Password cannot be bound; it stays empty unless a new password is typed
        viewModel.Saved += () =>
        {
            _clearing = true;
            PasswordInput.Clear();
            _clearing = false;
        };
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_clearing)
        {
            return;
        }
        _viewModel.Password = PasswordInput.Password;
        _viewModel.PasswordTouched = PasswordInput.Password.Length > 0;
    }
}
