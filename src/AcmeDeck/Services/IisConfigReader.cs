using System.Xml.Linq;
using AcmeDeck.Models;

namespace AcmeDeck.Services;

/// <summary>
/// Reads sites and bindings from applicationHost.config. Read-only on purpose:
/// win-acme's IIS installation step is responsible for changing bindings.
/// The process is x64, so System32 is not redirected.
/// </summary>
public static class IisConfigReader
{
    public static string ConfigPath =>
        Path.Combine(Environment.SystemDirectory, "inetsrv", "config", "applicationHost.config");

    public static bool IisInstalled => File.Exists(ConfigPath);

    public static List<IisSite> ReadSites()
    {
        if (!IisInstalled)
        {
            return [];
        }
        var doc = XDocument.Load(ConfigPath);
        var sites = doc.Root?
            .Element("system.applicationHost")?
            .Element("sites")?
            .Elements("site") ?? [];

        var result = new List<IisSite>();
        foreach (var site in sites)
        {
            if (!long.TryParse((string?)site.Attribute("id"), out var id))
            {
                continue;
            }
            var rootApp = site.Elements("application").FirstOrDefault(a => (string?)a.Attribute("path") == "/");
            var rootDir = rootApp?.Elements("virtualDirectory").FirstOrDefault(v => (string?)v.Attribute("path") == "/");
            var physicalPath = (string?)rootDir?.Attribute("physicalPath");

            var bindings = new List<IisBinding>();
            foreach (var b in site.Element("bindings")?.Elements("binding") ?? [])
            {
                var protocol = (string?)b.Attribute("protocol") ?? "";
                var info = (string?)b.Attribute("bindingInformation") ?? "";
                int.TryParse((string?)b.Attribute("sslFlags"), out var sslFlags);
                var binding = IisBinding.Parse(protocol, info, sslFlags);
                if (binding != null)
                {
                    bindings.Add(binding);
                }
            }

            result.Add(new IisSite
            {
                Id = id,
                Name = (string?)site.Attribute("name") ?? $"Site {id}",
                PhysicalPath = physicalPath == null ? null : Environment.ExpandEnvironmentVariables(physicalPath),
                Bindings = bindings,
            });
        }
        return result.OrderBy(s => s.Id).ToList();
    }

    /// <summary>
    /// The host names win-acme would put into the order for this renewal,
    /// following the same include/exclude rules as the IIS source plugin.
    /// </summary>
    public static List<string> EffectiveHosts(Renewal renewal, IReadOnlyList<IisSite> sites)
    {
        if (!renewal.IsIisSource)
        {
            return [];
        }
        var relevantSites = renewal.SiteIds.Count == 0 ? sites : sites.Where(s => renewal.SiteIds.Contains(s.Id));
        var hosts = relevantSites.SelectMany(s => s.HostNames).Distinct(StringComparer.OrdinalIgnoreCase);

        if (renewal.IncludeHosts.Count > 0)
        {
            hosts = hosts.Where(h => renewal.IncludeHosts.Contains(h, StringComparer.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(renewal.IncludePattern))
        {
            var patterns = renewal.IncludePattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            hosts = hosts.Where(h => patterns.Any(p => WildcardMatch(h, p)));
        }
        else if (!string.IsNullOrWhiteSpace(renewal.IncludeRegex))
        {
            var regex = new System.Text.RegularExpressions.Regex(renewal.IncludeRegex);
            hosts = hosts.Where(h => regex.IsMatch(h));
        }
        if (renewal.ExcludeHosts.Count > 0)
        {
            hosts = hosts.Where(h => !renewal.ExcludeHosts.Contains(h, StringComparer.OrdinalIgnoreCase));
        }
        return hosts.ToList();
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
