using System.Collections.ObjectModel;
using System.Windows;
using AcmeDeck.Infrastructure;
using AcmeDeck.Models;
using AcmeDeck.Services;
using AcmeDeck.Views;

namespace AcmeDeck.ViewModels;

public sealed class HostOption : ObservableObject
{
    private bool _isSelected;

    public required string Host { get; init; }
    public required string Protocols { get; init; }
    public string? InvalidReason { get; init; }
    public string? CoveredBy { get; init; }
    public bool IsValid => InvalidReason == null;

    public string Note => InvalidReason ?? (CoveredBy != null ? "Already in: " + CoveredBy : "");

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value && IsValid))
            {
                SelectionChanged?.Invoke();
            }
        }
    }

    public Action? SelectionChanged { get; set; }
}

/// <summary>
/// Four-step wizard: site, host names, checks, options. Used for new
/// certificates, for changing the host names of an existing renewal and for
/// taking over a binding whose certificate comes from another tool.
/// </summary>
public sealed class NewCertificateViewModel : ObservableObject
{
    public static readonly string[] StepNames = ["Site", "Host names", "Checks", "Options"];

    private readonly WacsEnvironment _environment;
    private readonly AppSettings _settings;
    private readonly List<CertificateItemViewModel> _existing;
    private readonly CertificateItemViewModel? _edit;
    private readonly BindingItemViewModel? _takeOver;
    private int _step;
    private IisSite? _selectedSite;
    private string? _commonName;
    private string? _contactEmail;
    private string? _friendlyName;
    private ValidationMethod _validation = ValidationMethod.SelfHosting;
    private bool _acceptTerms;
    private bool _deletePrevious = true;
    private bool _isChecking;

    public NewCertificateViewModel(WacsEnvironment environment, AppSettings settings, List<IisSite> sites,
        List<CertificateItemViewModel> existing, CertificateItemViewModel? edit, BindingItemViewModel? takeOver)
    {
        _environment = environment;
        _settings = settings;
        _existing = existing;
        _edit = edit;
        _takeOver = takeOver;
        Sites = sites.Where(s => s.Bindings.Any(b => b.IsWeb)).ToList();
        _contactEmail = settings.ContactEmail;

        BackCommand = new RelayCommand(() => Step--, () => Step > 0);
        NextCommand = new AsyncRelayCommand(NextAsync, CanGoNext);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => Step == 3 && CanCreate);
        StagingTestCommand = new AsyncRelayCommand(StagingTestAsync, () => SelectedHosts.Count > 0);
        RecheckCommand = new AsyncRelayCommand(RunChecksAsync, () => !IsChecking);

        if (edit != null)
        {
            Title = $"Change host names · {edit.Name}";
            _validation = string.Equals(edit.Renewal.ValidationPlugin, PluginCatalog.ValidationFileSystem, StringComparison.OrdinalIgnoreCase)
                ? ValidationMethod.FileSystem
                : ValidationMethod.SelfHosting;
            SelectedSite = Sites.FirstOrDefault(s => edit.Renewal.SiteIds.Contains(s.Id))
                           ?? Sites.FirstOrDefault(s => s.HostNames.Intersect(edit.EffectiveHosts, StringComparer.OrdinalIgnoreCase).Any());
            Preselect(edit.EffectiveHosts);
            _step = SelectedSite != null ? 1 : 0;
        }
        else if (takeOver != null)
        {
            Title = $"Take over · {takeOver.Host}";
            SelectedSite = Sites.FirstOrDefault(s => s.Id == takeOver.Site.Id);
            var names = new List<string> { takeOver.Binding.Host };
            names.AddRange(takeOver.Certificate?.DnsNames ?? []);
            Preselect(names);
            _step = SelectedSite != null ? 1 : 0;
        }
        else
        {
            Title = "New certificate";
            if (Sites.Count == 1)
            {
                SelectedSite = Sites[0];
            }
        }
    }

    public string Title { get; }
    public string? CreatedId { get; private set; }
    public Window? Owner { get; set; }
    public event Action<bool>? RequestClose;

    public List<IisSite> Sites { get; }
    public ObservableCollection<HostOption> HostOptions { get; } = [];
    public ObservableCollection<Issue> CheckResults { get; } = [];

    public bool IsEditMode => _edit != null;
    public bool IsTakeOver => _takeOver != null;
    public string? EditName => _edit?.Name;

    public int Step
    {
        get => _step;
        set
        {
            if (SetProperty(ref _step, Math.Clamp(value, 0, StepNames.Length - 1)))
            {
                OnPropertyChanged(nameof(IsLastStep));
                OnPropertyChanged(nameof(CommandPreview));
            }
        }
    }

    public bool IsLastStep => Step == StepNames.Length - 1;

    public IisSite? SelectedSite
    {
        get => _selectedSite;
        set
        {
            if (SetProperty(ref _selectedSite, value))
            {
                BuildHostOptions();
            }
        }
    }

    public List<string> SelectedHosts => HostOptions.Where(h => h.IsSelected).Select(h => h.Host).ToList();

    public string? CommonName
    {
        get => _commonName;
        set
        {
            SetProperty(ref _commonName, value);
            OnPropertyChanged(nameof(CommandPreview));
        }
    }

    public string? ContactEmail
    {
        get => _contactEmail;
        set
        {
            SetProperty(ref _contactEmail, value);
            OnPropertyChanged(nameof(CommandPreview));
        }
    }

    public string? FriendlyName
    {
        get => _friendlyName;
        set
        {
            SetProperty(ref _friendlyName, value);
            OnPropertyChanged(nameof(CommandPreview));
        }
    }

    public ValidationMethod Validation
    {
        get => _validation;
        set
        {
            SetProperty(ref _validation, value);
            OnPropertyChanged(nameof(CommandPreview));
        }
    }

    public bool AcceptTerms
    {
        get => _acceptTerms;
        set => SetProperty(ref _acceptTerms, value);
    }

    public bool DeletePrevious
    {
        get => _deletePrevious;
        set => SetProperty(ref _deletePrevious, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetProperty(ref _isChecking, value);
    }

    public bool HasFileSystemPath => !string.IsNullOrWhiteSpace(SelectedSite?.PhysicalPath);

    public string CommandPreview
    {
        get
        {
            if (SelectedSite == null || SelectedHosts.Count == 0 || _environment.WacsPath == null)
            {
                return "";
            }
            return WacsRunner.FormatCommandLine(_environment.WacsPath, WacsCommands.Create(BuildRequest(), "<new id>"));
        }
    }

    private bool CanCreate =>
        SelectedSite != null && SelectedHosts.Count > 0 && AcceptTerms &&
        (Validation != ValidationMethod.FileSystem || HasFileSystemPath);

    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand StagingTestCommand { get; }
    public AsyncRelayCommand RecheckCommand { get; }

    private bool CanGoNext() => Step switch
    {
        0 => SelectedSite != null,
        1 => SelectedHosts.Count > 0,
        2 => !IsChecking,
        _ => false
    };

    private async Task NextAsync()
    {
        Step++;
        if (Step == 2)
        {
            await RunChecksAsync();
        }
    }

    private void Preselect(IEnumerable<string> hosts)
    {
        var wanted = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
        foreach (var option in HostOptions)
        {
            option.IsSelected = option.IsValid && wanted.Contains(option.Host);
        }
        UpdateCommonName();
    }

    private void BuildHostOptions()
    {
        HostOptions.Clear();
        if (SelectedSite == null)
        {
            return;
        }
        var others = _existing.Where(e => e != _edit).ToList();
        foreach (var group in SelectedSite.Bindings.Where(b => b.IsWeb)
                     .GroupBy(b => b.Host, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => string.IsNullOrEmpty(g.Key))
                     .ThenBy(g => g.Key))
        {
            var host = group.Key;
            var coveredBy = others
                .Where(o => o.EffectiveHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ||
                            (o.Certificate?.DnsNames.Contains(host, StringComparer.OrdinalIgnoreCase) ?? false))
                .Select(o => o.Name)
                .ToList();
            var option = new HostOption
            {
                Host = string.IsNullOrEmpty(host) ? "(no host name)" : host,
                Protocols = string.Join(", ", group.Select(b => $"{b.Protocol}:{b.Port}").Distinct()),
                InvalidReason = HostNameRules.WhyInvalid(host),
                CoveredBy = coveredBy.Count > 0 ? string.Join(", ", coveredBy) : null,
            };
            option.SelectionChanged = OnHostSelectionChanged;
            HostOptions.Add(option);
        }
        // New certificate: preselect all valid names that no other renewal covers yet
        if (_edit == null && _takeOver == null)
        {
            foreach (var option in HostOptions.Where(o => o.IsValid && o.CoveredBy == null))
            {
                option.IsSelected = true;
            }
        }
        UpdateCommonName();
        OnPropertyChanged(nameof(HasFileSystemPath));
    }

    private void OnHostSelectionChanged()
    {
        UpdateCommonName();
        OnPropertyChanged(nameof(SelectedHosts));
        OnPropertyChanged(nameof(CommandPreview));
    }

    private void UpdateCommonName()
    {
        var hosts = SelectedHosts;
        if (CommonName == null || !hosts.Contains(CommonName, StringComparer.OrdinalIgnoreCase))
        {
            CommonName = hosts.FirstOrDefault(h => !h.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) ?? hosts.FirstOrDefault();
        }
        OnPropertyChanged(nameof(SelectedHosts));
    }

    private NewCertificateRequest BuildRequest() => new()
    {
        SiteId = SelectedSite!.Id,
        Hosts = SelectedHosts,
        CommonName = SelectedHosts.Count > 1 ? CommonName : null,
        FriendlyName = string.IsNullOrWhiteSpace(FriendlyName) ? null : FriendlyName.Trim(),
        Validation = Validation,
        ContactEmail = string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail.Trim(),
    };

    private async Task RunChecksAsync()
    {
        IsChecking = true;
        CheckResults.Clear();
        try
        {
            var hosts = SelectedHosts;

            foreach (var option in HostOptions.Where(o => o.IsSelected && o.CoveredBy != null))
            {
                CheckResults.Add(new Issue(CheckLevel.Warning,
                    $"{option.Host} is already part of '{option.CoveredBy}'. Both renewals would overwrite each other's " +
                    "binding. Delete the other renewal after this certificate has been issued."));
            }

            if (Validation == ValidationMethod.SelfHosting)
            {
                CheckResults.Add(new Issue(CheckLevel.Info,
                    "Let's Encrypt connects to port 80 of each name from several locations worldwide. Port 80 must be " +
                    "reachable from any source address, a firewall that only allows certain countries or IPs will fail."));
            }
            else if (!HasFileSystemPath)
            {
                CheckResults.Add(new Issue(CheckLevel.Error, "The site has no physical path, FileSystem validation is not possible."));
            }

            if (!_settings.OnlineChecks)
            {
                CheckResults.Add(new Issue(CheckLevel.Info, "Public DNS checks are disabled in the settings."));
                return;
            }

            var serverIp = await OnlineChecks.GetPublicIPv4Async();
            if (serverIp != null)
            {
                CheckResults.Add(new Issue(CheckLevel.Info, $"This server reaches the internet as {serverIp}."));
            }
            var results = await Task.WhenAll(hosts.Select(h => OnlineChecks.CheckHostAsync(h, serverIp)));
            foreach (var r in results)
            {
                CheckResults.Add(new Issue(r.Level, $"{r.Host}: {r.Message}"));
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private Task StagingTestAsync()
    {
        if (SelectedSite == null || _environment.WacsPath == null)
        {
            return Task.CompletedTask;
        }
        var request = BuildRequest();
        var id = WacsCommands.NewId();
        var env = _environment;
        RunWindow.Run(Owner, "Test against Let's Encrypt staging", env.WacsPath, async ctx =>
        {
            ctx.Log("Test run: certificates from the staging environment are not trusted and are neither stored nor bound.");
            ctx.Log("");
            var result = await ctx.WacsAsync(WacsCommands.CreateStagingTest(request, id, env.StagingBaseUri));
            var stagingPath = env.ConfigurationPathFor(env.StagingBaseUri);
            var renewal = RenewalStore.Find(stagingPath, id);
            var last = renewal?.History.LastOrDefault();
            if (renewal != null)
            {
                ctx.Log("");
                ctx.Log("Removing the test renewal…");
                await ctx.WacsAsync(WacsCommands.CancelStaging(id, env.StagingBaseUri));
            }
            if (last?.Success == true)
            {
                return new RunOutcome(true, "Validation works. The real certificate can be requested now.");
            }
            var errors = last?.AllErrors.ToList() ?? [];
            return new RunOutcome(false, errors.Count > 0
                ? "The test failed: " + string.Join(" | ", errors)
                : $"The test failed (exit code {result.ExitCode}). See the output above.");
        });
        return Task.CompletedTask;
    }

    private Task CreateAsync()
    {
        if (SelectedSite == null || _environment.WacsPath == null)
        {
            return Task.CompletedTask;
        }
        var request = BuildRequest();
        var id = WacsCommands.NewId();
        var env = _environment;
        var previous = _edit != null && DeletePrevious ? _edit.Renewal.Id : null;

        var outcome = RunWindow.Run(Owner, Title, env.WacsPath, async ctx =>
        {
            var result = await ctx.WacsAsync(WacsCommands.Create(request, id));
            var created = MainViewModel.Evaluate(env, id, 0, result, "The certificate was issued and installed in IIS.");
            if (created.Success && previous != null)
            {
                ctx.Log("");
                ctx.Log("Removing the previous renewal…");
                await ctx.WacsAsync(WacsCommands.Cancel(previous));
                created = created with { Message = created.Message + " The previous renewal was removed." };
            }
            return created;
        });

        if (outcome.Success)
        {
            CreatedId = id;
            if (string.IsNullOrWhiteSpace(_settings.ContactEmail) && !string.IsNullOrWhiteSpace(request.ContactEmail))
            {
                _settings.ContactEmail = request.ContactEmail;
                _settings.Save();
            }
            RequestClose?.Invoke(true);
        }
        return Task.CompletedTask;
    }
}
