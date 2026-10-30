namespace AcmeDeck.Models;

public sealed class IisSite
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public string? PhysicalPath { get; init; }
    public List<IisBinding> Bindings { get; init; } = [];

    public IEnumerable<string> HostNames => Bindings
        .Where(b => b.IsWeb && !string.IsNullOrWhiteSpace(b.Host))
        .Select(b => b.Host)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public string BindingSummary => string.Join(", ", Bindings.Where(b => b.IsWeb).Select(b => b.Display));

    /// <summary>
    /// Spellings of the host name in this site's bindings when they differ only in
    /// upper/lower case (e.g. http "Burgdorf.example.ch", https "burgdorf.example.ch"),
    /// otherwise null. win-acme 2.2.9 narrows the matching bindings down with a
    /// case-sensitive comparison (IISHttpBindingUpdater.AddOrUpdateBindings) and then
    /// neither updates the https binding nor adds one ("Prevent adding duplicate binding").
    /// </summary>
    public IReadOnlyList<string>? CaseConflict(string host)
    {
        var spellings = Bindings
            .Where(b => b.IsWeb && string.Equals(b.Host, host, StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Host)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return spellings.Count > 1 ? spellings : null;
    }

    public static string CaseConflictText(string host, IReadOnlyList<string> spellings) =>
        $"The bindings for {host} use different capitalization ({string.Join(", ", spellings.Select(s => "'" + s + "'"))}). " +
        "win-acme 2.2.9 compares them case-sensitively at one point and may then leave the HTTPS binding unchanged while " +
        "reporting success. Use the same spelling for all bindings of this name, preferably lower case " +
        "(host names are not case-sensitive, so this is safe).";

    public override string ToString() => $"{Name} (ID {Id})";
}

public sealed class IisBinding
{
    public required string Protocol { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }
    public required string Host { get; init; }
    public int SslFlags { get; init; }

    public bool IsWeb => Protocol is "http" or "https";
    public bool IsHttps => Protocol == "https";
    public bool UsesSni => (SslFlags & 1) == 1;

    /// <summary>
    /// "Use Centralized Certificate Store": IIS takes the certificate from
    /// &lt;CCS folder&gt;\&lt;host&gt;.pfx and ignores the certificate of the binding.
    /// win-acme treats such bindings as already served and leaves them alone,
    /// unless it stores into the CCS itself (--store centralssl).
    /// </summary>
    public bool UsesCentralCertStore => (SslFlags & 2) == 2;

    public string Display => string.IsNullOrEmpty(Host)
        ? $"{Protocol}://*:{Port}"
        : $"{Protocol}://{Host}:{Port}";

    /// <summary>
    /// Parses IIS binding information in the form "ip:port:host".
    /// The IP part may be an IPv6 address in brackets, so split from the right.
    /// </summary>
    public static IisBinding? Parse(string protocol, string bindingInformation, int sslFlags)
    {
        var lastColon = bindingInformation.LastIndexOf(':');
        if (lastColon < 0)
        {
            return null;
        }
        var host = bindingInformation[(lastColon + 1)..];
        var rest = bindingInformation[..lastColon];
        var portColon = rest.LastIndexOf(':');
        if (portColon < 0 || !int.TryParse(rest[(portColon + 1)..], out var port))
        {
            return null;
        }
        var ip = rest[..portColon];
        return new IisBinding
        {
            Protocol = protocol.ToLowerInvariant(),
            IpAddress = string.IsNullOrEmpty(ip) ? "*" : ip,
            Port = port,
            Host = host,
            SslFlags = sslFlags,
        };
    }
}
