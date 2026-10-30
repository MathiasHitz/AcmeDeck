using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace AcmeDeck.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "";
        // Strip the source revision that the SDK appends (1.0.0+abc123)
        var plus = version.IndexOf('+');
        VersionText.Text = "Version " + (plus > 0 ? version[..plus] : version);
        AuthorText.Text = App.Author;
        CopyrightRun.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnShowLicense(object sender, RoutedEventArgs e)
    {
        // The setup installs LICENSE next to the executable
        var file = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        if (File.Exists(file))
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo("https://opensource.org/license/mit") { UseShellExecute = true });
        }
    }
}
