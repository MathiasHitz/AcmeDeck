using System.Windows;
using AcmeDeck.ViewModels;

namespace AcmeDeck.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel(this);
        DataContext = vm;
        Loaded += async (_, _) => await vm.RefreshAsync();
    }
}
