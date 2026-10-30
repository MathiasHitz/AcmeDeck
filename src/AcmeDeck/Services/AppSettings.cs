using System.Text.Json;

namespace AcmeDeck.Services;

/// <summary>
/// Settings of the manager itself (not of win-acme). Stored machine-wide,
/// because the tool runs elevated and may be used by several administrators.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AcmeDeck");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Full path to wacs.exe. Empty means auto-detect.</summary>
    public string? WacsPath { get; set; }

    /// <summary>Contact address for the ACME account, pre-filled in the wizard.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Allows checks that contact external services: public DNS lookups via
    /// dns.google and the server's public IP via api.ipify.org.
    /// </summary>
    public bool OnlineChecks { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch
        {
            // A damaged settings file must not prevent the tool from starting.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
