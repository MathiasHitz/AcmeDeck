using System.Windows;
using System.Windows.Threading;
using AcmeDeck.Views;

namespace AcmeDeck;

public partial class App : Application
{
    public const string Title = "AcmeDeck";
    public const string Author = "Mathias Hitz";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
