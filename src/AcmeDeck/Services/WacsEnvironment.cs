using System.Diagnostics;
using System.Text.Json.Nodes;
using AcmeDeck.Infrastructure;

namespace AcmeDeck.Services;

/// <summary>
/// Everything that depends on where win-acme lives and how it is configured.
/// Path rules mirror win-acme 2.2.9 (SettingsService.ChooseConfigPath / ChooseLogPath).
/// </summary>
public sealed class WacsEnvironment
{
    public const string DefaultBaseUri = "https://acme-v02.api.letsencrypt.org/";
    public const string DefaultStagingBaseUri = "https://acme-staging-v02.api.letsencrypt.org/";

    public string? WacsPath { get; private init; }
    public bool WacsFound => WacsPath != null && File.Exists(WacsPath);
    public string? WacsFolder => WacsPath == null ? null : Path.GetDirectoryName(WacsPath);
    public string? Version { get; private init; }

    public string ClientName { get; private init; } = "win-acme";
    public string BaseUri { get; private init; } = DefaultBaseUri;
    public string StagingBaseUri { get; private init; } = DefaultStagingBaseUri;
    public string ConfigRoot { get; private init; } = "";
    public string ConfigurationPath { get; private init; } = "";
    public string LogPath { get; private init; } = "";
    public int RenewalDays { get; private init; } = 55;

    /// <summary>settings.json next to wacs.exe, or null when win-acme has never run.</summary>
    public string? SettingsFile { get; private init; }

    /// <summary>
    /// wacs.exe below C:\Users (Desktop, Downloads ...). The scheduled task points
    /// to this path, so cleaning up the profile silently stops all renewals.
    /// </summary>
    public bool IsInUserProfile
    {
        get
        {
            var profilesRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return WacsPath != null && profilesRoot != null &&
                   WacsPath.StartsWith(profilesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    public string ExpectedTaskName => $"{CleanPath(ClientName)} renew ({CleanUri(BaseUri)})";

    public static WacsEnvironment Load(string? wacsPath)
    {
        string? settingsFile = null;
        JsonNode? settings = null;
        string? version = null;
        if (wacsPath != null && File.Exists(wacsPath))
        {
            var folder = Path.GetDirectoryName(wacsPath)!;
            foreach (var name in new[] { "settings.json", "settings_default.json" })
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    settingsFile = name == "settings.json" ? candidate : null;
                    try
                    {
                        settings = JsonNode.Parse(File.ReadAllText(candidate), documentOptions: JsonHelper.ReadOptions);
                    }
                    catch
                    {
                        settings = null;
                    }
                    break;
                }
            }
            var info = FileVersionInfo.GetVersionInfo(wacsPath);
            version = info.ProductVersion ?? info.FileVersion;
            if (version != null && version.Contains('+'))
            {
                version = version[..version.IndexOf('+')];
            }
        }

        var client = settings.Get("Client");
        var acme = settings.Get("Acme");
        var clientName = NullIfEmpty(client.GetString("ClientName")) ?? "win-acme";
        var baseUri = NullIfEmpty(acme.GetString("DefaultBaseUri")) ?? DefaultBaseUri;
        var stagingUri = NullIfEmpty(acme.GetString("DefaultBaseUriTest")) ?? DefaultStagingBaseUri;

        // Configuration root, including the bug-compatible client name subfolder
        var userRoot = NullIfEmpty(client.GetString("ConfigurationPath"));
        string configRoot;
        if (userRoot != null)
        {
            configRoot = userRoot;
            var withClient = Path.Combine(userRoot, clientName);
            if (Directory.Exists(withClient))
            {
                configRoot = withClient;
            }
        }
        else
        {
            configRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), clientName);
        }
        var configPath = Path.Combine(configRoot, CleanUri(baseUri));

        var logRoot = NullIfEmpty(client.GetString("LogPath"));
        var logPath = logRoot == null ? Path.Combine(configPath, "Log") : Path.Combine(logRoot, CleanUri(baseUri));

        return new WacsEnvironment
        {
            WacsPath = wacsPath,
            Version = version,
            ClientName = clientName,
            BaseUri = baseUri,
            StagingBaseUri = stagingUri,
            ConfigRoot = configRoot,
            ConfigurationPath = configPath,
            LogPath = logPath,
            RenewalDays = settings.Get("ScheduledTask").GetInt("RenewalDays") ?? 55,
            SettingsFile = settingsFile,
        };
    }

    public string ConfigurationPathFor(string baseUri) => Path.Combine(ConfigRoot, CleanUri(baseUri));

    /// <summary>
    /// Finds wacs.exe: the configured path first, then the one the scheduled task
    /// runs (that is the win-acme that actually renews, the GUI must drive the same
    /// one), then the copy bundled with the setup and the usual install folder.
    /// </summary>
    public static string? Locate(string? configuredPath, IEnumerable<ScheduledTaskInfo> tasks)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }
        var candidates = tasks
            .Where(t => t.Name.Contains(" renew (") && !t.Name.Contains("staging", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ExecutablePath)
            .OfType<string>()
            .ToList();
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "win-acme", "wacs.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "win-acme", "wacs.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    // Same as win-acme StringExtensions.CleanUri / CleanPath
    public static string CleanUri(string uri)
    {
        var s = uri;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.UserInfo))
        {
            s = s.Replace($"{parsed.UserInfo}@", "");
        }
        s = s.Replace("https://", "").Replace("http://", "");
        return CleanPath(s);
    }

    public static string CleanPath(string name) =>
        Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c.ToString(), string.Empty));

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
