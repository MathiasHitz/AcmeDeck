using System.Collections.ObjectModel;
using System.Windows;
using AcmeDeck.Infrastructure;
using AcmeDeck.Services;
using Microsoft.Win32;

namespace AcmeDeck.ViewModels;

public sealed record SecurityOption(SmtpSecurity Value, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ScheduledTaskInfo? _task;
    private WacsEnvironment _environment;
    private string? _wacsPath;
    private string? _contactEmail;
    private bool _onlineChecks;
    private string? _smtpServer;
    private int _smtpPort;
    private string? _smtpUser;
    private SmtpSecurity _security;
    private string? _senderName;
    private string? _senderAddress;
    private string? _receivers;
    private bool _emailOnSuccess;
    private string? _computerName;
    private string? _storedPassword;
    private string _testStatus = "";

    public SettingsViewModel(WacsEnvironment environment, AppSettings settings, ScheduledTaskInfo? task)
    {
        _environment = environment;
        _settings = settings;
        _task = task;
        _wacsPath = settings.WacsPath ?? environment.WacsPath;
        _contactEmail = settings.ContactEmail;
        _onlineChecks = settings.OnlineChecks;
        LoadNotification();

        BrowseCommand = new RelayCommand(Browse);
        SaveCommand = new RelayCommand(Save);
        SendTestCommand = new AsyncRelayCommand(SendTestAsync, () => NotificationAvailable);
        OpenWacsFolderCommand = new RelayCommand(() => MainViewModel.OpenInExplorer(_environment.WacsFolder));
        OpenConfigFolderCommand = new RelayCommand(() => MainViewModel.OpenInExplorer(_environment.ConfigurationPath));
        OpenSettingsFileCommand = new RelayCommand(() => MainViewModel.OpenInExplorer(SettingsFilePath));
    }

    /// <summary>True after something was saved, so the main window reloads.</summary>
    public bool Changed { get; private set; }

    public string? Password { get; set; }
    public bool PasswordTouched { get; set; }
    public event Action? Saved;

    public static IReadOnlyList<SecurityOption> SecurityOptions { get; } =
    [
        new(SmtpSecurity.None, "None (plain SMTP)"),
        new(SmtpSecurity.Automatic, "Automatic, based on the port"),
        new(SmtpSecurity.StartTlsRequired, "STARTTLS, required (usually port 587)"),
        new(SmtpSecurity.StartTlsWhenAvailable, "STARTTLS when available"),
        new(SmtpSecurity.ImplicitTls, "Implicit TLS (usually port 465)"),
    ];

    // --- win-acme -----------------------------------------------------------

    public string? WacsPath
    {
        get => _wacsPath;
        set
        {
            if (SetProperty(ref _wacsPath, value))
            {
                _environment = WacsEnvironment.Load(File.Exists(value) ? value : null);
                OnPropertyChanged(nameof(WacsInfo));
                OnPropertyChanged(nameof(ConfigurationPath));
                OnPropertyChanged(nameof(LogPath));
                OnPropertyChanged(nameof(SettingsFilePath));
                OnPropertyChanged(nameof(NotificationAvailable));
                OnPropertyChanged(nameof(LocationWarning));
                LoadNotification();
            }
        }
    }

    public string WacsInfo => _environment.WacsFound
        ? $"Version {_environment.Version ?? "unknown"}"
        : "wacs.exe not found at this location";

    public string? LocationWarning => _environment.IsInUserProfile
        ? "This location is inside a user profile. If the folder is moved or cleaned up, automatic renewal stops. " +
          @"Recommended: C:\Program Files\win-acme, then repair the scheduled task on the main window."
        : null;

    public string ConfigurationPath => _environment.ConfigurationPath;
    public string LogPath => _environment.LogPath;

    /// <summary>
    /// settings.json next to wacs.exe. win-acme creates it from settings_default.json
    /// on first start; we do the same when saving before win-acme has ever run.
    /// </summary>
    public string? SettingsFilePath => _environment.WacsFolder == null ? null : Path.Combine(_environment.WacsFolder, "settings.json");

    public string? ContactEmail
    {
        get => _contactEmail;
        set => SetProperty(ref _contactEmail, value);
    }

    public bool OnlineChecks
    {
        get => _onlineChecks;
        set => SetProperty(ref _onlineChecks, value);
    }

    // --- Notification -------------------------------------------------------

    public bool NotificationAvailable => _environment.WacsFound;

    public string? SmtpServer { get => _smtpServer; set => SetProperty(ref _smtpServer, value); }
    public int SmtpPort { get => _smtpPort; set => SetProperty(ref _smtpPort, value); }
    public string? SmtpUser { get => _smtpUser; set => SetProperty(ref _smtpUser, value); }

    public SmtpSecurity Security
    {
        get => _security;
        set
        {
            if (SetProperty(ref _security, value))
            {
                // Help with the usual port for the chosen mode
                if (value == SmtpSecurity.ImplicitTls && SmtpPort is 25 or 587)
                {
                    SmtpPort = 465;
                }
                else if (value is SmtpSecurity.StartTlsRequired or SmtpSecurity.StartTlsWhenAvailable && SmtpPort is 25 or 465)
                {
                    SmtpPort = 587;
                }
            }
        }
    }

    public string? SenderName { get => _senderName; set => SetProperty(ref _senderName, value); }
    public string? SenderAddress { get => _senderAddress; set => SetProperty(ref _senderAddress, value); }
    public string? Receivers { get => _receivers; set => SetProperty(ref _receivers, value); }
    public bool EmailOnSuccess { get => _emailOnSuccess; set => SetProperty(ref _emailOnSuccess, value); }
    public string? ComputerName { get => _computerName; set => SetProperty(ref _computerName, value); }

    public bool PasswordIsVaultReference => _storedPassword?.StartsWith("vault://", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasStoredPassword => !string.IsNullOrEmpty(_storedPassword);

    public string PasswordHint => PasswordIsVaultReference
        ? $"Stored in the win-acme secret vault ({_storedPassword}). Leave empty to keep it."
        : HasStoredPassword ? "A password is stored. Leave empty to keep it." : "";

    public string TestStatus
    {
        get => _testStatus;
        private set => SetProperty(ref _testStatus, value);
    }

    // --- Scheduled task -----------------------------------------------------

    public ObservableCollection<KeyValuePair<string, string>> TaskDetails { get; } = [];

    public string TaskSummary => _task == null
        ? $"No scheduled task found (expected: '{_environment.ExpectedTaskName}')."
        : $"{_task.FullName}";

    public RelayCommand BrowseCommand { get; }
    public RelayCommand SaveCommand { get; }
    public AsyncRelayCommand SendTestCommand { get; }
    public RelayCommand OpenWacsFolderCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand OpenSettingsFileCommand { get; }

    private void LoadNotification()
    {
        var file = SettingsFilePath;
        var source = file != null && File.Exists(file) ? file : DefaultSettingsFile();
        var n = new NotificationSettings();
        if (source != null)
        {
            try
            {
                n = NotificationSettings.Load(source);
            }
            catch (Exception ex)
            {
                TestStatus = "settings.json could not be read: " + ex.Message;
            }
        }
        _smtpServer = n.SmtpServer;
        _smtpPort = n.SmtpPort;
        _smtpUser = n.SmtpUser;
        _security = n.Security;
        _senderName = n.SenderName;
        _senderAddress = n.SenderAddress;
        _receivers = string.Join("; ", n.ReceiverAddresses);
        _emailOnSuccess = n.EmailOnSuccess;
        _computerName = n.ComputerName;
        _storedPassword = n.SmtpPassword;
        foreach (var name in new[]
                 {
                     nameof(SmtpServer), nameof(SmtpPort), nameof(SmtpUser), nameof(Security), nameof(SenderName),
                     nameof(SenderAddress), nameof(Receivers), nameof(EmailOnSuccess), nameof(ComputerName),
                     nameof(PasswordHint), nameof(PasswordIsVaultReference), nameof(HasStoredPassword)
                 })
        {
            OnPropertyChanged(name);
        }

        TaskDetails.Clear();
        if (_task != null)
        {
            TaskDetails.Add(new("State", $"{_task.State}{(_task.Enabled ? "" : " (disabled)")}"));
            TaskDetails.Add(new("Program", _task.ExecutablePath ?? "-"));
            TaskDetails.Add(new("Arguments", _task.Arguments ?? "-"));
            TaskDetails.Add(new("Runs as", _task.RunAs ?? "-"));
            TaskDetails.Add(new("Last run", _task.LastRun?.ToString("dd.MM.yyyy HH:mm") ?? "never"));
            TaskDetails.Add(new("Last result", _task.LastResultText));
            TaskDetails.Add(new("Next run", _task.NextRun?.ToString("dd.MM.yyyy HH:mm") ?? "-"));
        }
    }

    private string? DefaultSettingsFile()
    {
        var folder = _environment.WacsFolder;
        if (folder == null)
        {
            return null;
        }
        var file = Path.Combine(folder, "settings_default.json");
        return File.Exists(file) ? file : null;
    }

    private NotificationSettings BuildNotification() => new()
    {
        SmtpServer = SmtpServer,
        SmtpPort = SmtpPort,
        SmtpUser = SmtpUser,
        SmtpPassword = PasswordTouched && !string.IsNullOrEmpty(Password) ? Password : _storedPassword,
        Security = Security,
        SenderName = SenderName,
        SenderAddress = SenderAddress,
        ReceiverAddresses = (Receivers ?? "")
            .Split([';', ',', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        EmailOnSuccess = EmailOnSuccess,
        ComputerName = ComputerName,
    };

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate wacs.exe",
            Filter = "win-acme|wacs.exe|Programs|*.exe",
            InitialDirectory = _environment.WacsFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog() == true)
        {
            WacsPath = dialog.FileName;
        }
    }

    private void Save()
    {
        try
        {
            _settings.WacsPath = string.IsNullOrWhiteSpace(WacsPath) ? null : WacsPath;
            _settings.ContactEmail = string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail.Trim();
            _settings.OnlineChecks = OnlineChecks;
            _settings.Save();

            var file = SettingsFilePath;
            if (file != null && _environment.WacsFound)
            {
                if (!File.Exists(file))
                {
                    var template = DefaultSettingsFile() ?? throw new FileNotFoundException(
                        "Neither settings.json nor settings_default.json exists next to wacs.exe. Start win-acme once, then try again.");
                    File.Copy(template, file);
                }
                BuildNotification().Save(file);
                _storedPassword = BuildNotification().SmtpPassword;
                PasswordTouched = false;
                Password = null;
            }
            Changed = true;
            TestStatus = "Saved.";
            OnPropertyChanged(nameof(PasswordHint));
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, App.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SendTestAsync()
    {
        TestStatus = "Sending…";
        try
        {
            await SmtpTester.SendAsync(BuildNotification(), PasswordTouched && !string.IsNullOrEmpty(Password) ? Password : null);
            TestStatus = "Test message sent. If it does not arrive, check the spam folder and the 'Other' tab of a focused inbox.";
        }
        catch (Exception ex)
        {
            TestStatus = "Failed: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }
}
