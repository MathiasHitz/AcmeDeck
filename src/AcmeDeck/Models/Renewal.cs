using System.Text.Json.Nodes;
using AcmeDeck.Infrastructure;

namespace AcmeDeck.Models;

/// <summary>
/// One win-acme renewal definition, read from a *.renewal.json file.
/// Only the fields the manager needs are mapped; everything else stays
/// untouched because the file is never written by this application.
/// </summary>
public sealed class Renewal
{
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public string? FriendlyName { get; init; }
    public string? LastFriendlyName { get; init; }
    public string? Account { get; init; }

    public string? SourcePlugin { get; init; }
    public List<long> SiteIds { get; init; } = [];
    public List<string> IncludeHosts { get; init; } = [];
    public List<string> ExcludeHosts { get; init; } = [];
    public string? IncludePattern { get; init; }
    public string? IncludeRegex { get; init; }
    public string? CommonName { get; init; }

    public string? ValidationPlugin { get; init; }
    public string? OrderPlugin { get; init; }
    public string? CsrPlugin { get; init; }
    public List<string> StorePlugins { get; init; } = [];
    public List<string> InstallationPlugins { get; init; } = [];

    public List<HistoryEntry> History { get; init; } = [];

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(LastFriendlyName) ? LastFriendlyName :
        !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName : Id;

    public bool IsIisSource => string.Equals(SourcePlugin, PluginCatalog.SourceIis, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// No site filter: win-acme reads the bindings of every IIS site.
    /// </summary>
    public bool CoversAllSites => IsIisSource && SiteIds.Count == 0;

    /// <summary>
    /// No host filter: every binding of the site ends up in the order,
    /// including ones that are added later.
    /// </summary>
    public bool CoversAllHosts => IsIisSource && IncludeHosts.Count == 0 &&
                                  string.IsNullOrWhiteSpace(IncludePattern) &&
                                  string.IsNullOrWhiteSpace(IncludeRegex);

    public bool UsesManualValidation =>
        string.Equals(ValidationPlugin, PluginCatalog.ValidationManualDns, StringComparison.OrdinalIgnoreCase);

    public HistoryEntry? LastEntry => History.LastOrDefault(h => h.Success != null);

    public HistoryEntry? LastSuccess => History.LastOrDefault(h => h.Success == true);

    /// <summary>
    /// Number of failed runs since the last success, same as win-acme counts them.
    /// </summary>
    public int ConsecutiveErrors =>
        History.AsEnumerable().Reverse().Where(h => h.Success != null).TakeWhile(h => h.Success == false).Count();

    public string? LastThumbprint => LastSuccess?.Orders
        .Where(o => o.Success != false && !string.IsNullOrWhiteSpace(o.Thumbprint))
        .Select(o => o.Thumbprint)
        .FirstOrDefault();

    public DateTime? DueDate => LastSuccess?.Orders
        .Where(o => o.DueStart != null)
        .Select(o => o.DueStart)
        .Min();

    public DateTime? HistoryExpireDate =>
        LastSuccess?.ExpireDate ??
        LastSuccess?.Orders.Where(o => o.ExpireDate != null).Select(o => o.ExpireDate).Min();

    public static Renewal Parse(JsonNode root, string filePath)
    {
        var target = root.Get("TargetPluginOptions");
        var validation = root.Get("ValidationPluginOptions");
        return new Renewal
        {
            Id = root.GetString("Id") ?? Path.GetFileName(filePath).Replace(".renewal.json", "", StringComparison.OrdinalIgnoreCase),
            FilePath = filePath,
            FriendlyName = root.GetString("FriendlyName"),
            LastFriendlyName = root.GetString("LastFriendlyName"),
            Account = root.GetString("Account"),
            SourcePlugin = target.GetString("Plugin"),
            SiteIds = target.GetLongList("IncludeSiteIds"),
            IncludeHosts = target.GetStringList("IncludeHosts"),
            ExcludeHosts = target.GetStringList("ExcludeHosts"),
            IncludePattern = target.GetString("IncludePattern"),
            IncludeRegex = target.GetString("IncludeRegex"),
            CommonName = target.GetString("CommonName"),
            ValidationPlugin = validation.GetString("Plugin"),
            OrderPlugin = root.Get("OrderPluginOptions").GetString("Plugin"),
            CsrPlugin = root.Get("CsrPluginOptions").GetString("Plugin"),
            StorePlugins = root.GetArray("StorePluginOptions").Select(s => s.GetString("Plugin")).OfType<string>().ToList(),
            InstallationPlugins = root.GetArray("InstallationPluginOptions").Select(s => s.GetString("Plugin")).OfType<string>().ToList(),
            History = root.GetArray("History").Select(HistoryEntry.Parse).OrderBy(h => h.Date).ToList(),
        };
    }
}

public sealed class HistoryEntry
{
    public DateTime Date { get; init; }
    public bool? Success { get; init; }
    public DateTime? ExpireDate { get; init; }
    public List<OrderEntry> Orders { get; init; } = [];
    public List<string> ErrorMessages { get; init; } = [];

    public string ResultText => Success switch
    {
        true => "Success",
        false => "Failed",
        _ => "Incomplete"
    };

    public IEnumerable<string> AllErrors => ErrorMessages.Concat(Orders.SelectMany(o => o.ErrorMessages)).Distinct();

    public string Summary
    {
        get
        {
            if (Success == true)
            {
                var thumb = Orders.Select(o => o.Thumbprint).FirstOrDefault(t => !string.IsNullOrEmpty(t));
                return thumb == null ? "Certificate issued" : $"Certificate issued ({thumb})";
            }
            var errors = AllErrors.ToList();
            return errors.Count == 0 ? (Success == false ? "Failed without message" : "Run did not complete") : string.Join(" | ", errors);
        }
    }

    public static HistoryEntry Parse(JsonNode node)
    {
        var orders = node.GetArray("OrderResults").Select(OrderEntry.Parse).ToList();
        if (orders.Count == 0)
        {
            // Legacy format: plain thumbprint list without order results
            orders = node.GetStringList("Thumbprints")
                .Select(t => new OrderEntry { Name = "main", Thumbprint = t, Success = node.GetBool("Success") })
                .ToList();
        }
        return new HistoryEntry
        {
            Date = node.GetDate("Date") ?? DateTime.MinValue,
            Success = node.GetBool("Success"),
            ExpireDate = node.GetDate("ExpireDate"),
            Orders = orders,
            ErrorMessages = node.GetStringList("ErrorMessages"),
        };
    }
}

public sealed class OrderEntry
{
    public string Name { get; init; } = "main";
    public bool? Success { get; init; }
    public string? Thumbprint { get; init; }
    public DateTime? ExpireDate { get; init; }
    public DateTime? DueStart { get; init; }
    public DateTime? DueEnd { get; init; }
    public bool? Revoked { get; init; }
    public List<string> ErrorMessages { get; init; } = [];

    public static OrderEntry Parse(JsonNode node)
    {
        var due = node.Get("DueDate");
        return new OrderEntry
        {
            Name = node.GetString("Name") ?? "main",
            Success = node.GetBool("Success"),
            Thumbprint = node.GetString("Thumbprint"),
            ExpireDate = node.GetDate("ExpireDate"),
            DueStart = due.GetDate("Start"),
            DueEnd = due.GetDate("End"),
            Revoked = node.GetBool("Revoked"),
            ErrorMessages = node.GetStringList("ErrorMessages"),
        };
    }
}
