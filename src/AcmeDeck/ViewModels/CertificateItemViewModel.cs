using System.Collections.ObjectModel;
using AcmeDeck.Infrastructure;
using AcmeDeck.Models;
using AcmeDeck.Services;

namespace AcmeDeck.ViewModels;

/// <summary>
/// A renewal together with everything we can find out about it: the issued
/// certificate in the store, the IIS sites it reads and its health.
/// </summary>
public sealed class CertificateItemViewModel : ObservableObject
{
    private const int ExpiryWarningDays = 14;

    public CertificateItemViewModel(Renewal renewal, IReadOnlyList<IisSite> sites, int renewalDays)
    {
        Renewal = renewal;
        Certificate = CertificateLookup.Find(renewal.LastThumbprint);
        EffectiveHosts = IisConfigReader.EffectiveHosts(renewal, sites);
        Sites = renewal.SiteIds.Count == 0
            ? sites.ToList()
            : sites.Where(s => renewal.SiteIds.Contains(s.Id)).ToList();
        RenewalDays = renewalDays;
        Issues = BuildIssues();
        _baseLevel = Issues.Count == 0 ? CheckLevel.Ok : Issues.Max(i => i.Level);
        History = new ObservableCollection<HistoryEntry>(renewal.History.AsEnumerable().Reverse());
    }

    private readonly CheckLevel _baseLevel;
    private bool _bindingMismatch;

    public Renewal Renewal { get; }
    public CertificateDetails? Certificate { get; }
    public List<string> EffectiveHosts { get; }
    public List<IisSite> Sites { get; }
    public int RenewalDays { get; }
    public List<Issue> Issues { get; }
    public ObservableCollection<HistoryEntry> History { get; }

    public ObservableCollection<Issue> BindingChecks { get; } = [];
    public ObservableCollection<Issue> DnsChecks { get; } = [];

    /// <summary>Guards against two binding checks filling the list at the same time.</summary>
    internal bool BindingCheckRunning { get; set; }

    /// <summary>
    /// Set by the binding check: IIS serves a different certificate than the one
    /// this renewal issued last. Issuing worked, but the certificate is not in use.
    /// </summary>
    public bool BindingMismatch
    {
        get => _bindingMismatch;
        set
        {
            if (SetProperty(ref _bindingMismatch, value))
            {
                OnPropertyChanged(nameof(Level));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public CheckLevel Level => _bindingMismatch ? CheckLevel.Error : _baseLevel;

    public string Name => Renewal.DisplayName;

    public string StatusText => Level switch
    {
        CheckLevel.Error when Renewal.ConsecutiveErrors > 0 => "Renewal failing",
        CheckLevel.Error when _bindingMismatch => "Not applied",
        CheckLevel.Error when Certificate is { DaysLeft: < 0 } => "Expired",
        CheckLevel.Error => "Problem",
        CheckLevel.Warning when Certificate is { DaysLeft: < ExpiryWarningDays } => "Expires soon",
        CheckLevel.Warning => "Attention",
        _ => "Valid"
    };

    /// <summary>
    /// Names on the certificate if we have it, otherwise what the next order would contain.
    /// </summary>
    public string HostsText
    {
        get
        {
            var names = Certificate?.DnsNames ?? (EffectiveHosts.Count > 0 ? EffectiveHosts : Renewal.IncludeHosts);
            return names.Count == 0 ? "-" : string.Join(", ", names);
        }
    }

    public string SitesText => Renewal.IsIisSource
        ? Renewal.SiteIds.Count == 0 ? "All sites" : string.Join(", ", Sites.Select(s => s.Name).DefaultIfEmpty(string.Join(", ", Renewal.SiteIds)))
        : PluginCatalog.NameOf(Renewal.SourcePlugin);

    public string HostFilterText =>
        !Renewal.IsIisSource ? "-" :
        Renewal.IncludeHosts.Count > 0 ? string.Join(", ", Renewal.IncludeHosts) :
        !string.IsNullOrWhiteSpace(Renewal.IncludePattern) ? "Pattern: " + Renewal.IncludePattern :
        !string.IsNullOrWhiteSpace(Renewal.IncludeRegex) ? "Regex: " + Renewal.IncludeRegex :
        "All bindings of the site";

    public string EffectiveHostsText => EffectiveHosts.Count == 0 ? "-" : string.Join(", ", EffectiveHosts);

    public DateTime? Expires => Certificate?.NotAfter ?? Renewal.HistoryExpireDate;

    public string ExpiresText
    {
        get
        {
            if (Expires is not { } date)
            {
                return "-";
            }
            var days = (int)Math.Floor((date - DateTime.Now).TotalDays);
            return days < 0 ? $"{date:dd.MM.yyyy} (expired)" : $"{date:dd.MM.yyyy} ({days} days)";
        }
    }

    public DateTime? NextRenewal => Renewal.DueDate ?? Renewal.LastSuccess?.Date.AddDays(RenewalDays);

    public string NextRenewalText => NextRenewal is { } d ? (d <= DateTime.Now ? "Due now" : d.ToString("dd.MM.yyyy")) : "-";

    public string LastRunText => Renewal.LastEntry is { } e ? $"{e.Date:dd.MM.yyyy HH:mm} · {e.ResultText}" : "Never";

    public string ValidationText => PluginCatalog.NameOf(Renewal.ValidationPlugin);
    public string StoreText => string.Join(", ", Renewal.StorePlugins.Select(PluginCatalog.NameOf).DefaultIfEmpty("-"));
    public string InstallationText => string.Join(", ", Renewal.InstallationPlugins.Select(PluginCatalog.NameOf).DefaultIfEmpty("-"));
    public string CommandText => WacsCommands.Describe(Renewal);

    public string CertificateNamesText => Certificate == null ? "-" : string.Join(", ", Certificate.DnsNames);

    public string CertificateValidityText => Certificate == null
        ? "-"
        : $"{Certificate.NotBefore:dd.MM.yyyy} – {Certificate.NotAfter:dd.MM.yyyy}";

    public string CertificateLocationText => Certificate == null
        ? (Renewal.LastThumbprint == null ? "No certificate issued yet" : "Not found in the local machine store")
        : $"LocalMachine\\{Certificate.StoreName}";

    public bool CanEdit => Renewal.IsIisSource;

    private List<Issue> BuildIssues()
    {
        var issues = new List<Issue>();
        var r = Renewal;

        if (r.ConsecutiveErrors > 0)
        {
            var errors = r.History.LastOrDefault(h => h.Success == false)?.AllErrors.ToList() ?? [];
            var detail = errors.Count > 0 ? ": " + string.Join(" | ", errors) : "";
            issues.Add(new Issue(CheckLevel.Error,
                $"The last {(r.ConsecutiveErrors == 1 ? "run" : r.ConsecutiveErrors + " runs")} failed{detail}"));
        }

        if (Certificate != null)
        {
            if (Certificate.DaysLeft < 0)
            {
                issues.Add(new Issue(CheckLevel.Error, $"The certificate expired on {Certificate.NotAfter:dd.MM.yyyy}."));
            }
            else if (Certificate.DaysLeft < ExpiryWarningDays)
            {
                issues.Add(new Issue(CheckLevel.Warning, $"The certificate expires in {Certificate.DaysLeft} days."));
            }
            if (Certificate.IsStaging)
            {
                issues.Add(new Issue(CheckLevel.Error, "This is a test certificate from the Let's Encrypt staging environment and is not trusted by browsers."));
            }
        }
        else if (r.LastSuccess == null)
        {
            issues.Add(new Issue(CheckLevel.Error, "No certificate has been issued for this renewal yet."));
        }
        else if (r.LastThumbprint != null)
        {
            issues.Add(new Issue(CheckLevel.Warning,
                $"The last issued certificate ({r.LastThumbprint}) is no longer in the local machine store."));
        }

        foreach (var host in EffectiveHosts)
        {
            if (HostNameRules.WhyInvalid(host) is { } reason)
            {
                issues.Add(new Issue(CheckLevel.Error,
                    $"'{host}' is part of the next order but cannot be certified ({reason}). Let's Encrypt rejects the whole order."));
            }
        }

        // Bindings using the Central Certificate Store are skipped by win-acme's IIS
        // installation step unless it stores into the CCS itself
        if (!r.StorePlugins.Contains(PluginCatalog.StoreCentralSsl, StringComparer.OrdinalIgnoreCase))
        {
            var names = new HashSet<string>(EffectiveHosts.Concat(Certificate?.DnsNames ?? []), StringComparer.OrdinalIgnoreCase);
            foreach (var host in Sites.SelectMany(s => s.Bindings)
                         .Where(b => b.IsHttps && b.UsesCentralCertStore && names.Contains(b.Host))
                         .Select(b => b.Host)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new Issue(CheckLevel.Error,
                    $"The HTTPS binding of {host} uses the IIS Central Certificate Store. IIS serves {host}.pfx from the CCS " +
                    "folder and win-acme leaves such bindings unchanged, so this certificate is not used. In IIS Manager, edit " +
                    "the binding, clear 'Use Centralized Certificate Store' and select this certificate."));
            }
        }

        if (r.CoversAllSites)
        {
            issues.Add(new Issue(CheckLevel.Warning,
                "This renewal reads the bindings of all IIS sites. Any binding added to any site ends up in the next order."));
        }
        else if (r.CoversAllHosts)
        {
            issues.Add(new Issue(CheckLevel.Warning,
                "This renewal uses all bindings of the site. A binding added later becomes part of the next order. " +
                "Use 'Change host names' to fix the list."));
        }

        if (r.IsIisSource && EffectiveHosts.Count == 0 && r.LastSuccess != null)
        {
            issues.Add(new Issue(CheckLevel.Error,
                "None of the configured host names exist as IIS bindings any more. The next renewal will fail."));
        }

        if (r.UsesManualValidation)
        {
            issues.Add(new Issue(CheckLevel.Warning, "Manual DNS validation cannot renew automatically."));
        }

        return issues.OrderByDescending(i => i.Level).ToList();
    }
}
