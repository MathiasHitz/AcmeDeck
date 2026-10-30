using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using AcmeDeck.Services;

namespace AcmeDeck.Views;

public sealed record RunOutcome(bool Success, string Message, CheckLevel? Level = null);

/// <summary>
/// Gives a run script access to wacs.exe and the output pane.
/// </summary>
public sealed class RunContext(RunWindow window, string wacsPath, CancellationToken token)
{
    public CancellationToken Token { get; } = token;

    public void Log(string line) => window.AppendLine(line);

    public Task<WacsRunResult> WacsAsync(IReadOnlyList<string> arguments)
    {
        window.ShowCommand(WacsRunner.FormatCommandLine(wacsPath, arguments));
        return WacsRunner.RunAsync(wacsPath, arguments, window.AppendLine, Token);
    }
}

/// <summary>
/// Modal window that runs one or more wacs.exe calls and shows their output live.
/// </summary>
public partial class RunWindow : Window
{
    private readonly Func<RunContext, Task<RunOutcome>> _script;
    private readonly string _wacsPath;
    private readonly CancellationTokenSource _cts = new();
    private bool _running;
    private bool _closeWhenDone;

    private RunWindow(string title, string wacsPath, Func<RunContext, Task<RunOutcome>> script)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        _wacsPath = wacsPath;
        _script = script;
        Loaded += async (_, _) => await StartAsync();
    }

    public RunOutcome? Outcome { get; private set; }

    /// <summary>
    /// Shows the window, runs the script and returns its outcome once the user closes it.
    /// </summary>
    public static RunOutcome Run(Window? owner, string title, string wacsPath, Func<RunContext, Task<RunOutcome>> script)
    {
        var window = new RunWindow(title, wacsPath, script) { Owner = owner };
        window.ShowDialog();
        return window.Outcome ?? new RunOutcome(false, "Cancelled");
    }

    internal void AppendLine(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            OutputBox.AppendText(line + Environment.NewLine);
            OutputBox.ScrollToEnd();
        });
    }

    internal void ShowCommand(string command) => Dispatcher.BeginInvoke(() => CommandText.Text = command);

    private async Task StartAsync()
    {
        _running = true;
        RunOutcome outcome;
        try
        {
            outcome = await Task.Run(() => _script(new RunContext(this, _wacsPath, _cts.Token)));
        }
        catch (OperationCanceledException)
        {
            outcome = new RunOutcome(false, "The operation was cancelled.");
        }
        catch (Exception ex)
        {
            AppendLine("ERROR: " + ex.Message);
            outcome = new RunOutcome(false, ex.Message);
        }
        if (_cts.IsCancellationRequested && outcome.Success == false)
        {
            outcome = outcome with { Message = "Cancelled. " + outcome.Message };
        }
        _running = false;
        Outcome = outcome;
        ShowOutcome(outcome);
        if (_closeWhenDone)
        {
            Close();
        }
    }

    private void ShowOutcome(RunOutcome outcome)
    {
        var level = outcome.Level ?? (outcome.Success ? CheckLevel.Ok : CheckLevel.Error);
        var (brush, background, symbol) = level switch
        {
            CheckLevel.Ok => ("OkBrush", "OkBackgroundBrush", "✓"),
            CheckLevel.Warning => ("WarningBrush", "WarningBackgroundBrush", "!"),
            CheckLevel.Error => ("ErrorBrush", "ErrorBackgroundBrush", "✕"),
            _ => ("InfoBrush", "InfoBackgroundBrush", "i"),
        };
        ResultDot.Background = (Brush)FindResource(brush);
        ResultBanner.Background = (Brush)FindResource(background);
        ResultSymbol.Text = symbol;
        ResultText.Text = outcome.Message;
        ResultBanner.Visibility = Visibility.Visible;
        RunningPanel.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            CancelButton.IsEnabled = false;
            AppendLine("Cancelling…");
            _cts.Cancel();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CommandText.Text + Environment.NewLine + OutputBox.Text);
        }
        catch
        {
            // Clipboard can be locked by another process
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_running)
        {
            var answer = MessageBox.Show(this,
                "win-acme is still running. Cancel the operation?\n\nCancelling in the middle of an order is safe, " +
                "but the renewal may show a failed run afterwards.",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                _closeWhenDone = true;
                _cts.Cancel();
            }
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
