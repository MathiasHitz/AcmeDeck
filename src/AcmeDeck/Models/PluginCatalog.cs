namespace AcmeDeck.Models;

/// <summary>
/// Plugin identifiers as declared in the win-acme 2.2.9 source code. The renewal
/// files only store the GUID, the command line uses the short name.
/// </summary>
public static class PluginCatalog
{
    public const string SourceIis = "54deb3ee-b5df-4381-8485-fe386054055b";
    public const string ValidationSelfHosting = "c7d5e050-9363-4ba1-b3a8-931b31c618b7";
    public const string ValidationFileSystem = "1c77b3a4-5310-4c46-92c6-00d866e84d6b";
    public const string ValidationManualDns = "e45d62b9-f9a8-441e-b95f-c5ee0dcd8040";
    public const string StoreCertificateStore = "e30adc8e-d756-4e16-a6f2-450f784b1a97";
    public const string StoreCentralSsl = "af1f77b6-4e7b-4f96-bba5-c2eeb4d0dd42";
    public const string InstallationIis = "ea6a5be3-f8de-4d27-a6bd-750b619b2ee2";

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // Source
        [SourceIis] = "IIS",
        ["cdd79a68-4a87-4039-bee8-5a0ebdca41cb"] = "IIS sites (legacy)",
        ["d7940b23-f570-460e-ab15-2c822a79009b"] = "IIS site (legacy)",
        ["2f5dd428-0f5d-4c8a-8fd0-56fc1b5985ce"] = "IIS binding (legacy)",
        ["e239db3b-b42f-48aa-b64f-46d4f3e9941b"] = "Manual",
        // Validation
        [ValidationSelfHosting] = "SelfHosting (HTTP-01)",
        [ValidationFileSystem] = "FileSystem (HTTP-01)",
        ["bc27d719-dcf2-41ff-bf08-54db7ea49c48"] = "FTP (HTTP-01)",
        ["048aa2e7-2bce-4d3e-b731-6e0ed8b8170d"] = "SFTP (HTTP-01)",
        ["7e191d0e-30d1-47b3-ae2e-442499d33e16"] = "WebDav (HTTP-01)",
        ["a1565064-b208-4467-8ca1-1bd3c08aa500"] = "SelfHosting (TLS-ALPN-01)",
        ["c13acc1b-7571-432b-9652-7a68a5f506c5"] = "acme-dns (DNS-01)",
        [ValidationManualDns] = "Manual (DNS-01, no auto-renewal)",
        ["8f1da72e-f727-49f0-8546-ef69e5ecec32"] = "Script (DNS-01)",
        // Order
        ["b705fa7c-1152-4436-8913-e433d7f84c82"] = "Single certificate",
        ["74a42b2d-8eaa-4f40-ab6a-f55304254143"] = "One certificate per site",
        ["874a86e4-29c7-4294-9ab6-6908866847a0"] = "One certificate per host",
        ["b7c331d4-d875-453e-b83a-2b537ca12535"] = "One certificate per domain",
        // CSR
        ["b9060d4b-c2d3-49ac-b37f-962e7c3cbe9d"] = "RSA",
        ["9aadcf71-5241-4c4f-aee1-bfe3f6be3489"] = "Elliptic curve",
        // Store
        [StoreCertificateStore] = "Windows certificate store",
        [StoreCentralSsl] = "IIS Central Certificate Store",
        ["e57c70e4-cd60-4ba6-80f6-a41703e21031"] = "PEM files",
        ["2a2c576f-7637-4ade-b8db-e8613b0bb33e"] = "PFX file",
        ["cfdd7caa-ba34-4e9e-b9de-2a3d64c4f4ec"] = "None",
        // Installation
        [InstallationIis] = "IIS bindings",
        ["13058a79-5084-48af-b047-634e0ee222f4"] = "IIS FTP bindings",
        ["3bb22c70-358d-4251-86bd-11858363d913"] = "Script",
        ["aecc502c-5f75-43d2-b578-f95d50c79ea1"] = "None",
    };

    public static string NameOf(string? pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return "-";
        }
        return Names.TryGetValue(pluginId, out var name) ? name : $"Unknown ({pluginId})";
    }
}
