using AcmeDeck.Infrastructure;
using AcmeDeck.Models;
using AcmeDeck.Services;

namespace AcmeDeck.ViewModels;

/// <summary>
/// One HTTPS binding in IIS together with the certificate it actually serves.
/// Used to find certificates that are not managed by win-acme (other ACME clients,
/// purchased certificates, manual imports) so they can be taken over.
/// </summary>
public sealed class BindingItemViewModel(IisSite site, IisBinding binding) : ObservableObject
{
    private CertificateDetails? _certificate;
    private string? _probeError;
    private CertificateItemViewModel? _managedBy;
    private bool _checked;

    public IisSite Site { get; } = site;
    public IisBinding Binding { get; } = binding;

    public string SiteName => Site.Name;
    public string Host => string.IsNullOrEmpty(Binding.Host) ? "(no host name)" : Binding.Host;
    public string Endpoint => $"{Binding.IpAddress}:{Binding.Port}{(Binding.UsesSni ? " · SNI" : "")}";

    public CertificateDetails? Certificate
    {
        get => _certificate;
        private set
        {
            SetProperty(ref _certificate, value);
            OnPropertyChanged(nameof(IssuerText));
            OnPropertyChanged(nameof(ExpiresText));
            OnPropertyChanged(nameof(Level));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public CertificateItemViewModel? ManagedBy
    {
        get => _managedBy;
        private set
        {
            SetProperty(ref _managedBy, value);
            OnPropertyChanged(nameof(ManagedByText));
            OnPropertyChanged(nameof(CanTakeOver));
            OnPropertyChanged(nameof(Level));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string IssuerText => Certificate?.Issuer ?? (_checked ? "-" : "…");

    public string ExpiresText => Certificate == null
        ? _probeError ?? (_checked ? "-" : "…")
        : Certificate.DaysLeft < 0
            ? $"{Certificate.NotAfter:dd.MM.yyyy} (expired)"
            : $"{Certificate.NotAfter:dd.MM.yyyy} ({Certificate.DaysLeft} days)";

    public string ManagedByText => ManagedBy?.Name ?? (_checked ? "Not managed by win-acme" : "…");

    public bool CanTakeOver => _checked && ManagedBy == null && HostNameRules.WhyInvalid(Binding.Host) == null;

    public CheckLevel Level =>
        !_checked ? CheckLevel.Info :
        Certificate == null ? CheckLevel.Error :
        Certificate.DaysLeft < 0 ? CheckLevel.Error :
        ManagedBy == null ? CheckLevel.Warning :
        Certificate.DaysLeft < 14 ? CheckLevel.Warning :
        CheckLevel.Ok;

    public string StatusText =>
        !_checked ? "Checking…" :
        Certificate == null ? "No certificate" :
        Certificate.DaysLeft < 0 ? "Expired" :
        ManagedBy == null ? "Not managed" :
        "Managed";

    public async Task CheckAsync(IReadOnlyList<CertificateItemViewModel> certificates)
    {
        var host = string.IsNullOrEmpty(Binding.Host) ? "localhost" : Binding.Host;
        var result = await TlsProbe.ProbeAsync(Binding.IpAddress, Binding.Port, host, TimeSpan.FromSeconds(5));
        _probeError = result.Error;
        _checked = true;
        Certificate = result.Certificate;
        ManagedBy = result.Certificate == null
            ? null
            : certificates.FirstOrDefault(c => string.Equals(c.Renewal.LastThumbprint, result.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
              ?? certificates.FirstOrDefault(c => c.Renewal.History.Any(h => h.Orders.Any(o =>
                  string.Equals(o.Thumbprint, result.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))));
        OnPropertyChanged(nameof(IssuerText));
        OnPropertyChanged(nameof(ExpiresText));
        OnPropertyChanged(nameof(ManagedByText));
        OnPropertyChanged(nameof(CanTakeOver));
    }
}
