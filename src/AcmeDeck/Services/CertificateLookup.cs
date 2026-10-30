using System.Security.Cryptography.X509Certificates;

namespace AcmeDeck.Services;

public sealed class CertificateDetails
{
    public required string Thumbprint { get; init; }
    public required string Subject { get; init; }
    public required string Issuer { get; init; }
    public required DateTime NotBefore { get; init; }
    public required DateTime NotAfter { get; init; }
    public required List<string> DnsNames { get; init; }
    public string? StoreName { get; init; }
    public string? FriendlyName { get; init; }

    public int DaysLeft => (int)Math.Floor((NotAfter - DateTime.Now).TotalDays);

    public bool IsStaging => Issuer.Contains("STAGING", StringComparison.OrdinalIgnoreCase) ||
                             Issuer.Contains("(STAGING)", StringComparison.OrdinalIgnoreCase) ||
                             Issuer.Contains("Fake", StringComparison.OrdinalIgnoreCase);

    public static CertificateDetails From(X509Certificate2 cert, string? storeName)
    {
        var names = new List<string>();
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.5.29.17")
            {
                var san = ext as X509SubjectAlternativeNameExtension ?? new X509SubjectAlternativeNameExtension(ext.RawData, ext.Critical);
                names.AddRange(san.EnumerateDnsNames());
            }
        }
        if (names.Count == 0)
        {
            names.Add(cert.GetNameInfo(X509NameType.DnsName, false));
        }
        return new CertificateDetails
        {
            Thumbprint = cert.Thumbprint,
            Subject = cert.Subject,
            Issuer = cert.GetNameInfo(X509NameType.SimpleName, true),
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            DnsNames = names,
            StoreName = storeName,
            FriendlyName = cert.FriendlyName,
        };
    }
}

/// <summary>
/// Looks up issued certificates in the local machine stores. win-acme uses
/// WebHosting by default, older installs and other tools use My (Personal).
/// </summary>
public static class CertificateLookup
{
    private static readonly string[] Stores = ["WebHosting", "My"];

    public static CertificateDetails? Find(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return null;
        }
        foreach (var storeName in Stores)
        {
            try
            {
                using var store = new X509Store(storeName, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                if (found.Count > 0)
                {
                    using var cert = found[0];
                    return CertificateDetails.From(cert, storeName);
                }
            }
            catch
            {
                // Store does not exist on this machine
            }
        }
        return null;
    }
}
