using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AcmeDeck.Infrastructure;
using AcmeDeck.Models;
using AcmeDeck.Services;
using AcmeDeck.Views;

namespace AcmeDeck.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Window _owner;
    private AppSettings _settings = AppSettings.Load();
    private WacsEnvironment _environment = WacsEnvironment.Load(null);
    private List<IisSite> _sites = [];
    private ScheduledTaskInfo? _task;
    private CertificateItemViewModel? _selected;
    private bool _isBusy;
    private string _statusText = "";
    private int _selectedTab;

    public MainViewModel(Window owner)
    {
        _owner = owner;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        NewCertificateCommand = new AsyncRelayCommand(() => NewCertificateAsync(null, null), () => _environment.WacsFound);
        EditCommand = new AsyncRelayCommand(() => NewCertificateAsync(Selected, null), () => Selected?.CanEdit == true && _environment.WacsFound);
        RenewCommand = new AsyncRelayCommand(RenewAsync, () => Selected != null && _environment.WacsFound);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => Selected != null && _environment.WacsFound);
        RevokeCommand = new AsyncRelayCommand(RevokeAsync, () => Selected?.Renewal.LastThumbprint != null && _environment.WacsFound);
        CheckBindingsCommand = new AsyncRelayCommand(() => CheckBindingsAsync(Selected), () => Selected != null);
        CheckDnsCommand = new AsyncRelayCommand(CheckDnsAsync, () => Selected != null);
        TakeOverCommand = new AsyncRelayCommand(p => NewCertificateAsync(null, p as BindingItemViewModel),
            p => p is BindingItemViewModel { CanTakeOver: true } && _environment.WacsFound);
        CheckAllBindingsCommand = new AsyncRelayCommand(CheckAllBindingsAsync);
        RepairTaskCommand = new AsyncRelayCommand(RepairTaskAsync, () => _environment.WacsFound);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        OpenLogsCommand = new RelayCommand(OpenLogs, () => Directory.Exists(_environment.LogPath));
        OpenRenewalFileCommand = new RelayCommand(() => OpenInExplorer(Selected?.Renewal.FilePath), () => Selected != null);
        OpenAboutCommand = new RelayCommand(() => new AboutWindow { Owner = _owner }.ShowDialog());
    }

    public ObservableCollection<CertificateItemViewModel> Certificates { get; } = [];
    public ObservableCollection<BindingItemViewModel> Bindings { get; } = [];
    public ObservableCollection<Issue> Health { get; } = [];

    public CertificateItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value != null && value.BindingChecks.Count == 0)
            {
                _ = CheckBindingsAsync(value);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value) && value == 1 && Bindings.Any(b => b.Certificate == null))
            {
                _ = CheckAllBindingsAsync();
            }
        }
    }

    public string EnvironmentText => _environment.WacsFound
        ? $"win-acme {_environment.Version} · {_environment.BaseUri.Replace("https://", "").TrimEnd('/')}"
        : "win-acme not found";

    public string TaskText => _task == null
        ? "Scheduled task: not found"
        : $"Scheduled task: {_task.State}, last run {(_task.LastRun?.ToString("dd.MM.yyyy HH:mm") ?? "never")} ({_task.LastResultText}), next {(_task.NextRun?.ToString("dd.MM.yyyy HH:mm") ?? "-")}";

    public string ConfigText => _environment.ConfigurationPath;

    public bool HasCertificates => Certificates.Count > 0;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NewCertificateCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand RenewCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand RevokeCommand { get; }
    public AsyncRelayCommand CheckBindingsCommand { get; }
    public AsyncRelayCommand CheckDnsCommand { get; }
    public AsyncRelayCommand TakeOverCommand { get; }
    public AsyncRelayCommand CheckAllBindingsCommand { get; }
    public AsyncRelayCommand RepairTaskCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenRenewalFileCommand { get; }
    public RelayCommand OpenAboutCommand { get; }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Loading…";
        try
        {
            _settings = AppSettings.Load();

            List<ScheduledTaskInfo> tasks = [];
            string? taskError = null;
            try
            {
                tasks = await ScheduledTaskReader.FindWinAcmeTasksAsync();
            }
            catch (Exception ex)
            {
                taskError = ex.Message;
            }

            var wacsPath = WacsEnvironment.Locate(_settings.WacsPath, tasks);
            _environment = WacsEnvironment.Load(wacsPath);
            _task = tasks.FirstOrDefault(t => string.Equals(t.Name, _environment.ExpectedTaskName, StringComparison.OrdinalIgnoreCase))
                    ?? tasks.FirstOrDefault(t => string.Equals(t.ExecutablePath, wacsPath, StringComparison.OrdinalIgnoreCase));

            string? iisError = null;
            try
            {
                _sites = await Task.Run(IisConfigReader.ReadSites);
            }
            catch (Exception ex)
            {
                _sites = [];
                iisError = ex.Message;
            }

            var environment = _environment;
            var sites = _sites;
            var load = await Task.Run(() => RenewalStore.Load(environment.ConfigurationPath));
            var items = await Task.Run(() => load.Renewals.Select(r => new CertificateItemViewModel(r, sites, environment.RenewalDays)).ToList());

            NotificationSettings? notification = null;
            try
            {
                if (_environment.SettingsFile != null)
                {
                    notification = NotificationSettings.Load(_environment.SettingsFile);
                }
            }
            catch
            {
                // reported as "not configured"
            }

            var selectedId = Selected?.Renewal.Id;
            Certificates.Clear();
            foreach (var item in items)
            {
                Certificates.Add(item);
            }
            Selected = Certificates.FirstOrDefault(c => c.Renewal.Id == selectedId) ?? Certificates.FirstOrDefault();

            Bindings.Clear();
            foreach (var site in _sites)
            {
                foreach (var binding in site.Bindings.Where(b => b.IsHttps))
                {
                    Bindings.Add(new BindingItemViewModel(site, binding));
                }
            }

            BuildHealth(tasks, taskError, iisError, load.Errors, notification);
            OnPropertyChanged(nameof(EnvironmentText));
            OnPropertyChanged(nameof(TaskText));
            OnPropertyChanged(nameof(ConfigText));
            OnPropertyChanged(nameof(HasCertificates));
            StatusText = $"{Certificates.Count} certificate{(Certificates.Count == 1 ? "" : "s")} · refreshed {DateTime.Now:HH:mm:ss}";

            // Check every certificate's bindings right away, so a certificate that
            // was issued but never reached IIS shows up in the list, not only when selected
            foreach (var item in Certificates.ToList())
            {
                _ = CheckBindingsAsync(item);
            }
            if (SelectedTab == 1)
            {
                _ = CheckAllBindingsAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildHealth(List<ScheduledTaskInfo> tasks, string? taskError, string? iisError,
        List<string> loadErrors, NotificationSettings? notification)
    {
        Health.Clear();
        var env = _environment;

        if (!env.WacsFound)
        {
            Health.Add(new Issue(CheckLevel.Error,
                "win-acme (wacs.exe) was not found. Set its location in the settings.",
                "Settings", OpenSettingsCommand));
            return;
        }
        if (!IisConfigReader.IisInstalled)
        {
            Health.Add(new Issue(CheckLevel.Error, "IIS is not installed on this computer."));
        }
        else if (iisError != null)
        {
            Health.Add(new Issue(CheckLevel.Error, "The IIS configuration could not be read: " + iisError));
        }

        if (env.IsInUserProfile)
        {
            Health.Add(new Issue(CheckLevel.Warning,
                $"win-acme runs from a user profile folder ({env.WacsFolder}). The scheduled task points to this path: " +
                @"if the folder is moved or cleaned up, all renewals stop without notice. Move it to C:\Program Files\win-acme, " +
                "select the new location in the settings and repair the scheduled task.",
                "Settings", OpenSettingsCommand));
        }

        if (taskError != null)
        {
            Health.Add(new Issue(CheckLevel.Warning, "The scheduled task could not be checked: " + taskError));
        }
        else if (_task == null)
        {
            Health.Add(new Issue(Certificates.Count > 0 ? CheckLevel.Error : CheckLevel.Info,
                $"No scheduled task '{env.ExpectedTaskName}' found. Certificates are not renewed automatically.",
                "Create task", RepairTaskCommand));
        }
        else
        {
            if (!string.Equals(_task.ExecutablePath, env.WacsPath, StringComparison.OrdinalIgnoreCase))
            {
                var missing = _task.ExecutablePath == null || !File.Exists(_task.ExecutablePath);
                Health.Add(new Issue(missing ? CheckLevel.Error : CheckLevel.Warning,
                    $"The scheduled task starts {_task.ExecutablePath ?? "nothing"}" +
                    (missing ? ", which does not exist. Automatic renewal is broken." : $", not {env.WacsPath}."),
                    "Repair task", RepairTaskCommand));
            }
            if (!_task.Enabled)
            {
                Health.Add(new Issue(CheckLevel.Error, "The scheduled task is disabled. Certificates are not renewed automatically."));
            }
            else if (_task.LastRunFailed)
            {
                Health.Add(new Issue(CheckLevel.Warning,
                    $"The last scheduled run on {_task.LastRun:dd.MM.yyyy HH:mm} ended with {_task.LastResultText}. Check the log.",
                    "Open logs", OpenLogsCommand));
            }
        }
        foreach (var other in tasks.Where(t => t != _task && t.Name.Contains(" renew (") && !t.Name.Contains("staging", StringComparison.OrdinalIgnoreCase)))
        {
            Health.Add(new Issue(CheckLevel.Info, $"Another win-acme task exists: {other.FullName} ({other.ExecutablePath})."));
        }

        if (notification == null || !notification.IsConfigured)
        {
            Health.Add(new Issue(CheckLevel.Warning,
                "No e-mail notification is configured. A failing renewal would go unnoticed until the certificate expires.",
                "Configure", OpenSettingsCommand));
        }

        foreach (var error in loadErrors)
        {
            Health.Add(new Issue(CheckLevel.Warning, "Renewal file could not be read: " + error));
        }

        // Several renewals managing the same certificate
        foreach (var group in Certificates.Where(c => c.Renewal.LastThumbprint != null)
                     .GroupBy(c => c.Renewal.LastThumbprint, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            Health.Add(new Issue(CheckLevel.Warning,
                $"{string.Join(" and ", group.Select(c => $"'{c.Name}'"))} manage the same certificate. Keep one and delete the others."));
        }

        // Host names requested by more than one renewal
        var overlaps = Certificates
            .SelectMany(c => c.EffectiveHosts.Select(h => (Host: h.ToLowerInvariant(), Item: c)))
            .GroupBy(x => x.Host)
            .Where(g => g.Select(x => x.Item).Distinct().Count() > 1)
            .ToList();
        foreach (var overlap in overlaps)
        {
            Health.Add(new Issue(CheckLevel.Warning,
                $"'{overlap.Key}' is covered by several renewals ({string.Join(", ", overlap.Select(x => x.Item.Name).Distinct())}). " +
                "They will overwrite each other's binding."));
        }
    }

    private async Task CheckBindingsAsync(CertificateItemViewModel? item)
    {
        if (item == null || item.BindingCheckRunning)
        {
            return;
        }
        item.BindingCheckRunning = true;
        try
        {
            item.BindingChecks.Clear();
            var names = new HashSet<string>(item.Certificate?.DnsNames ?? item.EffectiveHosts, StringComparer.OrdinalIgnoreCase);
            var bindings = item.Sites
                .SelectMany(s => s.Bindings)
                .Where(b => b.IsHttps && names.Contains(b.Host))
                .ToList();
            if (bindings.Count == 0)
            {
                item.BindingChecks.Add(new Issue(CheckLevel.Warning, "No HTTPS binding in IIS uses these host names."));
                return;
            }
            var expected = item.Renewal.LastThumbprint;
            item.BindingChecks.Add(new Issue(CheckLevel.Info, $"Checking {bindings.Count} binding{(bindings.Count == 1 ? "" : "s")}…"));
            var results = await Task.WhenAll(bindings.Select(b =>
                TlsProbe.ProbeAsync(b.IpAddress, b.Port, b.Host, TimeSpan.FromSeconds(5))));
            item.BindingChecks.Clear();
            var mismatch = false;
            foreach (var (binding, result) in bindings.Zip(results))
            {
                var ccs = binding.UsesCentralCertStore ? " The binding uses the Central Certificate Store, see the note above." : "";
                if (result.Certificate == null)
                {
                    item.BindingChecks.Add(new Issue(CheckLevel.Error, $"{binding.Display}: no answer ({result.Error})."));
                }
                else if (expected != null && string.Equals(result.Certificate.Thumbprint, expected, StringComparison.OrdinalIgnoreCase))
                {
                    item.BindingChecks.Add(new Issue(CheckLevel.Ok,
                        $"{binding.Display} serves the current certificate (valid until {result.Certificate.NotAfter:dd.MM.yyyy})."));
                }
                else
                {
                    mismatch = expected != null;
                    item.BindingChecks.Add(new Issue(CheckLevel.Error,
                        $"{binding.Display} serves a different certificate: {result.Certificate.Issuer}, " +
                        $"valid until {result.Certificate.NotAfter:dd.MM.yyyy}, thumbprint {result.Certificate.Thumbprint}.{ccs}"));
                }
            }
            item.BindingMismatch = mismatch;
        }
        finally
        {
            item.BindingCheckRunning = false;
        }
    }

    private async Task CheckDnsAsync()
    {
        var item = Selected;
        if (item == null)
        {
            return;
        }
        item.DnsChecks.Clear();
        if (!_settings.OnlineChecks)
        {
            item.DnsChecks.Add(new Issue(CheckLevel.Info, "Online checks are disabled in the settings."));
            return;
        }
        var hosts = (item.EffectiveHosts.Count > 0 ? item.EffectiveHosts : item.Certificate?.DnsNames ?? [])
            .Where(h => HostNameRules.WhyInvalid(h) == null)
            .ToList();
        if (hosts.Count == 0)
        {
            item.DnsChecks.Add(new Issue(CheckLevel.Info, "No public host names to check."));
            return;
        }
        item.DnsChecks.Add(new Issue(CheckLevel.Info, "Checking public DNS…"));
        var serverIp = await OnlineChecks.GetPublicIPv4Async();
        var results = await Task.WhenAll(hosts.Select(h => OnlineChecks.CheckHostAsync(h, serverIp)));
        item.DnsChecks.Clear();
        foreach (var r in results)
        {
            item.DnsChecks.Add(new Issue(r.Level, $"{r.Host}: {r.Message}"));
        }
    }

    private async Task CheckAllBindingsAsync()
    {
        var certificates = Certificates.ToList();
        // Refresh both views: the binding list and the per-certificate status
        await Task.WhenAll(Bindings.ToList().Select(b => b.CheckAsync(certificates))
            .Concat(certificates.Select(CheckBindingsAsync)));
    }

    private async Task NewCertificateAsync(CertificateItemViewModel? edit, BindingItemViewModel? takeOver)
    {
        if (!_environment.WacsFound)
        {
            return;
        }
        var wizard = new NewCertificateViewModel(_environment, _settings, _sites, Certificates.ToList(), edit, takeOver);
        var window = new NewCertificateWindow(wizard) { Owner = _owner };
        if (window.ShowDialog() == true)
        {
            await RefreshAsync();
            Selected = Certificates.FirstOrDefault(c => c.Renewal.Id == wizard.CreatedId) ?? Selected;
            SelectedTab = 0;
            if (takeOver != null)
            {
                MessageBox.Show(_owner,
                    "The certificate is now managed by win-acme.\n\nIf it was created with another tool, disable " +
                    "or delete the renewal there now. Otherwise both tools keep renewing and overwrite each " +
                    "other's binding.",
                    App.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async Task RenewAsync()
    {
        var item = Selected;
        if (item == null)
        {
            return;
        }
        var answer = MessageBox.Show(_owner,
            $"Request a new certificate for '{item.Name}' now?\n\n" +
            "This happens even if the current certificate is still valid. Let's Encrypt allows at most " +
            "5 identical certificates per week, so use this for testing or after changes, not routinely.",
            App.Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var env = _environment;
        var before = item.Renewal.History.Count;
        RunWindow.Run(_owner, $"Renew {item.Name}", env.WacsPath!, async ctx =>
        {
            var result = await ctx.WacsAsync(WacsCommands.Renew(item.Renewal.Id, force: true));
            return Evaluate(env, item.Renewal.Id, before, result, "The certificate was renewed and installed.");
        });
        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        var item = Selected;
        if (item == null)
        {
            return;
        }
        var until = item.Certificate != null ? $" until {item.Certificate.NotAfter:dd.MM.yyyy}" : "";
        var answer = MessageBox.Show(_owner,
            $"Delete the renewal '{item.Name}'?\n\n" +
            $"The current certificate stays installed and keeps working{until}, but it will not be renewed any more. " +
            "IIS bindings and the certificate store are not changed.",
            App.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var env = _environment;
        RunWindow.Run(_owner, $"Delete {item.Name}", env.WacsPath!, async ctx =>
        {
            await ctx.WacsAsync(WacsCommands.Cancel(item.Renewal.Id));
            return File.Exists(item.Renewal.FilePath)
                ? new RunOutcome(false, "The renewal still exists. See the output above.")
                : new RunOutcome(true, "The renewal was deleted.");
        });
        await RefreshAsync();
    }

    private async Task RevokeAsync()
    {
        var item = Selected;
        if (item == null)
        {
            return;
        }
        var answer = MessageBox.Show(_owner,
            $"Revoke the certificate of '{item.Name}'?\n\n" +
            $"The certificate becomes invalid immediately. Browsers will show security errors for {item.HostsText} " +
            "until a new certificate is installed.\n\n" +
            "Only do this if the private key was compromised. If you simply no longer need the certificate, use Delete instead.",
            App.Title, MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var env = _environment;
        RunWindow.Run(_owner, $"Revoke {item.Name}", env.WacsPath!, async ctx =>
        {
            var result = await ctx.WacsAsync(WacsCommands.Revoke(item.Renewal.Id));
            return result.ExitCode == 0
                ? new RunOutcome(true, "Revocation was requested. Renew the certificate to get a valid one again.", CheckLevel.Warning)
                : new RunOutcome(false, $"win-acme exited with code {result.ExitCode}.");
        });
        await RefreshAsync();
    }

    private async Task RepairTaskAsync()
    {
        var env = _environment;
        if (env.IsInUserProfile)
        {
            var answer = MessageBox.Show(_owner,
                $"win-acme is located in a user profile ({env.WacsFolder}). The task would point there again.\n\n" +
                "Continue anyway?",
                App.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }
        RunWindow.Run(_owner, "Create or repair the scheduled task", env.WacsPath!, async ctx =>
        {
            var result = await ctx.WacsAsync(WacsCommands.SetupTaskScheduler());
            return result.ExitCode == 0
                ? new RunOutcome(true, "The scheduled task was created or updated.")
                : new RunOutcome(false, $"win-acme exited with code {result.ExitCode}.");
        });
        await RefreshAsync();
    }

    private async Task OpenSettingsAsync()
    {
        var vm = new SettingsViewModel(_environment, _settings, _task);
        var window = new SettingsWindow(vm) { Owner = _owner };
        window.ShowDialog();
        if (vm.Changed)
        {
            await RefreshAsync();
        }
    }

    private void OpenLogs()
    {
        var window = new LogsWindow(new LogsViewModel(_environment.LogPath)) { Owner = _owner };
        window.Show();
    }

    /// <summary>
    /// Judges a run by what win-acme recorded in the renewal history, which is
    /// more reliable than the exit code of the process.
    /// </summary>
    internal static RunOutcome Evaluate(WacsEnvironment env, string id, int historyCountBefore, WacsRunResult result, string successMessage)
    {
        if (result.Cancelled)
        {
            return new RunOutcome(false, "The operation was cancelled.");
        }
        var renewal = RenewalStore.Find(env.ConfigurationPath, id);
        var last = renewal?.History.LastOrDefault();
        if (renewal != null && renewal.History.Count > historyCountBefore && last != null)
        {
            if (last.Success == true)
            {
                return new RunOutcome(true, successMessage);
            }
            var errors = last.AllErrors.ToList();
            return new RunOutcome(false, errors.Count > 0
                ? "win-acme reported: " + string.Join(" | ", errors)
                : "win-acme recorded a failed run. See the output above.");
        }
        return result.ExitCode == 0
            ? new RunOutcome(false, "win-acme finished without recording a result. See the output above.", CheckLevel.Warning)
            : new RunOutcome(false, $"win-acme exited with code {result.ExitCode}. See the output above.");
    }

    internal static void OpenInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Directory.Exists(path))
        {
            Process.Start("explorer.exe", $"\"{path}\"");
        }
    }
}
