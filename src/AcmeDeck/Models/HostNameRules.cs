using System.Net;
using System.Text.RegularExpressions;

namespace AcmeDeck.Models;

/// <summary>
/// Pre-checks host names before they reach Let's Encrypt. A single invalid
/// name makes the CA reject the whole order, which is exactly what broke the
/// renewals with "(any host)" and a localhost binding.
/// </summary>
public static partial class HostNameRules
{
    private static readonly string[] PrivateSuffixes =
        [".local", ".lan", ".localdomain", ".internal", ".intranet", ".home", ".corp", ".private", ".test", ".example", ".invalid", ".localhost"];

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$", RegexOptions.IgnoreCase)]
    private static partial Regex HostRegex();

    /// <summary>
    /// Returns null when the name can be validated publicly, otherwise the reason why not.
    /// </summary>
    public static string? WhyInvalid(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "Binding without host name";
        }
        var h = host.Trim().TrimEnd('.');
        if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "localhost cannot be certified publicly";
        }
        if (IPAddress.TryParse(h, out _))
        {
            return "IP addresses are not supported";
        }
        if (h.StartsWith("*.", StringComparison.Ordinal))
        {
            return "Wildcards need DNS validation";
        }
        if (!h.Contains('.'))
        {
            return "Name needs at least one dot";
        }
        if (PrivateSuffixes.Any(s => h.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            return "Not a public domain";
        }
        if (!HostRegex().IsMatch(h))
        {
            return "Invalid characters";
        }
        return null;
    }
}
