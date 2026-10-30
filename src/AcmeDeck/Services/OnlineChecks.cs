using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using AcmeDeck.Infrastructure;

namespace AcmeDeck.Services;

/// <summary>
/// Ordered by severity, so Max() yields the worst finding.
/// </summary>
public enum CheckLevel
{
    Ok,
    Info,
    Warning,
    Error
}

public sealed record DnsCheckResult(string Host, CheckLevel Level, string Message, List<string> Addresses);

/// <summary>
/// Checks what the internet sees, not what this server sees. Local resolvers
/// (Pi-hole, split DNS, hosts file) can make a name look fine on site while
/// Let's Encrypt resolves it to a different machine. Uses DNS over HTTPS so
/// blocked outbound port 53 does not matter.
/// </summary>
public static class OnlineChecks
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<string?> GetPublicIPv4Async()
    {
        try
        {
            var text = (await Http.GetStringAsync("https://api.ipify.org")).Trim();
            return IPAddress.TryParse(text, out _) ? text : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(int Status, List<string> Addresses, List<string> Aliases)> ResolveAsync(string host)
    {
        var addresses = new List<string>();
        var aliases = new List<string>();
        var status = 0;
        foreach (var type in new[] { "A", "AAAA" })
        {
            var json = await Http.GetStringAsync($"https://dns.google/resolve?name={Uri.EscapeDataString(host)}&type={type}");
            var root = JsonNode.Parse(json);
            var s = root.GetInt("Status") ?? 0;
            if (type == "A")
            {
                status = s;
            }
            foreach (var answer in root.GetArray("Answer"))
            {
                var data = answer.GetString("data");
                switch (answer.GetInt("type"))
                {
                    case 1:
                    case 28:
                        if (data != null && !addresses.Contains(data))
                        {
                            addresses.Add(data);
                        }
                        break;
                    case 5:
                        if (data != null && !aliases.Contains(data.TrimEnd('.')))
                        {
                            aliases.Add(data.TrimEnd('.'));
                        }
                        break;
                }
            }
        }
        return (status, addresses, aliases);
    }

    public static async Task<DnsCheckResult> CheckHostAsync(string host, string? serverPublicIp)
    {
        try
        {
            var (status, addresses, aliases) = await ResolveAsync(host);
            var via = aliases.Count > 0 ? $" (via {string.Join(" → ", aliases)})" : "";
            if (status == 3 || addresses.Count == 0)
            {
                return new DnsCheckResult(host, CheckLevel.Error,
                    "No public DNS record. Let's Encrypt cannot reach this name.", addresses);
            }
            if (serverPublicIp == null)
            {
                return new DnsCheckResult(host, CheckLevel.Info,
                    $"Resolves to {string.Join(", ", addresses)}{via}. Public IP of this server unknown.", addresses);
            }
            if (addresses.Contains(serverPublicIp))
            {
                return new DnsCheckResult(host, CheckLevel.Ok,
                    $"Resolves to {string.Join(", ", addresses)}{via}, this server.", addresses);
            }
            var message = $"Resolves to {string.Join(", ", addresses)}{via}, but this server leaves the internet as {serverPublicIp}. " +
                          "Validation will fail unless that address forwards to this server.";
            if (await IsWildcardAnswerAsync(host, addresses))
            {
                message += " The answer is identical to the zone's wildcard record (*." + ParentDomain(host) +
                           "), so there is probably no dedicated record for this name. Add an A record pointing to this server.";
            }
            return new DnsCheckResult(host, CheckLevel.Warning, message, addresses);
        }
        catch (Exception ex)
        {
            return new DnsCheckResult(host, CheckLevel.Info, "DNS check not possible: " + ex.Message, []);
        }
    }

    /// <summary>
    /// Many hosters answer every unknown subdomain with a wildcard record. A name
    /// without its own record then silently points to the hoster's default page,
    /// while a local resolver may still return the right address.
    /// </summary>
    private static async Task<bool> IsWildcardAnswerAsync(string host, List<string> addresses)
    {
        var parent = ParentDomain(host);
        if (parent == null)
        {
            return false;
        }
        try
        {
            var probe = $"acmedeck-{Guid.NewGuid():N}"[..20] + "." + parent;
            var (_, probeAddresses, _) = await ResolveAsync(probe);
            return probeAddresses.Count > 0 && probeAddresses.Order().SequenceEqual(addresses.Order());
        }
        catch
        {
            return false;
        }
    }

    private static string? ParentDomain(string host)
    {
        var dot = host.IndexOf('.');
        var parent = dot < 0 ? null : host[(dot + 1)..];
        // Do not probe directly below a public suffix like "ch"
        return parent != null && parent.Contains('.') ? parent : null;
    }
}
