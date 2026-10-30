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
