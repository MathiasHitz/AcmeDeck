using AcmeDeck.Models;

namespace AcmeDeck.Services;

public enum ValidationMethod
{
    SelfHosting,
    FileSystem
}

public sealed class NewCertificateRequest
{
    public required long SiteId { get; init; }
    public required IReadOnlyList<string> Hosts { get; init; }
    public string? CommonName { get; init; }
    public string? FriendlyName { get; init; }
    public ValidationMethod Validation { get; init; } = ValidationMethod.SelfHosting;
    public string? ContactEmail { get; init; }
}

/// <summary>
/// Builds wacs.exe argument lists. New certificates always get an explicit
/// site id and an explicit host list: a renewal without filters picks up every
/// binding added later and breaks as soon as one of them is not public.
/// </summary>
public static class WacsCommands
{
    public static List<string> Create(NewCertificateRequest request, string id, string? baseUri = null)
    {
        if (request.Hosts.Count == 0)
        {
            throw new ArgumentException("At least one host name is required.");
        }
        var args = new List<string>();
        if (baseUri != null)
        {
            args.AddRange(["--baseuri", baseUri]);
        }
        args.AddRange(["--id", id]);
        args.AddRange(["--source", "iis"]);
        args.AddRange(["--siteid", request.SiteId.ToString()]);
        args.AddRange(["--host", string.Join(",", request.Hosts)]);
        if (!string.IsNullOrWhiteSpace(request.CommonName))
        {
            args.AddRange(["--commonname", request.CommonName]);
        }
        if (!string.IsNullOrWhiteSpace(request.FriendlyName))
        {
            args.AddRange(["--friendlyname", request.FriendlyName]);
        }
        switch (request.Validation)
        {
            case ValidationMethod.FileSystem:
                args.AddRange(["--validation", "filesystem", "--validationsiteid", request.SiteId.ToString()]);
                break;
            default:
                args.AddRange(["--validation", "selfhosting"]);
                break;
        }
        args.AddRange(["--store", "certificatestore"]);
        args.AddRange(["--installation", "iis"]);
        args.Add("--accepttos");
        if (!string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            args.AddRange(["--emailaddress", request.ContactEmail]);
        }
        return args;
    }

    /// <summary>
    /// Dry run against the Let's Encrypt staging environment. Nothing is stored
    /// or bound and no scheduled task is created, so production stays untouched
    /// and the production rate limits are not consumed.
    /// </summary>
    public static List<string> CreateStagingTest(NewCertificateRequest request, string id, string stagingBaseUri)
    {
        var args = Create(request, id, stagingBaseUri);
        ReplaceValue(args, "--store", "none");
        ReplaceValue(args, "--installation", "none");
        args.Add("--notaskscheduler");
        return args;
    }

    public static List<string> CancelStaging(string id, string stagingBaseUri) =>
        ["--baseuri", stagingBaseUri, "--cancel", "--id", id];

    public static List<string> Renew(string id, bool force) =>
        force ? ["--renew", "--id", id, "--force"] : ["--renew", "--id", id];

    public static List<string> Cancel(string id) => ["--cancel", "--id", id];

    public static List<string> Revoke(string id) => ["--revoke", "--id", id];

    public static List<string> SetupTaskScheduler() => ["--setuptaskscheduler"];

    /// <summary>
    /// Renewal ids end up in file names, so only use safe characters.
    /// </summary>
    public static string NewId() => "ad" + Guid.NewGuid().ToString("N")[..20];

    /// <summary>
    /// Equivalent command line for an existing renewal, for display purposes.
    /// </summary>
    public static string Describe(Renewal renewal)
    {
        if (!renewal.IsIisSource)
        {
            return $"(source: {PluginCatalog.NameOf(renewal.SourcePlugin)})";
        }
        var parts = new List<string> { "--source iis" };
        parts.Add(renewal.SiteIds.Count > 0 ? "--siteid " + string.Join(",", renewal.SiteIds) : "(all sites)");
        if (renewal.IncludeHosts.Count > 0)
        {
            parts.Add("--host " + string.Join(",", renewal.IncludeHosts));
        }
        else if (!string.IsNullOrWhiteSpace(renewal.IncludePattern))
        {
            parts.Add("--host-pattern " + renewal.IncludePattern);
        }
        else if (!string.IsNullOrWhiteSpace(renewal.IncludeRegex))
        {
            parts.Add("--host-regex " + renewal.IncludeRegex);
        }
        else
        {
            parts.Add("(all bindings)");
        }
        if (renewal.ExcludeHosts.Count > 0)
        {
            parts.Add("--excludebindings " + string.Join(",", renewal.ExcludeHosts));
        }
        return string.Join(" ", parts);
    }

    private static void ReplaceValue(List<string> args, string option, string value)
    {
        var index = args.IndexOf(option);
        if (index >= 0 && index + 1 < args.Count)
        {
            args[index + 1] = value;
        }
        else
        {
            args.AddRange([option, value]);
        }
    }
}
