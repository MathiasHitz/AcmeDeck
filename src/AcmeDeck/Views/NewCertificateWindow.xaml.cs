using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using AcmeDeck.ViewModels;

namespace AcmeDeck.Views;

public partial class NewCertificateWindow : Window
{
    public NewCertificateWindow(NewCertificateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Owner = this;
        viewModel.RequestClose += ok => DialogResult = ok;
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
