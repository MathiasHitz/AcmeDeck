using AcmeDeck.Infrastructure;
using AcmeDeck.Models;
using AcmeDeck.Services;

namespace AcmeDeck.ViewModels;

public enum BindingState
{
    Checking,
    NoCertificate,
    /// <summary>A renewal exists for this host, but IIS serves a different certificate.</summary>
    NotApplied,
    Expired,
    NotManaged,
    Managed
}

/// <summary>
/// One HTTPS binding in IIS together with the certificate it actually serves.
/// Used to find certificates that are not managed by win-acme (other ACME clients,
/// purchased certificates, manual imports) so they can be taken over, and to
/// catch renewals whose certificate never reached the binding.
/// </summary>
public sealed class BindingItemViewModel(IisSite site, IisBinding binding) : ObservableObject
{
    private CertificateDetails? _certificate;
    private string? _probeError;
    private CertificateItemViewModel? _renewal;
    private BindingState _state = BindingState.Checking;

    public IisSite Site { get; } = site;
    public IisBinding Binding { get; } = binding;

    public string SiteName => Site.Name;
    public string Host => string.IsNullOrEmpty(Binding.Host) ? "(no host name)" : Binding.Host;

    public string Endpoint => $"{Binding.IpAddress}:{Binding.Port}" +
                              (Binding.UsesSni ? " · SNI" : "") +
                              (Binding.UsesCentralCertStore ? " · CCS" : "");

    public CertificateDetails? Certificate => _certificate;
    public BindingState State => _state;

    public string IssuerText => _certificate?.Issuer ?? (_state == BindingState.Checking ? "…" : "-");

    public string ExpiresText => _certificate == null
        ? _probeError ?? (_state == BindingState.Checking ? "…" : "-")
        : _certificate.DaysLeft < 0
            ? $"{_certificate.NotAfter:dd.MM.yyyy} (expired)"
            : $"{_certificate.NotAfter:dd.MM.yyyy} ({_certificate.DaysLeft} days)";

    public string ManagedByText => _state switch
    {
        BindingState.Checking => "…",
        BindingState.Managed => _renewal!.Name,
        BindingState.NotApplied => _renewal!.Name + " (not applied)",
        _ when _renewal != null => _renewal.Name,
        _ => "Not managed by win-acme"
    };

    /// <summary>
    /// Only offered when no renewal covers the host yet, otherwise a second,
    /// competing renewal would be created.
    /// </summary>
    public bool CanTakeOver => _state is BindingState.NotManaged or BindingState.Expired or BindingState.NoCertificate &&
                               _renewal == null && HostNameRules.WhyInvalid(Binding.Host) == null;

    public CheckLevel Level => _state switch
    {
        BindingState.Checking => CheckLevel.Info,
        BindingState.NoCertificate or BindingState.NotApplied or BindingState.Expired => CheckLevel.Error,
        BindingState.NotManaged => CheckLevel.Warning,
        _ => _certificate is { DaysLeft: < 14 } ? CheckLevel.Warning : CheckLevel.Ok
    };

    public string StatusText => _state switch
    {
        BindingState.Checking => "Checking…",
        BindingState.NoCertificate => "No certificate",
        BindingState.NotApplied => "Not applied",
        BindingState.Expired => "Expired",
        BindingState.NotManaged => "Not managed",
        _ => "Managed"
    };

    /// <summary>
    /// Explanation and fix, shown below the list for the selected binding.
    /// </summary>
    public string? Hint => _state switch
    {
        BindingState.NotApplied when Binding.UsesCentralCertStore =>
            $"This binding uses the IIS Central Certificate Store (CCS): IIS serves {Host}.pfx from the CCS folder and ignores " +
            $"the certificate issued for '{_renewal!.Name}'. win-acme does not change CCS bindings. Fix: in IIS Manager, edit this " +
            "binding, clear 'Use Centralized Certificate Store' and select the certificate of that renewal. Later renewals " +
            "then update the binding automatically.",
        BindingState.NotApplied when Site.CaseConflict(Binding.Host) is { } spellings =>
            $"The renewal '{_renewal!.Name}' covers {Host}, but IIS serves a different certificate. Likely cause: " +
            IisSite.CaseConflictText(Binding.Host, spellings) + " Fix the spelling in IIS Manager and select the certificate " +
            "of the renewal for this binding; 'Renew now' alone does not help.",
        BindingState.NotApplied =>
            $"The renewal '{_renewal!.Name}' covers {Host}, but IIS serves a different certificate. Select the renewal on the " +
            "Certificates tab and use 'Renew now', or select its certificate for this binding in IIS Manager.",
        BindingState.NotManaged when Site.CaseConflict(Binding.Host) is { } spellings =>
            "No win-acme renewal covers this host. Before taking it over: " + IisSite.CaseConflictText(Binding.Host, spellings),
        BindingState.NotManaged when Binding.UsesCentralCertStore =>
            "This binding uses the IIS Central Certificate Store. Before taking it over, clear 'Use Centralized Certificate Store' " +
            "in IIS Manager (keep the current certificate selected), otherwise win-acme leaves the binding unchanged.",
        BindingState.NotManaged =>
            "No win-acme renewal covers this host. The certificate comes from another tool or was installed manually. " +
            "'Take over' requests a Let's Encrypt certificate for it.",
        BindingState.NoCertificate =>
            "No certificate could be retrieved from this binding: " + (_probeError ?? "unknown error"),
        _ => null
    };

    public async Task CheckAsync(IReadOnlyList<CertificateItemViewModel> certificates)
    {
        var host = string.IsNullOrEmpty(Binding.Host) ? "localhost" : Binding.Host;
        var result = await TlsProbe.ProbeAsync(Binding.IpAddress, Binding.Port, host, TimeSpan.FromSeconds(5));
        _probeError = result.Error;
        _certificate = result.Certificate;

        var served = result.Certificate?.Thumbprint;
        var current = served == null
            ? null
            : certificates.FirstOrDefault(c => string.Equals(c.Renewal.LastThumbprint, served, StringComparison.OrdinalIgnoreCase));
        // Several renewals may cover the host; prefer the one for this site with an explicit host list
        _renewal = current ?? certificates
            .Where(c => Covers(c, Binding.Host))
            .OrderByDescending(c => (c.Renewal.SiteIds.Contains(Site.Id) ? 2 : 0) +
                                    (c.Renewal.IncludeHosts.Contains(Binding.Host, StringComparer.OrdinalIgnoreCase) ? 1 : 0))
            .FirstOrDefault();

        _state =
            result.Certificate == null ? BindingState.NoCertificate :
            current != null ? (result.Certificate.DaysLeft < 0 ? BindingState.Expired : BindingState.Managed) :
            _renewal != null ? BindingState.NotApplied :
            result.Certificate.DaysLeft < 0 ? BindingState.Expired :
            BindingState.NotManaged;

        foreach (var name in new[]
                 {
                     nameof(Certificate), nameof(State), nameof(IssuerText), nameof(ExpiresText), nameof(ManagedByText),
                     nameof(CanTakeOver), nameof(Level), nameof(StatusText), nameof(Hint)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private static bool Covers(CertificateItemViewModel item, string host) =>
        !string.IsNullOrEmpty(host) &&
        (item.EffectiveHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ||
         item.Renewal.IncludeHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ||
         (item.Certificate?.DnsNames.Contains(host, StringComparer.OrdinalIgnoreCase) ?? false));
}
