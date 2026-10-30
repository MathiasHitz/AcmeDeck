using System.Windows;
using AcmeDeck.ViewModels;

namespace AcmeDeck.Views;

public partial class LogsWindow : Window
{
    public LogsWindow(LogsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
